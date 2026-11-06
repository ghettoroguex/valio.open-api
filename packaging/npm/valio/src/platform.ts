// Shared platform resolution for the ValIO npm wrapper.
//
// The wrapper package ships no binary of its own. Instead it declares one
// optional dependency per supported platform, each carrying a single prebuilt
// executable, and npm installs only the one matching the host's os/cpu.
//
// Stable packages use @val.io; preview packages on GitHub Packages use the
// repository owner's scope. The package name is discovered at runtime from the
// wrapper's own `optionalDependencies`. That manifest is stamped at publish time
// (see .github/scripts/prepare-npm.mjs) and is the single source of truth for
// which packages this build expects.

import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";

const require = createRequire(import.meta.url);

/**
 * Identifies the per-platform package that carries the binary for the current
 * host, along with the file name of the executable inside it.
 */
export interface PlatformInfo {
  /** Platform key in `<os>-<arch>` form, e.g. `"linux-x64"`. */
  key: string;
  /** Fully qualified npm package name, e.g. `"@val.io/cli-linux-x64"`. */
  pkg: string;
  /** File name of the executable inside the package. */
  exe: string;
}

/**
 * Platform keys ValIO ships prebuilt binaries for. Kept in sync with
 * packaging/npm/platforms/* and the PLATFORMS list in
 * .github/scripts/prepare-npm.mjs.
 */
const SUPPORTED = new Set([
  "win32-x64",
  "linux-x64",
  "linux-arm64",
  "darwin-x64",
  "darwin-arm64",
]);

/**
 * Scope assumed when the wrapper's own manifest cannot be read or declares no
 * optional dependencies. This is only used to build a human-readable error
 * message, so a damaged install still names a plausible package instead of
 * something obviously wrong.
 */
const FALLBACK_SCOPE = "@val.io";

/**
 * Reads the wrapper's own package.json.
 *
 * Compiled output lives in `dist/`, so the manifest sits one directory up. The
 * file is read rather than imported because JSON imports would require import
 * attributes and pin the module format unnecessarily.
 *
 * @returns The parsed manifest, or null when it cannot be read or parsed.
 */
function readOwnManifest(): Record<string, unknown> | null {
  try {
    const raw = readFileSync(new URL("../package.json", import.meta.url), "utf8");
    return JSON.parse(raw) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/**
 * Finds the declared optional dependency whose name ends with
 * `cli-<key>`, which is how every per-platform package is named regardless of
 * the scope it was published under.
 *
 * @param key Platform key such as `"darwin-arm64"`.
 * @returns The declared package name, or null when the manifest is unavailable
 *   or does not declare this platform.
 */
function findDeclaredPackage(key: string): string | null {
  const manifest = readOwnManifest();
  const optional = manifest?.optionalDependencies;
  if (!optional || typeof optional !== "object") return null;

  const suffix = `cli-${key}`;
  for (const name of Object.keys(optional as Record<string, string>)) {
    // Match `@scope/cli-linux-x64` as well as a hypothetical unscoped
    // `cli-linux-x64`, without matching `cli-linux-x64-something`.
    if (name === suffix || name.endsWith(`/${suffix}`)) return name;
  }
  return null;
}

/**
 * Determines which per-platform package serves the current host.
 *
 * @returns Platform details, or null when ValIO ships no prebuilt binary for
 *   this os/cpu combination.
 *
 * @example
 * ```ts
 * const info = resolvePlatform();
 * if (info) console.log(`binary comes from ${info.pkg}`);
 * ```
 */
export function resolvePlatform(): PlatformInfo | null {
  const key = `${process.platform}-${process.arch}`;
  if (!SUPPORTED.has(key)) return null;

  const exe = process.platform === "win32" ? "valio.exe" : "valio";
  const pkg = findDeclaredPackage(key) ?? `${FALLBACK_SCOPE}/cli-${key}`;
  return { key, pkg, exe };
}

/**
 * Outcome of looking for the platform binary on disk.
 *
 * The two failure cases are kept apart deliberately: a missing package means
 * the user opted out of optional dependencies and can fix it by reinstalling,
 * whereas a missing file inside an installed package points at a corrupt or
 * partially extracted download. Callers surface different advice for each.
 */
export type BinaryLocation =
  /** The executable exists and is ready to spawn. */
  | { status: "ok"; path: string }
  /** The per-platform package is not installed at all. */
  | { status: "package-missing" }
  /** The package is installed but does not contain the executable. */
  | { status: "binary-missing"; path: string };

/**
 * Locates the executable on disk by resolving the per-platform package's
 * manifest and joining the binary name to its directory.
 *
 * Resolution goes through `require.resolve` so npm's own layout rules apply,
 * including hoisting and nested `node_modules`.
 *
 * @param info Platform details from {@link resolvePlatform}.
 * @returns A {@link BinaryLocation} describing success or the precise failure.
 *
 * @see resolvePlatform
 */
export function locateBinary(info: PlatformInfo): BinaryLocation {
  let manifestPath: string;
  try {
    manifestPath = require.resolve(`${info.pkg}/package.json`);
  } catch {
    return { status: "package-missing" };
  }

  const path = join(dirname(manifestPath), info.exe);
  return existsSync(path) ? { status: "ok", path } : { status: "binary-missing", path };
}
