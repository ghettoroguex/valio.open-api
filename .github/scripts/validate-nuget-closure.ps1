# Every ValIO dependency must be present in the same release batch. A CLI tool
# embeds referenced assemblies, so installing the CLI alone cannot detect a
# missing library package required by consumers of ValIO.Application.
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$archives = @(Get-ChildItem -LiteralPath $directory -Filter '*.nupkg' -File)
if (!$archives.Count) { throw "No NuGet packages found in $directory" }

$packages = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $archives) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $manifests = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
        if ($manifests.Count -ne 1) { throw "Expected one root nuspec in $($file.Name)" }
        $reader = [System.IO.StreamReader]::new($manifests[0].Open())
        try { [xml]$manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }

        $metadata = $manifest.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        $id = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        $version = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        if (!$id.StartsWith('ValIO.', [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($packages.ContainsKey($id)) { throw "Duplicate package ID $id in release batch" }
        if (!$ExpectedVersion) { $ExpectedVersion = $version }
        if ($version -ne $ExpectedVersion) {
            throw "Package $id has version $version; release batch requires $ExpectedVersion"
        }
        $dependencies = @($metadata.SelectNodes("*[local-name()='dependencies']//*[local-name()='dependency']") |
            Where-Object { $_.GetAttribute('id').StartsWith('ValIO.', [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { [pscustomobject]@{ Id = $_.GetAttribute('id'); Version = $_.GetAttribute('version') } })
        $packages.Add($id, [pscustomobject]@{ Version = $version; Dependencies = $dependencies })
    }
    finally { $archive.Dispose() }
}

if (!$packages.Count) { throw 'The release batch contains no ValIO packages.' }
$errors = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $packages.GetEnumerator()) {
    foreach ($dependency in $entry.Value.Dependencies) {
        if (!$packages.ContainsKey($dependency.Id)) {
            $errors.Add("$($entry.Key) requires missing package $($dependency.Id) $($dependency.Version)")
            continue
        }
        # Project references in this repository share one build version. Accept
        # the equivalent NuGet exact or inclusive-minimum forms emitted by pack.
        $range = $dependency.Version -replace '\s', ''
        if ($range -notin @($ExpectedVersion, "[$ExpectedVersion]", "[$ExpectedVersion,)")) {
            $errors.Add("$($entry.Key) requires $($dependency.Id) $range; release batch version is $ExpectedVersion")
        }
    }
}
if ($errors.Count) {
    throw "Incomplete ValIO NuGet release batch:`n$($errors -join "`n")"
}
Write-Output "Validated $($packages.Count) ValIO packages at $ExpectedVersion; every ValIO dependency exists in this release batch."
