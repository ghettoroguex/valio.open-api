// Install real npm tarballs offline and invoke the packaged native CLI. This
// catches omitted binaries, wrong package scopes, missing dist files and broken
// postinstall/launcher behavior before anything reaches a package registry.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { delimiter, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const binary = process.argv[2];
if (!binary) throw new Error("Usage: node .github/scripts/smoke-npm.mjs <native CLI binary>");
const platform = `${process.platform}-${process.arch}`;
const staging = mkdtempSync(join(tmpdir(), "valio-npm-smoke-"));
const npm = "npm";
// Invoke npm's JavaScript entry point on Windows; .cmd files require a shell,
// which would change quoting for paths containing spaces or shell characters.
const windowsNpmCli = process.platform === "win32"
  ? [dirname(process.execPath), ...(process.env.PATH || "").split(delimiter)]
    .map(directory => join(directory, "node_modules/npm/bin/npm-cli.js"))
    .find(candidate => existsSync(candidate))
  : null;

function run(command, args, cwd, json = false) {
  if (process.platform === "win32" && command === npm) {
    assert.ok(windowsNpmCli, "Could not locate npm-cli.js next to Node.js or on PATH.");
    command = process.execPath;
    args = [windowsNpmCli, ...args];
  }
  const result = spawnSync(command, args, {
    cwd, encoding: "utf8",
    env: { ...process.env, npm_config_update_notifier: "false" },
  });
  assert.equal(result.status, 0, `${command} ${args.join(" ")}\n${result.stdout}\n${result.stderr}`);
  return json ? JSON.parse(result.stdout) : result.stdout;
}

try {
  const wrapper = join(staging, "wrapper");
  const native = join(staging, "native");
  const consumer = join(staging, "consumer");
  for (const directory of [wrapper, native, consumer]) mkdirSync(directory);
  for (const file of ["package.json", "README.md"]) {
    copyFileSync(join(root, "packaging/npm/valio", file), join(wrapper, file));
  }
  cpSync(join(root, "packaging/npm/valio/dist"), join(wrapper, "dist"), { recursive: true });
  copyFileSync(join(root, "LICENSE"), join(wrapper, "LICENSE"));
  copyFileSync(join(root, `packaging/npm/platforms/${platform}/package.json`), join(native, "package.json"));
  copyFileSync(join(root, "LICENSE"), join(native, "LICENSE"));
  const nativeManifest = JSON.parse(readFileSync(join(native, "package.json")));
  const wrapperManifest = JSON.parse(readFileSync(join(wrapper, "package.json")));
  assert.equal(wrapperManifest.optionalDependencies[nativeManifest.name], nativeManifest.version);
  const executable = platform.startsWith("win32") ? "valio.exe" : "valio";
  copyFileSync(resolve(binary), join(native, executable));
  chmodSync(join(native, executable), 0o755);
  const wrapperPack = run(npm, ["pack", "--ignore-scripts", "--json"], wrapper, true)[0];
  const nativePack = run(npm, ["pack", "--ignore-scripts", "--json"], native, true)[0];
  assert.ok(wrapperPack.files.some(f => f.path === "dist/valio.js"));
  assert.ok(nativePack.files.some(f => f.path === executable));
  writeFileSync(join(consumer, "package.json"), '{"private":true}\n');
  run(npm, ["install", "--offline", "--ignore-scripts", "--omit=optional", "--package-lock=false", "--no-audit", "--no-fund",
    join(native, nativePack.filename), join(wrapper, wrapperPack.filename)], consumer);
  const installed = join(consumer, "node_modules", wrapperManifest.name);
  run(process.execPath, [join(installed, "dist/install.js")], consumer);
  const help = run(process.execPath, [join(installed, "dist/valio.js"), "--help"], consumer);
  assert.match(help, /generate/i);
  console.log(`Installed ${wrapperManifest.name}@${wrapperManifest.version} and ${nativeManifest.name} from tarballs; valio --help passed.`);
} finally {
  rmSync(staging, { recursive: true, force: true });
}
