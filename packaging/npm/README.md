# ValIO npm distribution

The public CLI is `@val.io/valio`. It installs a self-contained executable from
one matching optional dependency, `@val.io/cli-<platform>`, so consumers need
Node.js 18+ and do not need a .NET runtime. Supported platforms are Windows x64,
Linux x64/arm64 and macOS x64/arm64. The installed command is `valio`.

GitHub Packages preview builds use `@valio-projects/valio` and
`@valio-projects/cli-<platform>` because that registry requires the repository
owner as its scope. The launcher reads its own `optionalDependencies` and works
with either scope.

## Build from a clean checkout

```sh
cd packaging/npm
npm ci --ignore-scripts
npm run check
npm run build
```

The private `packaging/npm/package.json` and its lockfile contain only build
tools. The published wrapper manifest lives in `valio/package.json`; native
manifests live under `platforms/`. Keeping build dependencies separate lets CI
build the first release while no native packages exist on the registry yet.
Do not run `npm ci` in `valio/`.

## Release pipeline

`.github/workflows/release.yml` runs the full CI suite, packs NuGet artifacts,
and cross-publishes five self-contained single-file CLI executables:

```sh
dotnet publish src/ValIO.Cli -r <rid> -c Release \
  --self-contained -p:PublishSingleFile=true
```

`.github/scripts/prepare-npm.mjs` stamps the channel's scope, version, exact
optional dependency versions, repository metadata and license text. Every
native package is published before the wrapper. A repeated release skips
versions that already exist.

Public publishing runs on a GitHub-hosted runner with Node.js 24, npm 11 and
`id-token: write`. Configure an npm trusted publisher for each of the six
packages: GitHub owner `valio-projects`, repository `valio.open-api`, workflow
`release.yml`, environment `production`. For the first release, before the
packages exist, provide a granular `NPM_TOKEN` with permission to publish new
packages in `@val.io`. The token remains an optional bootstrap fallback;
subsequent releases can use OIDC. GitHub Packages previews use `GITHUB_TOKEN`.
See [npm's trusted publisher documentation](https://docs.npmjs.com/trusted-publishers/).

## Verify packaging locally

From the repository root, after building the wrapper and a self-contained
single-file executable for the current host:

```sh
node --test .github/scripts/release-scripts.test.mjs
node .github/scripts/smoke-npm.mjs /absolute/path/to/valio
```

The smoke check packs the real wrapper and native package into temporary
tarballs, installs them offline into a clean consumer, runs postinstall, and
invokes the packaged `valio --help`. It fails if the binary, launcher output,
native package name, or bundled .NET runtime is missing.
