#!/usr/bin/env node
// -----------------------------------------------------------------------------
// prepare-npm.mjs — stamps the npm package manifests for one publish channel.
//
// ValIO ships as a thin wrapper package plus five per-platform packages that
// each carry a single prebuilt binary:
//
//   @<owner>/valio               the wrapper; installs the `valio` command
//   @<owner>/cli-<platform>      one prebuilt binary, gated by os/cpu
//
// Stable packages use the public npm organization @val.io. Preview packages
// use @valio-projects because GitHub Packages requires the repository owner as
// the scope. The runtime reads optionalDependencies so both scopes resolve.
//
// What this script stamps per channel: version, registry, the pinned
// `optionalDependencies` map, the repository URL (GitHub Packages validates it)
// and a copy of the licence. Because the wrapper resolves its binary through its
// own `optionalDependencies` at runtime (see packaging/npm/valio/src/platform.ts),
// rewriting the manifests here is sufficient — no source changes are needed to
// follow a rename.
//
// Usage:
//   node .github/scripts/prepare-npm.mjs \
//     --version 1.2.0-rc.7 \
//     --scope @valio-projects \
//     --main-name @valio-projects/valio \
//     --registry https://npm.pkg.github.com
// -----------------------------------------------------------------------------

import { copyFileSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

/**
 * Platform directory names under packaging/npm/platforms. Each one becomes the
 * `cli-<platform>` suffix of a per-platform package.
 *
 * @type {readonly string[]}
 */
const PLATFORMS = ["win32-x64", "linux-x64", "linux-arm64", "darwin-x64", "darwin-arm64"];

/** Path of the wrapper package relative to the repository root. */
const WRAPPER_DIR = "packaging/npm/valio";

/** Path of the per-platform package directory relative to the repository root. */
const PLATFORMS_DIR = "packaging/npm/platforms";

/**
 * Parses `--key value` command line arguments into a plain object.
 *
 * @param {string[]} argv Raw arguments (typically `process.argv.slice(2)`).
 * @returns {Record<string, string>} Mapping of flag name to value.
 * @throws {Error} If a flag is supplied without a value.
 */
function parseArgs(argv) {
  /** @type {Record<string, string>} */
  const out = {};
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i];
    if (!key.startsWith("--")) {
      throw new Error(`Expected a --flag but got '${key}'.`);
    }
    const value = argv[i + 1];
    if (value === undefined) {
      throw new Error(`Flag '${key}' is missing a value.`);
    }
    out[key.slice(2)] = value;
  }
  return out;
}

/**
 * Reads a JSON file into an object.
 *
 * @param {string} path Absolute path to the file.
 * @returns {Record<string, unknown>} The parsed document.
 */
function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

/**
 * Writes an object back as pretty-printed JSON with a trailing newline, matching
 * the formatting npm itself produces so diffs stay readable.
 *
 * @param {string} path Absolute path to the file.
 * @param {unknown} value Document to serialise.
 * @returns {void}
 */
function writeJson(path, value) {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

/**
 * Copies the repository's LICENSE next to a package manifest.
 *
 * Every manifest declares `"license": "LGPL-2.1-only"`, but npm only ships the licence
 * text when the file is inside the package directory — and it is kept at the
 * repository root, not duplicated into six subfolders. npm always includes a
 * file named LICENSE regardless of the `files` allowlist, so copying it here is
 * all that is required.
 *
 * @param {string} directory Package directory relative to the repository root.
 * @returns {void}
 */
function copyLicense(directory) {
  const source = resolve(process.cwd(), "LICENSE");
  if (!existsSync(source)) return;
  copyFileSync(source, resolve(process.cwd(), directory, "LICENSE"));
}

/**
 * Derives the canonical repository URL for the checkout currently being built.
 *
 * GitHub Packages maps every npm package to a repository through the manifest's
 * `repository` field and refuses the upload when it points somewhere else. A
 * stale URL left over from a rename is therefore a hard publish failure, so the
 * value is regenerated from the workflow environment whenever it is available
 * instead of trusting what is committed.
 *
 * @returns {string | null} A `git+https://…​.git` URL, or null outside Actions.
 */
function currentRepositoryUrl() {
  const repo = process.env.GITHUB_REPOSITORY;
  if (!repo) return null;
  const server = process.env.GITHUB_SERVER_URL || "https://github.com";
  return `git+${server}/${repo}.git`;
}

/**
 * Points a manifest's `repository` entry at the repository being built, keeping
 * any existing `directory` hint so npm still links to the right subfolder.
 *
 * @param {Record<string, any>} pkg Manifest being stamped, mutated in place.
 * @param {string} directory Path of the package relative to the repo root.
 * @returns {void}
 */
function stampRepository(pkg, directory) {
  const url = currentRepositoryUrl();
  if (!url) return;
  pkg.repository = { type: "git", url, directory };
}

/**
 * Rewrites the wrapper manifest: its own name and version, the full
 * `optionalDependencies` map (names follow the scope, versions are pinned
 * exactly to this build), and the registry it publishes to.
 *
 * @param {object} opts Channel parameters.
 * @param {string} opts.version Version to stamp on every package.
 * @param {string} opts.scope npm scope for the per-platform packages, e.g. `@valio`.
 * @param {string} opts.mainName Name of the wrapper package itself.
 * @param {string} opts.registry Registry URL the packages are published to.
 * @returns {void}
 */
function stampWrapper({ version, scope, mainName, registry }) {
  const path = resolve(process.cwd(), WRAPPER_DIR, "package.json");
  const pkg = readJson(path);

  pkg.name = mainName;
  pkg.version = version;

  // Rebuild rather than patch: this removes stale entries if PLATFORMS changes
  // and guarantees every optional dependency is pinned to the current build.
  pkg.optionalDependencies = Object.fromEntries(
    PLATFORMS.map((platform) => [`${scope}/cli-${platform}`, version]),
  );

  pkg.publishConfig = { registry, access: "public" };
  stampRepository(pkg, WRAPPER_DIR);
  copyLicense(WRAPPER_DIR);

  writeJson(path, pkg);
  console.log(`wrapper: ${mainName}@${version} -> ${registry}`);
}

/**
 * Rewrites the five per-platform manifests to the channel's scope and version.
 *
 * @param {object} opts Channel parameters.
 * @param {string} opts.version Version to stamp on every package.
 * @param {string} opts.scope npm scope for the per-platform packages.
 * @param {string} opts.registry Registry URL the packages are published to.
 * @returns {void}
 */
function stampPlatforms({ version, scope, registry }) {
  for (const platform of PLATFORMS) {
    const path = resolve(process.cwd(), PLATFORMS_DIR, platform, "package.json");
    const pkg = readJson(path);

    pkg.name = `${scope}/cli-${platform}`;
    pkg.version = version;
    pkg.publishConfig = { registry, access: "public" };
    stampRepository(pkg, `${PLATFORMS_DIR}/${platform}`);
    copyLicense(`${PLATFORMS_DIR}/${platform}`);

    writeJson(path, pkg);
    console.log(`platform: ${pkg.name}@${version} -> ${registry}`);
  }
}

/**
 * Entry point: validates arguments and stamps every manifest.
 *
 * @returns {void}
 * @throws {Error} If a required flag is missing or the scope is malformed.
 */
function main() {
  const args = parseArgs(process.argv.slice(2));
  const required = ["version", "scope", "main-name", "registry"];
  for (const key of required) {
    if (!args[key]) throw new Error(`Missing required flag --${key}.`);
  }
  if (!/^@[a-z0-9][a-z0-9._-]*$/.test(args.scope)) {
    throw new Error(`Invalid npm scope '${args.scope}'.`);
  }
  if (args["main-name"] !== `${args.scope}/valio`) {
    throw new Error("--main-name must be the valio package in --scope.");
  }
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(args.version)) {
    throw new Error(`Invalid package version '${args.version}'.`);
  }
  if (!["https://registry.npmjs.org", "https://npm.pkg.github.com"].includes(args.registry)) {
    throw new Error(`Unsupported npm registry '${args.registry}'.`);
  }

  const opts = {
    version: args.version,
    scope: args.scope,
    mainName: args["main-name"],
    registry: args.registry,
  };

  stampWrapper(opts);
  stampPlatforms(opts);
}

main();
