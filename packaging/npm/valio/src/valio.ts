#!/usr/bin/env node
// ValIO CLI npm shim — resolves the platform-specific binary from the matching
// per-platform optional dependency and spawns it, forwarding argv, stdio, and
// the exit code.
//
// The package name of that dependency is discovered at runtime rather than
// hardcoded, because stable and preview builds are published under different
// scopes. See ./platform.ts for the details.

import { spawn } from "node:child_process";
import { locateBinary, resolvePlatform, type PlatformInfo } from "./platform.js";

/**
 * Prints an actionable diagnostic when the native binary cannot be found.
 *
 * @param info Platform details, or null when ValIO ships nothing for this host.
 * @param detail Optional extra line describing the specific failure, e.g. a
 *   package that resolved but turned out not to contain the executable.
 * @returns Nothing.
 */
function printMissingBinaryError(info: PlatformInfo | null, detail?: string): void {
  const platform = `${process.platform}-${process.arch}`;
  process.stderr.write(
    [
      `valio: failed to locate the native CLI binary for ${platform}.`,
      "",
      info
        ? `Expected to find it via the optional dependency '${info.pkg}'.`
        : `No prebuilt npm package exists for this platform (${platform}).`,
      ...(detail ? [detail] : []),
      "",
      "Possible causes:",
      "  - npm was run with --no-optional or --omit=optional.",
      "  - The install failed silently and the platform package is missing.",
      "  - You are on a platform we do not ship a prebuilt binary for.",
      "",
      "Fixes:",
      "  - Reinstall:  npm install -g @val.io/valio",
      "  - Or install via .NET:  dotnet tool install --global ValIO.Cli",
      "",
    ].join("\n"),
  );
}

/**
 * Spawns the native binary, mirroring its stdio, exit code, and termination
 * signal so the shim is transparent to callers and to shell scripts.
 *
 * @param binary Absolute path to the executable.
 * @returns Nothing; the process exits when the child does.
 */
function run(binary: string): void {
  const child = spawn(binary, process.argv.slice(2), {
    stdio: "inherit",
    // Don't use a shell — pass args verbatim to the native binary.
    shell: false,
    windowsHide: true,
  });

  child.on("error", (err) => {
    process.stderr.write(`valio: failed to spawn '${binary}': ${err.message}\n`);
    process.exit(1);
  });

  child.on("exit", (code, signal) => {
    if (signal) {
      // Re-raise the signal so the caller sees the same termination cause.
      process.kill(process.pid, signal);
      return;
    }
    process.exit(code ?? 0);
  });

  // Forward common termination signals to the child so Ctrl-C etc. work.
  const forward: NodeJS.Signals[] = ["SIGINT", "SIGTERM", "SIGHUP"];
  for (const sig of forward) {
    process.on(sig, () => {
      if (!child.killed) child.kill(sig);
    });
  }
}

/**
 * Entry point: locates the binary for this host and hands control to it.
 *
 * @returns Nothing.
 */
function main(): void {
  const info = resolvePlatform();
  if (!info) {
    printMissingBinaryError(null);
    process.exit(1);
  }

  const located = locateBinary(info);
  if (located.status === "package-missing") {
    printMissingBinaryError(info);
    process.exit(1);
  }
  if (located.status === "binary-missing") {
    printMissingBinaryError(info, `The package is installed but '${located.path}' does not exist.`);
    process.exit(1);
  }

  run(located.path);
}

main();
