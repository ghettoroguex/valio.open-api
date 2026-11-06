import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const platforms = ["win32-x64", "linux-x64", "linux-arm64", "darwin-x64", "darwin-arm64"];

function fixture(t) {
  const directory = mkdtempSync(join(tmpdir(), "valio-release-test-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  writeFileSync(join(directory, "VERSION"), "1.2.3\n");
  return directory;
}

function compute(cwd, ref, event = "push") {
  const result = spawnSync(process.execPath, [join(root, ".github/scripts/compute-version.mjs")], {
    cwd,
    encoding: "utf8",
    env: {
      ...process.env,
      GITHUB_REF_NAME: ref,
      GITHUB_EVENT_NAME: event,
      GITHUB_RUN_NUMBER: "42",
      GITHUB_REPOSITORY_OWNER: "valio-projects",
      PR_NUMBER: "17",
      GITHUB_OUTPUT: "",
      GITHUB_STEP_SUMMARY: "",
    },
  });
  assert.equal(result.status, 0, result.stderr);
  return Object.fromEntries(result.stdout.trim().split(/\r?\n/).slice(1).map(line => {
    const split = line.indexOf("=");
    return [line.slice(0, split), line.slice(split + 1)];
  }));
}

test("only main publishes stable packages to the public organizations", t => {
  const cwd = fixture(t);
  const stable = compute(cwd, "main");
  assert.equal(stable.version, "1.2.3");
  assert.equal(stable.publish, "true");
  assert.equal(stable["npm-main-name"], "@val.io/valio");
  assert.equal(stable["npm-registry"], "https://registry.npmjs.org");
  assert.equal(stable["nuget-source"], "https://api.nuget.org/v3/index.json");
  assert.equal(stable.tag, "v1.2.3");
  for (const ref of ["master", "feature/new-generator", "main-extra"]) {
    assert.equal(compute(cwd, ref).publish, "false", ref);
  }
  assert.equal(compute(cwd, "main", "pull_request").publish, "false");
});

test("preview versions and names remain compatible with GitHub Packages", t => {
  const cwd = fixture(t);
  for (const [ref, version, tag] of [
    ["develop", "1.2.3-alpha.42", "dev"],
    ["release/v2.4", "2.4.0-rc.42", "next"],
    ["release/bugfix", "1.2.3-rc.42", "next"],
  ]) {
    const result = compute(cwd, ref);
    assert.equal(result.version, version);
    assert.equal(result["npm-main-name"], "@valio-projects/valio");
    assert.equal(result["npm-registry"], "https://npm.pkg.github.com");
    assert.equal(result["npm-tag"], tag);
    assert.equal(result.publish, "true");
    assert.equal(result["is-stable"], "false");
  }
});

for (const [scope, registry] of [
  ["@val.io", "https://registry.npmjs.org"],
  ["@valio-projects", "https://npm.pkg.github.com"],
]) {
  test(`stamping ${scope} preserves binary gates and pins every platform dependency`, t => {
    const cwd = fixture(t);
    const directories = ["packaging/npm/valio", ...platforms.map(p => `packaging/npm/platforms/${p}`)];
    writeFileSync(join(cwd, "LICENSE"), "test license\n");
    for (const directory of directories) {
      mkdirSync(join(cwd, directory), { recursive: true });
      writeFileSync(join(cwd, directory, "package.json"), readFileSync(join(root, directory, "package.json")));
    }
    const result = spawnSync(process.execPath, [join(root, ".github/scripts/prepare-npm.mjs"),
      "--version", "2.0.0-rc.42", "--scope", scope, "--main-name", `${scope}/valio`, "--registry", registry], {
      cwd, encoding: "utf8", env: { ...process.env, GITHUB_REPOSITORY: "valio-projects/valio.open-api", GITHUB_SERVER_URL: "https://github.com" },
    });
    assert.equal(result.status, 0, result.stderr);
    const wrapper = JSON.parse(readFileSync(join(cwd, directories[0], "package.json")));
    assert.equal(wrapper.name, `${scope}/valio`);
    assert.deepEqual(wrapper.optionalDependencies, Object.fromEntries(platforms.map(p => [`${scope}/cli-${p}`, "2.0.0-rc.42"])));
    for (const platform of platforms) {
      const directory = `packaging/npm/platforms/${platform}`;
      const pkg = JSON.parse(readFileSync(join(cwd, directory, "package.json")));
      const original = JSON.parse(readFileSync(join(root, directory, "package.json")));
      assert.equal(pkg.name, `${scope}/cli-${platform}`);
      assert.equal(pkg.version, wrapper.version);
      assert.equal(pkg.publishConfig.registry, registry);
      assert.deepEqual(pkg.os, original.os);
      assert.deepEqual(pkg.cpu, original.cpu);
      assert.ok(pkg.files.includes(platform.startsWith("win32") ? "valio.exe" : "valio"));
      assert.equal(pkg.repository.url, "git+https://github.com/valio-projects/valio.open-api.git");
      assert.equal(readFileSync(join(cwd, directory, "LICENSE"), "utf8"), "test license\n");
    }
  });
}
