#!/usr/bin/env bash
# Verify the freshly packed tool without using an existing registry version or
# a populated NuGet cache. All tool dependencies must be inside its package.
set -euo pipefail
package_dir="$(cd "${1:?Expected package directory}" && pwd)"
version="${2:?Expected package version}"
smoke_dir="$(mktemp -d)"
trap 'rm -rf -- "$smoke_dir"' EXIT
cat > "$smoke_dir/nuget.config" <<'XML'
<configuration><packageSources><clear /></packageSources></configuration>
XML
NUGET_PACKAGES="$smoke_dir/packages" dotnet tool install ValIO.Cli \
  --tool-path "$smoke_dir/tool" --version "$version" \
  --configfile "$smoke_dir/nuget.config" --add-source "$package_dir"
"$smoke_dir/tool/valio" --help
