param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$ModelsOnly,
    [string]$BuildDirectory,
    [string]$CMake
)

$ErrorActionPreference = 'Stop'
if (-not $CMake) {
    $taskInstalledCMake = Join-Path $env:ProgramFiles 'CMake\bin\cmake.exe'
    $CMake = if (Test-Path -LiteralPath $taskInstalledCMake) {
        $taskInstalledCMake
    } else {
        (Get-Command cmake -ErrorAction Stop).Source
    }
}
if (-not $BuildDirectory) {
    $taskBuildName = if ($ModelsOnly) { 'build-models' } else { 'build' }
    $BuildDirectory = Join-Path $PSScriptRoot $taskBuildName
}
$taskCtest = Join-Path (Split-Path -Parent $CMake) 'ctest.exe'
if (-not (Test-Path -LiteralPath $taskCtest)) {
    $taskCtest = (Get-Command ctest -ErrorAction Stop).Source
}
$taskFull = if ($ModelsOnly) { 'OFF' } else { 'ON' }
& $CMake -S $PSScriptRoot -B $BuildDirectory "-DVALIO_EXAMPLE_FULL=$taskFull"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed: $LASTEXITCODE" }
& $CMake --build $BuildDirectory --config $Configuration
if ($LASTEXITCODE -ne 0) { throw "C++ compilation failed: $LASTEXITCODE" }
& $taskCtest --test-dir $BuildDirectory -C $Configuration --output-on-failure
if ($LASTEXITCODE -ne 0) { throw "C++ integration checks failed: $LASTEXITCODE" }
