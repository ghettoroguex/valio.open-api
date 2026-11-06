// postinstall: makes the platform binary executable on Unix-like systems.
//
// npm installs only the per-platform optional dependency that matches the
// host's os/cpu. After install, the file ships without the executable bit on
// non-Windows hosts because npm strips perms during pack/unpack. We chmod 755
// here so `valio` works without the user having to fix it themselves.
//
// On Windows, .exe needs no chmod and PATHEXT handles it.
//
// This script must NEVER exit non-zero. A failed postinstall would break
// `npm install` for users who legitimately don't need the binary right away
// (e.g. CI caches, no-op installs) and there's no recoverable action they
// can take from here.

import { chmodSync } from "node:fs";
import { locateBinary, resolvePlatform } from "./platform.js";

/**
 * Writes a diagnostic to stderr.
 *
 * Prefixed so it's obvious where the message comes from in `npm install`
 * output, but stays on stderr and never changes the exit code.
 *
 * @param message Human-readable explanation of what could not be done.
 * @returns Nothing.
 */
function warn(message: string): void {
  process.stderr.write(`valio (postinstall): ${message}\n`);
}

/**
 * Resolves the platform binary and makes it executable, degrading to a warning
 * at every failure point.
 *
 * @returns Nothing.
 */
function main(): void {
  const info = resolvePlatform();
  if (!info) {
    warn(
      `no prebuilt binary exists for ${process.platform}-${process.arch}. ` +
        "You can still install via 'dotnet tool install --global ValIO.Cli'.",
    );
    return;
  }

  const located = locateBinary(info);

  if (located.status === "package-missing") {
    warn(
      `optional dependency '${info.pkg}' was not installed. ` +
        "Re-run 'npm install @val.io/valio' or pass --include=optional if you previously omitted optionals.",
    );
    return;
  }

  if (located.status === "binary-missing") {
    warn(
      `expected binary '${located.path}' is missing from '${info.pkg}'. ` +
        "The platform package may be corrupt — try reinstalling.",
    );
    return;
  }

  if (process.platform === "win32") {
    // .exe files don't need an executable bit on Windows.
    return;
  }

  try {
    chmodSync(located.path, 0o755);
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    warn(`failed to chmod '${located.path}': ${msg}`);
  }
}

main();
