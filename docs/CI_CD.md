# CI/CD

ValIO uses GitHub Actions to validate source changes, publish installable packages,
build the web application, and deploy documentation.

## Workflows and versions

| Workflow | Trigger | Result |
| --- | --- | --- |
| [CI](../.github/workflows/ci.yml) | PRs, ordinary branch pushes, reusable call | .NET build/tests on Linux, Windows and macOS; NuGet and native npm installation smoke; documentation build |
| [Release](../.github/workflows/release.yml) | `main`, `develop`, `release/**`, manual dispatch | Runs CI before publishing packages and the web image |
| [Web](../.github/workflows/web.yml) | Relevant PRs/ordinary branches; Release call | Builds a non-root image, tests HTTP generation, publishes from `main` |
| [Docs](../.github/workflows/docs.yml) | Documentation changes on `main`, manual dispatch on `main` | Deploys GitHub Pages |
| [CodeQL](../.github/workflows/codeql.yml) | PRs, release branches, schedule | C# and TypeScript analysis |
| [Runner diagnostics](../.github/workflows/runner.yml) | Manual dispatch on the default branch | Verifies rootless Docker, Compose and persistent deployment storage |

Release invokes CI for publishing branches, avoiding duplicate matrices on push.
Legacy `master` and feature branches validate without publishing.

The root [VERSION](../VERSION) contains `X.Y.Z`. MSBuild uses it locally;
[compute-version.mjs](../.github/scripts/compute-version.mjs) adds preview suffixes.

| Branch | Version | NuGet destination | npm package/registry | Tag |
| --- | --- | --- | --- | --- |
| `main` | `X.Y.Z` | nuget.org | `@val.io/valio`, npmjs.com | `latest` |
| `release/**` | `X.Y.Z-rc.<run>` | GitHub Packages | `@valio-projects/valio`, GitHub Packages | `next` |
| `develop` | `X.Y.Z-alpha.<run>` | GitHub Packages | `@valio-projects/valio`, GitHub Packages | `dev` |
| Other branches/PRs | CI/PR suffix | No publication | No publication | — |

Version-shaped release branch names select the RC base (`release/1.4` becomes
`1.4.0-rc.<run>`). Preview numbers use the workflow run number.

To release, merge tested changes into `main` with a new `VERSION`. The first
release can use the existing unpublished version. Registry versions are immutable:
existing versions are skipped, so changing code without bumping `VERSION` does
not update installed CLI packages. Web images use commit tags and digests and
publish independently of the CLI version. GitHub Release assets carry five native
archives and the .NET tool package.

## npm distribution and installation

The public wrapper is `@val.io/valio`. Its five exact-version optional dependencies
are `@val.io/cli-{win32-x64,linux-x64,linux-arm64,darwin-x64,darwin-arm64}`.
GitHub previews use `@valio-projects` because GitHub Packages requires its owner
as the scope. The launcher discovers its platform package from its own manifest.

Native packages publish before the wrapper. Each contains a self-contained,
single-file executable; consumers need Node.js and supported system libraries,
but do not need to install .NET. Alpine/musl and Windows ARM64 are not included.

Private build tools and the lockfile live in `packaging/npm`, separate from the
wrapper, so a fresh checkout builds before native packages exist in a registry.

```sh
npm ci --ignore-scripts --prefix packaging/npm
npm run check --prefix packaging/npm
npm run build --prefix packaging/npm
node --test .github/scripts/release-scripts.test.mjs
```

Stable installation:

```sh
npm install --global @val.io/valio
dotnet tool install --global ValIO.Cli
valio --help
```

The .NET tool targets .NET 10. Projects embedding generators can use
`dotnet add package ValIO.Generators.TypeScript` or another published library.

Previews require separate GitHub Packages read credentials:

```sh
npm config set @valio-projects:registry https://npm.pkg.github.com
npm install --global @valio-projects/valio@dev
dotnet nuget add source https://nuget.pkg.github.com/valio-projects/index.json --name valio-preview
```

Never commit registry access tokens or include them in installation examples.

## Publishing credentials

| Setting | Purpose |
| --- | --- |
| Variable or secret `NUGET_USER` | Personal NuGet username that created the trusted policy; currently `sentike161` |
| `NUGET_API_KEY` secret | Optional fallback when NuGet Trusted Publishing is unavailable |
| `NPM_TOKEN` secret | Temporary bootstrap for the first six public npm packages |
| `GITHUB_TOKEN` | Automatic credential for GitHub Packages, GHCR, tags and releases |

Jobs request their minimum permissions. Repository-wide token defaults can remain
read-only. Stable publishing uses environment `production`; previews use `preview`.

### NuGet Trusted Publishing

In the personal NuGet account, configure a policy owned by `valio-projects`:

- GitHub owner `valio-projects`, repository `valio.open-api`.
- Workflow filename `release.yml`, environment `production`.
- Allowed actions: enable **Allow npm publish**; staged publishing alone does
  not authorize this workflow's direct `npm publish` commands.
- Push new packages and versions, pattern `ValIO.*`.

Set `NUGET_USER` to the personal username, not the organization or an email.
`NuGet/login` exchanges OIDC for a short-lived push key immediately before upload.
A policy pointing to `ci.yml` does not authorize `release.yml`.
See [NuGet setup](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

### npm Trusted Publishing

npm needs an existing package before its trusted publisher can be configured.
Bootstrap all six with a short-lived granular token restricted to `@val.io`,
provided as `NPM_TOKEN`. Then configure **each** package:

- Provider GitHub Actions.
- Owner `valio-projects`, repository `valio.open-api`.
- Workflow filename `release.yml`, environment `production`.

The publish job uses GitHub-hosted runners, Node.js 24, npm 11 and
`id-token: write`. npm does not support self-hosted trusted publishers.
After a successful token-free release, revoke the bootstrap token and remove
`NPM_TOKEN` from GitHub. See [npm setup](https://docs.npmjs.com/trusted-publishers/).

## Documentation

Enable Settings → Pages → Source **GitHub Actions**. The Docs workflow reads the
actual Pages URL as `DOCS_SITE_URL`. The default deployment is
<https://valio-projects.github.io/valio.open-api/>. Builds validate all internal
links and generated HTML, including the project path prefix.

A future custom domain can be configured in Pages and DNS; the workflow derives
the new URL automatically. See [docs-site](../docs-site/README.md).

## Web image and optional hosting

The image is `ghcr.io/valio-projects/valio-web:latest`, with a source commit tag.
Publishing follows Release CI and an HTTP smoke that validates a schema and
generates a ZIP. New GHCR packages can initially be private; verify visibility
before promising anonymous pulls.

Deployment stays off unless `VALIO_WEB_DEPLOY_ENABLED=true`. Building/publishing
needs no domain or SSH credentials. Optional deployment uses a self-hosted
`valio-deploy` runner, an immutable image digest, Compose health checks and
rollback. See [deploy/web](../deploy/web/README.md).

[deploy/runner](../deploy/runner/README.md) provides a bounded Compose runner for a
dedicated rootless Docker account. Files alone do not install or register it.
Keep public PR jobs on GitHub-hosted runners; avoid the server's rootful socket.

## Release verification

Check the Actions run and resulting registries, not just a local build:

1. All required CI and web smoke jobs pass.
2. All 17 NuGet packages and six public npm packages have the expected version.
3. Clean public-registry installations launch `valio --help`.
4. GitHub Release contains the native archives and tool package.
5. Pages home, translated pages and static assets return successfully.
6. GHCR image pulls and passes `/healthz`; active hosting remains a separate opt-in.

Local equivalents:

```sh
dotnet build ValIO.sln -c Release
dotnet test ValIO.sln -c Release --filter "Category!=CompileFixture"
dotnet pack ValIO.sln -c Release -o nupkgs
```

C++ compile fixtures require native toolchains and run separately in CI. Some
runtime integration cases are opt-in or skip when external tools are absent.
The native CI job provisions pinned header libraries with Conan and UUID provider
sources at fixed commits before compiling generated C++17/20/23 clients.
