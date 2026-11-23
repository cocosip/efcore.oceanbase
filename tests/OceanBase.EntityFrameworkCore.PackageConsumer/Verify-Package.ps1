#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackagePath,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedVersion,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSourceRevision,
    [ValidateNotNullOrEmpty()][string]$ExpectedPackageId = 'Sharp.OceanBase.EntityFrameworkCore'
)

$ErrorActionPreference = 'Stop'
$packageFile = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($packageFile)
try {
    $nuspecEntries = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
    if ($nuspecEntries.Count -ne 1) { throw 'Expected exactly one package nuspec.' }
    $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.package.metadata
    if ($metadata.id -cne $ExpectedPackageId -or $metadata.version -cne $ExpectedVersion) {
        throw 'Package ID or version does not match the requested provider build.'
    }

    $assembly = @($archive.Entries | Where-Object FullName -CEQ 'lib/net8.0/OceanBase.EntityFrameworkCore.dll')
    if ($assembly.Count -ne 1 -or $assembly[0].Length -eq 0) { throw 'Expected a nonempty net8.0 provider assembly.' }
    if ($ExpectedSourceRevision) {
        $assemblyFile = [System.IO.Path]::GetTempFileName()
        try {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($assembly[0], $assemblyFile, $true)
            $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyFile).ProductVersion
            if (!$productVersion -or !$productVersion.EndsWith("+$ExpectedSourceRevision", [StringComparison]::OrdinalIgnoreCase)) {
                throw "Packaged provider revision does not match $ExpectedSourceRevision. Assembly product version is '$productVersion'."
            }
        } finally {
            [System.IO.File]::Delete($assemblyFile)
        }
    }
    $groups = @($metadata.dependencies.group)
    if ($groups.Count -ne 1 -or $groups[0].targetFramework -cne 'net8.0') {
        throw 'Expected one net8.0 dependency group.'
    }
    $expectedDependencies = @{
        'Microsoft.EntityFrameworkCore' = '[8.0.0, 9.0.0)'
        'Microsoft.EntityFrameworkCore.Relational' = '[8.0.0, 9.0.0)'
        'Microsoft.Extensions.Caching.Memory' = '8.0.1'
        'OceanBaseConnector' = '0.5.0'
    }
    $dependencies = @($groups[0].dependency)
    if ($dependencies.Count -ne $expectedDependencies.Count) { throw 'Unexpected package dependencies.' }
    foreach ($expected in $expectedDependencies.GetEnumerator()) {
        $dependency = @($dependencies | Where-Object id -CEQ $expected.Key)
        if ($dependency.Count -ne 1 -or $dependency[0].version -cne $expected.Value) {
            throw "Incorrect dependency range for $($expected.Key)."
        }
    }

    if ($metadata.readme -cne 'README.md') { throw 'Package README metadata is missing.' }
    $readmeEntries = @($archive.Entries | Where-Object FullName -CEQ 'README.md')
    if ($readmeEntries.Count -ne 1) { throw 'Package README entry is missing.' }
    $reader = [System.IO.StreamReader]::new($readmeEntries[0].Open())
    try { $readme = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ([string]::IsNullOrWhiteSpace($readme)) { throw 'Package README is empty.' }

    Write-Output "${ExpectedPackageId} ${ExpectedVersion}: package content checks passed."
} finally {
    $archive.Dispose()
}
