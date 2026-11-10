#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackagePath,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedVersion
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
    if ($metadata.id -cne 'OceanBase.EntityFrameworkCore' -or $metadata.version -cne $ExpectedVersion) {
        throw 'Package ID or version does not match the requested provider build.'
    }

    $assembly = @($archive.Entries | Where-Object FullName -CEQ 'lib/net10.0/OceanBase.EntityFrameworkCore.dll')
    if ($assembly.Count -ne 1 -or $assembly[0].Length -eq 0) { throw 'Expected a nonempty net10.0 provider assembly.' }
    $groups = @($metadata.dependencies.group)
    if ($groups.Count -ne 1 -or $groups[0].targetFramework -cne 'net10.0') {
        throw 'Expected one net10.0 dependency group.'
    }
    $expectedDependencies = @{
        'Microsoft.EntityFrameworkCore' = '[10.0.0, 11.0.0)'
        'Microsoft.EntityFrameworkCore.Relational' = '[10.0.0, 11.0.0)'
        'OceanBaseConnector' = '0.2.0'
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

    Write-Output "OceanBase.EntityFrameworkCore ${ExpectedVersion}: package content checks passed."
} finally {
    $archive.Dispose()
}
