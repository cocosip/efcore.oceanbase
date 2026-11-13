#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ProviderVersion,
    [Parameter(Mandatory)][ValidateSet('10.0.0', '10.0.14')][string]$EfCoreVersion,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackageFeed,
    [switch]$SkipBundle
)

$ErrorActionPreference = 'Stop'
$toolingRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$toolingWorkspace = Join-Path $toolingRoot "artifacts/tooling-$EfCoreVersion-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $toolingWorkspace | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Destination $toolingWorkspace
$toolingProject = Join-Path $toolingWorkspace 'ToolingConsumer.csproj'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OceanBase.EntityFrameworkCore.ToolingConsumer.csproj') -Destination $toolingProject
[xml]$toolingProjectXml = Get-Content $toolingProject -Raw
$toolingProjectXml.Project.PropertyGroup.OceanBaseProviderVersion.InnerText = $ProviderVersion
$toolingProjectXml.Project.PropertyGroup.OceanBaseConsumerEfCoreVersion.InnerText = $EfCoreVersion
$toolingProjectXml.Save($toolingProject)
[xml]$toolingNuGet = '<configuration><packageSources><clear /></packageSources><config /></configuration>'
foreach ($toolingSource in @((Resolve-Path $PackageFeed).Path, (Join-Path $env:USERPROFILE '.nuget/packages'))) {
    $toolingEntry = $toolingNuGet.CreateElement('add')
    $toolingEntry.SetAttribute('key', "source$($toolingNuGet.configuration.packageSources.ChildNodes.Count)")
    $toolingEntry.SetAttribute('value', $toolingSource)
    $null = $toolingNuGet.configuration.packageSources.AppendChild($toolingEntry)
}
$toolingCache = $toolingNuGet.CreateElement('add')
$toolingCache.SetAttribute('key', 'globalPackagesFolder')
$toolingCache.SetAttribute('value', (Join-Path $toolingRoot 'artifacts/tooling-packages'))
$null = $toolingNuGet.SelectSingleNode('/configuration/config').AppendChild($toolingCache)
$toolingNuGet.Save((Join-Path $toolingWorkspace 'NuGet.Config'))
$toolingProperties = @("-p:OceanBaseProviderVersion=$ProviderVersion", "-p:OceanBaseConsumerEfCoreVersion=$EfCoreVersion")
$toolingStep = 0

function Invoke-ToolingDotNet([string]$Description, [string[]]$Arguments, [int]$ExpectedExit = 0) {
    $script:toolingStep++
    # EF builds bundles in a temporary project, outside the consumer's NuGet.Config discovery path.
    $toolingPreviousConfig = $env:RestoreConfigFile
    $toolingPreviousCache = $env:NUGET_PACKAGES
    try {
        $env:RestoreConfigFile = Join-Path $toolingWorkspace 'NuGet.Config'
        $env:NUGET_PACKAGES = Join-Path $toolingRoot 'artifacts/tooling-packages'
        $toolingOutput = & dotnet @Arguments 2>&1
        $toolingExit = $LASTEXITCODE
    } finally {
        $env:RestoreConfigFile = $toolingPreviousConfig
        $env:NUGET_PACKAGES = $toolingPreviousCache
    }
    $toolingText = $toolingOutput -join "`n"
    $toolingText | Set-Content (Join-Path $toolingWorkspace "$($script:toolingStep)-$Description.log")
    if ($toolingExit -ne $ExpectedExit) { throw "$Description exited $toolingExit, expected $ExpectedExit. $toolingText" }
    Write-Host "$EfCoreVersion/$Description passed."
    return $toolingText
}

function Build-ToolingProject {
    $null = Invoke-ToolingDotNet 'build' (@('build', $toolingProject, '--no-restore', '--nologo', '-m:1', '-nr:false') + $toolingProperties)
}

$null = Invoke-ToolingDotNet 'restore' (@('restore', $toolingProject, '--source', (Resolve-Path $PackageFeed).Path,
    '--source', (Join-Path $env:USERPROFILE '.nuget/packages'), '--disable-parallel') + $toolingProperties)
Build-ToolingProject
$toolingAssets = Get-Content (Join-Path $toolingWorkspace 'obj/project.assets.json') -Raw | ConvertFrom-Json
if (@($toolingAssets.libraries.PSObject.Properties | Where-Object { $_.Value.type -ne 'package' }).Count -ne 0) {
    throw 'Tooling consumer must have package dependencies only.'
}
if (-not $toolingAssets.libraries."Microsoft.EntityFrameworkCore.Design/$EfCoreVersion") { throw 'Wrong EF Design patch.' }
if (-not $toolingAssets.libraries."OceanBase.EntityFrameworkCore/$ProviderVersion" -or
    -not $toolingAssets.libraries."Microsoft.EntityFrameworkCore/$EfCoreVersion" -or
    -not $toolingAssets.libraries."Microsoft.EntityFrameworkCore.Relational/$EfCoreVersion") {
    throw 'Wrong provider or EF runtime package version.'
}
$toolingCachedDll = Join-Path $toolingRoot "artifacts/tooling-packages/oceanbase.entityframeworkcore/$ProviderVersion/lib/net10.0/OceanBase.EntityFrameworkCore.dll"
$toolingConsumerDll = Join-Path $toolingWorkspace 'bin/Debug/net10.0/OceanBase.EntityFrameworkCore.dll'
if ((Get-FileHash $toolingCachedDll).Hash -ne (Get-FileHash $toolingConsumerDll).Hash) {
    throw 'Tooling output does not contain the selected packaged provider assembly.'
}

foreach ($toolingMode in @('MySql', 'Oracle')) {
    $toolingContext = "${toolingMode}Context"
    $toolingCommon = @('--project', $toolingProject, '--context', $toolingContext, '--no-build', '--no-color')
    $toolingMigrations = Join-Path $toolingWorkspace "Migrations/$toolingMode"
    $null = Invoke-ToolingDotNet "$toolingMode-add-initial" (@('ef', 'migrations', 'add', 'Initial', '--output-dir', "Migrations/$toolingMode") + $toolingCommon)
    $toolingInitial = Get-ChildItem $toolingMigrations -Filter '*_Initial.cs' | Select-Object -ExpandProperty FullName
    if ((Get-Content $toolingInitial -Raw) -notmatch 'OceanBase:Identity') { throw 'Scaffold lost identity annotations.' }
    Build-ToolingProject
    $null = Invoke-ToolingDotNet "$toolingMode-initial-unchanged" (@('ef', 'migrations', 'has-pending-model-changes') + $toolingCommon)
    $null = Invoke-ToolingDotNet "$toolingMode-expanded-pending" (@('ef', 'migrations', 'has-pending-model-changes') + $toolingCommon + @('--', '--expanded')) 1
    $null = Invoke-ToolingDotNet "$toolingMode-add-expand" (@('ef', 'migrations', 'add', 'Expand', '--output-dir', "Migrations/$toolingMode") + $toolingCommon + @('--', '--expanded'))
    Build-ToolingProject
    $null = Invoke-ToolingDotNet "$toolingMode-expanded-unchanged" (@('ef', 'migrations', 'has-pending-model-changes') + $toolingCommon + @('--', '--expanded'))
    $toolingList = Invoke-ToolingDotNet "$toolingMode-list" (@('ef', 'migrations', 'list', '--no-connect', '--json') + $toolingCommon + @('--', '--expanded'))
    if ($toolingList -notmatch '_Initial' -or $toolingList -notmatch '_Expand') { throw 'Migration discovery lost the generated migrations.' }
    foreach ($toolingScriptKind in @('forward', 'reverse', 'idempotent')) {
        $toolingSqlPath = Join-Path $toolingWorkspace "$toolingMode-$toolingScriptKind.sql"
        $toolingScriptArgs = @('ef', 'migrations', 'script')
        if ($toolingScriptKind -eq 'reverse') { $toolingScriptArgs += @('Expand', '0') }
        if ($toolingScriptKind -eq 'idempotent') { $toolingScriptArgs += '--idempotent' }
        $null = Invoke-ToolingDotNet "$toolingMode-$toolingScriptKind" ($toolingScriptArgs + @('--output', $toolingSqlPath) + $toolingCommon + @('--', '--expanded'))
        $toolingSql = Get-Content $toolingSqlPath -Raw
        if ($toolingSql -notmatch '__ToolingHistory' -or $toolingSql -notmatch 'ToolingRows') { throw 'Wrong migration/history script target.' }
        if ($toolingScriptKind -eq 'reverse' -and $toolingSql -notmatch 'DROP TABLE') { throw 'Missing downgrade.' }
        if ($toolingScriptKind -eq 'idempotent' -and $toolingSql -notmatch 'PREPARE|IF ob_count') { throw 'Missing idempotent guard.' }
        if ($toolingMode -eq 'Oracle' -and $toolingSql -notmatch '^SET DEFINE OFF') { throw 'Missing Oracle script client policy.' }
    }
    if (-not $SkipBundle) {
        $toolingBundle = Join-Path $toolingWorkspace "$toolingMode-efbundle.exe"
        $null = Invoke-ToolingDotNet "$toolingMode-bundle" (@('ef', 'migrations', 'bundle', '--output', $toolingBundle, '--target-runtime', 'win-x64', '--verbose') + $toolingCommon + @('--', '--expanded'))
        $toolingBundleHelp = & $toolingBundle --help 2>&1
        if ($LASTEXITCODE -ne 0 -or ($toolingBundleHelp -join "`n") -notmatch 'connection') { throw 'Generated bundle did not start.' }
        $toolingBundleHelp | Set-Content (Join-Path $toolingWorkspace "$toolingMode-bundle-help.log")
    }
    $toolingRemove = Invoke-ToolingDotNet "$toolingMode-remove" (@('ef', 'migrations', 'remove', '--force') + $toolingCommon + @('--', '--expanded'))
    if ($toolingRemove -notmatch 'Offline tooling cannot open a database') { throw 'Expected EF force-remove to hit the offline guard.' }
    if (Get-ChildItem $toolingMigrations -Filter '*_Expand.cs') { throw 'Latest migration was not removed.' }
    Build-ToolingProject
    $null = Invoke-ToolingDotNet "$toolingMode-removed-unchanged" (@('ef', 'migrations', 'has-pending-model-changes') + $toolingCommon)
}
Write-Output "Packed tooling $ProviderVersion / EF $EfCoreVersion passed; artifacts: $toolingWorkspace"
