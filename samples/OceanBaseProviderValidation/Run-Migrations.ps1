#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('MySql', 'Oracle', 'Both')][string]$Mode = 'Both',
    [string]$RestoreSource,
    [switch]$GenerateOnly,
    [switch]$NoTls,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
if ($Help) {
    Write-Output 'Run-Migrations.ps1 [-Mode MySql|Oracle|Both] [-RestoreSource path] [-GenerateOnly] [-NoTls]'
    return
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$modes = if ($Mode -eq 'Both') { 'MySql', 'Oracle' } else { $Mode }
$failures = @()
foreach ($selectedMode in $modes) {
    $id = [guid]::NewGuid().ToString('N')
    $prefix = 'EFT_' + $id.Substring(0, 20).ToUpperInvariant()
    $workspace = Join-Path $repo "artifacts/sample-migrations-$selectedMode-$id"
    New-Item -ItemType Directory -Path $workspace | Out-Null
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -In '.cs', '.csproj' | Copy-Item -Destination $workspace
    $project = Join-Path $workspace 'OceanBaseProviderValidation.csproj'
    $options = @('--mode', $selectedMode, '--prefix', $prefix)
    $restore = @('restore', $project)
    if ($RestoreSource) { $restore += @('--source', $RestoreSource, '-p:NuGetAudit=false') }
    $build = @('build', $project, '-c', 'Release', '--no-restore')
    $tooling = @('--project', $project, '--configuration', 'Release', '--no-build')
    try {
        Invoke-DotNet -Arguments $restore
        Invoke-DotNet -Arguments $build
        Invoke-DotNet -Arguments (@('ef', 'migrations', 'add', 'InitialJournal') + $tooling + @('--') + $options + @('--revision', '1'))
        Invoke-DotNet -Arguments $build
        Invoke-DotNet -Arguments (@('ef', 'migrations', 'add', 'AddJournalNotes') + $tooling + @('--') + $options + @('--revision', '2'))
        Invoke-DotNet -Arguments $build
        $forward = Join-Path $workspace 'forward.sql'
        $idempotent = Join-Path $workspace 'idempotent.sql'
        $rollback = Join-Path $workspace 'rollback.sql'
        Invoke-DotNet -Arguments (@('ef', 'migrations', 'script', '--output', $forward) + $tooling + @('--') + $options)
        Invoke-DotNet -Arguments (@('ef', 'migrations', 'script', '--idempotent', '--output', $idempotent) + $tooling + @('--') + $options)
        Invoke-DotNet -Arguments (@('ef', 'migrations', 'script', 'AddJournalNotes', '0', '--output', $rollback) + $tooling + @('--') + $options)
        Write-Output "PASS $selectedMode generated C# migrations, snapshot and SQL in $workspace"
        if (!$GenerateOnly) {
            if ($NoTls) { $options += '--no-tls' }
            Invoke-DotNet -Arguments (@('run', '--project', $project, '-c', 'Release', '--no-build', '--') + $options)
        }
    } catch {
        $failures += $selectedMode
        Write-Warning "FAIL $selectedMode ($workspace) $($_.Exception.Message)"
    }
}
if ($failures.Count) { throw "Migration sample failed for $($failures -join ', ')." }
