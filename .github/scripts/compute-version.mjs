#!/usr/bin/env node
// -----------------------------------------------------------------------------
// compute-version.mjs — single source of truth for "what version is this build,
// and where (if anywhere) does it get published?"
//
// The whole release pipeline is branch-driven; there is no manual version input
// and no tag-triggered build. Every publishing decision is derived here so that
// the workflow YAML stays declarative and the rules can be unit-inspected by
// running this file locally with the relevant environment variables set.
//
// Channel matrix
// --------------
//   main            -> <base>                     stable  public registry   latest
//   release/<x>     -> <rcBase>-rc.<run>          rc      GitHub Packages   next
//   develop         -> <base>-alpha.<run>         alpha   GitHub Packages   dev
//   pull_request    -> <base>-pr.<pr>.<run>       pr      (not published)   -
//   anything else   -> <base>-ci.<run>            ci      (not published)   -
//
// `<base>` is the contents of the repo-root VERSION file. For `release/*`
// branches the branch suffix wins when it parses as a semver-ish version
// (`release/1.4`, `release/v2.0.1`), which lets a release branch stabilise a
// version independently of what `main` currently carries; otherwise the VERSION
// file is used and the branch name only serves as a label in the log.
//
// Prerelease builds always embed `github.run_number`, which is monotonic per
// workflow file. That guarantees every preview upload is a fresh, ordered
// version and can never collide with an existing one in the registry.
//
// Outputs are written to $GITHUB_OUTPUT when running under Actions, and always
// echoed to stdout so a local run is self-explanatory.
// -----------------------------------------------------------------------------

import { appendFileSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

/**
 * Repository owner. GitHub Packages requires its npm scope to match this owner.
 */
const OWNER = process.env.GITHUB_REPOSITORY_OWNER || "valio-projects";

/** Public registries used for stable releases cut from the default branch. */
const PUBLIC_NUGET = "https://api.nuget.org/v3/index.json";
const PUBLIC_NPM = "https://registry.npmjs.org";

/** GitHub Packages endpoints used for every prerelease channel. */
const GH_NUGET = `https://nuget.pkg.github.com/${OWNER}/index.json`;
const GH_NPM = "https://npm.pkg.github.com";

/** Public npm organization; independent of the GitHub repository owner. */
const PUBLIC_NPM_SCOPE = "@val.io";

/** npm scope required by GitHub Packages for preview releases. */
const GH_NPM_SCOPE = `@${OWNER}`;

/**
 * Reads and validates the repo-root VERSION file.
 *
 * @returns {string} A strict `X.Y.Z` version with no prerelease or build suffix.
 * @throws {Error} If the file is missing or is not a plain three-part version.
 */
function readBaseVersion() {
  const path = resolve(process.cwd(), "VERSION");
  const raw = readFileSync(path, "utf8").trim();
  if (!/^\d+\.\d+\.\d+$/.test(raw)) {
    throw new Error(
      `VERSION must contain a plain X.Y.Z version (no prerelease suffix); got '${raw}'. ` +
        "Prerelease identifiers are appended by this script, not stored in the file.",
    );
  }
  return raw;
}

/**
 * Interprets the suffix of a `release/*` branch as a version.
 *
 * Accepts `1.4.2`, `v1.4.2` and the two-part `1.4` / `v1.4` shorthand (patch
 * defaults to 0). Anything else — `release/hotfix-auth`, `release/2026-q1` —
 * returns null so the caller falls back to the VERSION file.
 *
 * @param {string} suffix Branch text after the `release/` prefix.
 * @returns {string | null} A normalised `X.Y.Z` version, or null when the
 *   suffix is not version-shaped.
 */
function parseReleaseBranchVersion(suffix) {
  const match = /^v?(\d+)\.(\d+)(?:\.(\d+))?$/.exec(suffix.trim());
  if (!match) return null;
  const [, major, minor, patch] = match;
  return `${major}.${minor}.${patch ?? "0"}`;
}

/**
 * Converts arbitrary branch text into a semver-legal prerelease identifier:
 * alphanumerics and hyphens only, no leading/trailing/duplicate hyphens.
 *
 * @param {string} text Raw branch text.
 * @returns {string} A safe identifier, or `build` when nothing usable remains.
 */
function sanitiseIdentifier(text) {
  const cleaned = text
    .replace(/[^0-9A-Za-z-]+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^-|-$/g, "");
  return cleaned || "build";
}

/**
 * Applies the channel matrix documented at the top of this file.
 *
 * @param {object} env Selected environment values, injected for testability.
 * @param {string} env.eventName GitHub event name (`push`, `pull_request`, ...).
 * @param {string} env.refName Branch name (`GITHUB_REF_NAME`).
 * @param {string} env.runNumber Monotonic workflow run counter.
 * @param {string} env.prNumber Pull request number, when applicable.
 * @param {string} base Base version read from the VERSION file.
 * @returns {{version: string, channel: string, publish: boolean, registry: string,
 *   npmTag: string, isStable: boolean}} The resolved build identity.
 */
function resolve_(env, base) {
  const { eventName, refName, runNumber, prNumber } = env;

  if (eventName === "pull_request") {
    const id = sanitiseIdentifier(prNumber || "0");
    return {
      version: `${base}-pr.${id}.${runNumber}`,
      channel: "pr",
      publish: false,
      registry: "none",
      npmTag: "",
      isStable: false,
    };
  }

  if (refName === "main") {
    return {
      version: base,
      channel: "stable",
      publish: true,
      registry: "public",
      npmTag: "latest",
      isStable: true,
    };
  }

  if (refName.startsWith("release/")) {
    const suffix = refName.slice("release/".length);
    const rcBase = parseReleaseBranchVersion(suffix) ?? base;
    return {
      version: `${rcBase}-rc.${runNumber}`,
      channel: "rc",
      publish: true,
      registry: "github",
      npmTag: "next",
      isStable: false,
    };
  }

  if (refName === "develop") {
    return {
      version: `${base}-alpha.${runNumber}`,
      channel: "alpha",
      publish: true,
      registry: "github",
      npmTag: "dev",
      isStable: false,
    };
  }

  return {
    version: `${base}-ci.${runNumber}`,
    channel: "ci",
    publish: false,
    registry: "none",
    npmTag: "",
    isStable: false,
  };
}

/**
 * Expands a registry selector into the concrete endpoints and npm naming that
 * the publish jobs need.
 *
 * @param {string} registry Either `public`, `github`, or `none`.
 * @returns {{nugetSource: string, npmRegistry: string, npmScope: string,
 *   npmMainName: string}} Endpoint and naming details for the channel.
 */
function registryDetails(registry) {
  const npmScope = registry === "github" ? GH_NPM_SCOPE : PUBLIC_NPM_SCOPE;
  const naming = { npmScope, npmMainName: `${npmScope}/valio` };

  if (registry === "public") {
    return { ...naming, nugetSource: PUBLIC_NUGET, npmRegistry: PUBLIC_NPM };
  }
  if (registry === "github") {
    return { ...naming, nugetSource: GH_NUGET, npmRegistry: GH_NPM };
  }
  return { ...naming, nugetSource: "", npmRegistry: "" };
}

/**
 * Entry point: computes the build identity and emits it as workflow outputs.
 *
 * @returns {void}
 */
function main() {
  const base = readBaseVersion();
  const env = {
    eventName: process.env.GITHUB_EVENT_NAME || "push",
    refName: process.env.GITHUB_REF_NAME || "",
    runNumber: process.env.GITHUB_RUN_NUMBER || "0",
    prNumber: process.env.PR_NUMBER || "",
  };

  const resolved = resolve_(env, base);
  const details = registryDetails(resolved.registry);

  const outputs = {
    base,
    version: resolved.version,
    channel: resolved.channel,
    publish: String(resolved.publish),
    registry: resolved.registry,
    "npm-tag": resolved.npmTag,
    "npm-registry": details.npmRegistry,
    "npm-scope": details.npmScope,
    "npm-main-name": details.npmMainName,
    "nuget-source": details.nugetSource,
    "is-stable": String(resolved.isStable),
    tag: resolved.isStable ? `v${resolved.version}` : "",
  };

  const rendered = Object.entries(outputs)
    .map(([key, value]) => `${key}=${value}`)
    .join("\n");

  console.log(`ref=${env.refName} event=${env.eventName} run=${env.runNumber}`);
  console.log(rendered);

  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `${rendered}\n`, "utf8");
  }

  if (process.env.GITHUB_STEP_SUMMARY) {
    const summary = [
      "### Build identity",
      "",
      "| Field | Value |",
      "| --- | --- |",
      `| Version | \`${outputs.version}\` |`,
      `| Channel | \`${outputs.channel}\` |`,
      `| Publish | \`${outputs.publish}\` |`,
      `| Registry | \`${outputs.registry || "—"}\` |`,
      `| npm dist-tag | \`${outputs["npm-tag"] || "—"}\` |`,
      `| npm package | \`${outputs["npm-main-name"]}\` |`,
      "",
    ].join("\n");
    appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`, "utf8");
  }
}

main();
