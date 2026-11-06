---
title: Install
description: Multiple ways to install the ValIO CLI on Windows, macOS, and Linux.
---

The ValIO CLI is a single self-contained .NET 10 executable. Pick the
distribution channel that matches your toolchain.

## Via `dotnet tool` (recommended)

Requires the .NET 10 SDK (or runtime). Works on every OS .NET supports.

```bash
dotnet tool install --global ValIO.Cli
```

Update:

```bash
dotnet tool update --global ValIO.Cli
```

Uninstall:

```bash
dotnet tool uninstall --global ValIO.Cli
```

Pin per-repo via a `dotnet-tools.json` manifest so contributors all
run the same version:

```bash
dotnet new tool-manifest
dotnet tool install ValIO.Cli
```

Then commit `.config/dotnet-tools.json` and the team runs
`dotnet tool restore` before `dotnet valio …`.

## Via `npm`

For repos that already manage their toolchain with npm (frontend
monorepos, GitHub Actions jobs that set up Node anyway):

```bash
npm install -g @val.io/valio
# or as a dev dependency
npm install --save-dev @val.io/valio
```

The npm wrapper installs the matching platform package as an optional dependency.
Each platform package contains a self-contained .NET 10 executable, so this
installation does not require a system-wide .NET runtime.

## Via GitHub Releases

Pre-built binaries are attached to every tagged release at
<https://github.com/valio-projects/valio.open-api/releases>. Pick the archive for your
platform:

| File                          | Platform                |
|-------------------------------|-------------------------|
| `valio-<ver>-win32-x64.zip`   | Windows x64             |
| `valio-<ver>-darwin-x64.tar.gz`  | macOS Intel          |
| `valio-<ver>-darwin-arm64.tar.gz`| macOS Apple Silicon  |
| `valio-<ver>-linux-x64.tar.gz`| Linux x64               |
| `valio-<ver>-linux-arm64.tar.gz`| Linux ARM64           |

Extract anywhere on your `PATH`. The archives are self-contained — no
.NET SDK needed.

## Planned package managers

These are not yet published; track <https://github.com/valio-projects/valio.open-api/issues>
for progress.

| Tool   | Status   | Command (when released)        |
|--------|----------|--------------------------------|
| Homebrew | planned | `brew install valio`         |
| Scoop    | planned | `scoop install valio`        |
| winget   | planned | `winget install valio`       |
| Chocolatey | planned | `choco install valio`      |

## Per-OS notes

### Windows

- The `dotnet tool` installs into `%USERPROFILE%\.dotnet\tools`. That
  folder is added to `PATH` by the .NET 10 SDK installer; if you
  installed via Chocolatey/Scoop, restart the shell so `PATH` picks up
  the change.
- If `valio` isn't recognized, run `dotnet tool list -g` to confirm
  the install, then check `where valio` to see what shadows it.
- The GitHub Release `.zip` includes `valio.exe`; right-click →
  Properties → Unblock if Windows SmartScreen flagged the download.

### macOS

- The `dotnet tool` installs into `~/.dotnet/tools`. Add to your shell
  profile if missing:
  ```bash
  echo 'export PATH="$HOME/.dotnet/tools:$PATH"' >> ~/.zshrc
  ```
- macOS Gatekeeper will quarantine unsigned binaries from GitHub
  Releases. The first run shows *"valio cannot be opened"*. Open
  System Settings → Privacy & Security → click "Open Anyway".
  Releases will be notarized once Apple Developer setup lands.
- Apple Silicon users: pick `darwin-arm64`. Intel builds work under
  Rosetta but are slower.

### Linux

- The `dotnet tool` installs into `~/.dotnet/tools`. Most distros
  *do not* add it to `PATH` by default. Append once:
  ```bash
  echo 'export PATH="$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc
  ```
- Make sure the .NET 10 runtime is installed (`apt`/`dnf`/`zypper`
  packages or Microsoft's tarball). The `dotnet-tool` path is
  framework-dependent.
- For a self-contained binary, prefer the GitHub Release tarball — it
  bundles the .NET runtime so you don't need it system-wide.

## Verifying

```bash
valio --version
valio --help
valio validate -s https://petstore3.swagger.io/api/v3/openapi.json
```

If `validate` prints the API metadata, you're ready to
[Get started](/getting-started/).
