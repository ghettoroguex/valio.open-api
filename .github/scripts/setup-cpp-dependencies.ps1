# Install the actual pinned provider dependencies used by every native fixture.
# Both configurations share one CMakeDeps prefix: the main fixture uses Debug,
# while scalar-provider acceptance builds Release.
param([string]$OutputDirectory = 'artifacts/cpp-ci-dependencies')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory, $root)
$prefix = Join-Path $output 'cmake'
New-Item -ItemType Directory -Path $prefix -Force | Out-Null

$settings = @()
if ($IsWindows) {
    # Discover the installed MSVC toolchain rather than assuming a VS release.
    # Ninja works across VS versions once the developer environment is imported.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$installation) { throw 'No Visual Studio C++ toolchain was found.' }
    $developerCommand = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
    $before = [Environment]::GetEnvironmentVariables('Process')
    $developerEnvironment = & $env:ComSpec /d /s /c "`"$developerCommand`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    foreach ($line in $developerEnvironment) {
        if ($line -notmatch '^([^=]+)=(.*)$') { continue }
        $name, $value = $Matches[1], $Matches[2]
        if ($name -match '^(GITHUB_|RUNNER_)' -or $name -eq 'NODE_OPTIONS') { continue }
        if ($before[$name] -eq $value) { continue }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        $settings += "$name=$value"
    }
    $settings += 'CC=cl', 'CXX=cl'
    Write-Output "Native fixtures use MSVC from $installation with Ninja."
}

conan profile detect --name valio-ci --force
if ($IsWindows) {
    # Conan discovers MSVC through Visual Studio; CMake's Ninja compiler selection
    # additionally needs explicit CC/CXX when other compiler families are on PATH.
    $env:CC = 'cl'
    $env:CXX = 'cl'
}
foreach ($configuration in @('Debug', 'Release')) {
    conan install (Join-Path $PSScriptRoot 'cpp-dependencies') `
        --output-folder $prefix --profile:host valio-ci --profile:build valio-ci `
        --settings "build_type=$configuration" --build missing
}

# These providers are added as real upstream CMake subdirectories by the fixture.
$providers = @(
    @{ Name = 'stduuid'; Repository = 'https://github.com/mariusbancila/stduuid.git'; Revision = 'daedc9a0029efec422d252e4152a776285a68006'; Variable = 'VALIO_CPP_STDUUID_SOURCE' },
    @{ Name = 'modern-uuid'; Repository = 'https://github.com/gershnik/modern-uuid.git'; Revision = '008d03d9d749bab064add9c93a30f4aa249baa2c'; Variable = 'VALIO_CPP_MODERN_UUID_SOURCE' }
)
$settings += "VALIO_CPP_CMAKE_PREFIX_PATH=$prefix", "VALIO_CPP_SCALAR_CMAKE_PREFIX_PATH=$prefix"
foreach ($provider in $providers) {
    $source = Join-Path $output $provider.Name
    if (!(Test-Path (Join-Path $source '.git'))) {
        git init $source
        git -C $source remote add origin $provider.Repository
    }
    git -C $source fetch --depth 1 origin $provider.Revision
    git -C $source checkout --detach FETCH_HEAD
    $settings += "$($provider.Variable)=$source"
}

if ($env:GITHUB_ENV) {
    $settings | Out-File -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Append
} else {
    foreach ($setting in $settings) {
        $name, $value = $setting.Split('=', 2)
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}
# Environment imports can contain user-specific paths; print only provider locations.
$settings | Where-Object { $_ -like 'VALIO_CPP_*' } | ForEach-Object { Write-Output $_ }
