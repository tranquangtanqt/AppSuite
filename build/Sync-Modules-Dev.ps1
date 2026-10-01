<#
.SYNOPSIS
    Dev convenience script: builds MainLauncher + every Module and copies each module's output
    next to MainLauncher's own build output, so modules.json's relative paths
    (".\Modules\ModuleA\ModuleA.exe") resolve when you press F5 on MainLauncher.

.DESCRIPTION
    This script is NOT required to build or run any project - every module still builds and runs
    completely on its own (open Modules\ModuleA\ModuleA.csproj directly, or `dotnet run` from that
    folder). It only saves a manual copy step while developing MainLauncher locally, since there is
    no ProjectReference from MainLauncher to any module.

    Every project is built with an explicit RuntimeIdentifier (win-<platform>, same as VS F5), and
    the output folder is asked from MSBuild (TargetDir) instead of guessed. Without an explicit RID
    `dotnet build` writes to bin\<Platform>\<Config>\<TFM>\ (with runtimes\ for every arch), and that
    folder also contains VS's own <RID>\ subfolder - copying it duplicated each module several times.

    Each module's destination folder is wiped before copying, and folders under Modules\ that are no
    longer in the module list (e.g. old names ModuleD..ModuleH) are removed, so the launcher output
    never accumulates stale copies.

.EXAMPLE
    .\build\Sync-Modules-Dev.ps1
    .\build\Sync-Modules-Dev.ps1 -Configuration Release -Platform x64
#>
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$rid = "win-$($Platform.ToLowerInvariant())"
$buildProps = @("-p:Configuration=$Configuration", "-p:Platform=$Platform", "-p:RuntimeIdentifier=$rid")

$modules = @("ModuleA", "ModuleB", "ModuleC", "Mcf.DbDef.HtmlGenerator", "Rdbms.HtmlGenerator", "CsvEditor", "Mcf.Screen.HtmlGenerator", "Mcf.CrudDiagram.HtmlGenerator", "ScreenCapture", "ImageCompare", "FileTools")

# Modules: force framework-dependent. Some projects (ModuleA/B, ImageCompare) have template
# Properties\PublishProfiles\win-*.pubxml with SelfContained=true, and the SDK imports that profile on
# plain builds too - each copy would otherwise carry its own ~60 MB .NET runtime. MainLauncher is left
# as-is so this output matches what VS F5 builds into the same folder.
$moduleProps = @("-p:SelfContained=false")

function Build-AndLocate {
    param([string]$ProjectPath, [string]$ExeName, [string[]]$ExtraProps = @(), [switch]$CleanOutput)

    $props = $buildProps + $ExtraProps
    $targetDir = (dotnet msbuild $ProjectPath -getProperty:TargetDir @props | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $targetDir) { throw "Could not evaluate TargetDir of $ProjectPath" }

    # Drop leftovers of an earlier self-contained build (e.g. VS F5 on the module) so they are not
    # copied along; obj\ is kept, so this only re-copies outputs - no recompile.
    if ($CleanOutput -and (Test-Path $targetDir)) { Remove-Item $targetDir -Recurse -Force }

    dotnet build $ProjectPath @props | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $ProjectPath" }

    if (-not (Test-Path (Join-Path $targetDir $ExeName))) {
        throw "Could not find $ExeName in '$targetDir' after building $ProjectPath"
    }
    return $targetDir.TrimEnd('\')
}

Write-Host "Building MainLauncher..." -ForegroundColor Cyan
$launcherOutDir = Build-AndLocate -ProjectPath (Join-Path $root "MainLauncher\MainLauncher.csproj") -ExeName "MainLauncher.exe"
$modulesRoot = Join-Path $launcherOutDir "Modules"

# Remove module folders that are no longer in the list (renamed/deleted modules).
if (Test-Path $modulesRoot) {
    Get-ChildItem $modulesRoot -Directory | Where-Object { $modules -notcontains $_.Name } | ForEach-Object {
        Write-Host "Removing stale module folder $($_.FullName)" -ForegroundColor DarkYellow
        Remove-Item $_.FullName -Recurse -Force
    }
}

foreach ($module in $modules) {
    Write-Host "Building $module..." -ForegroundColor Cyan
    $moduleOutDir = Build-AndLocate -ProjectPath (Join-Path $root "Modules\$module\$module.csproj") -ExeName "$module.exe" -ExtraProps $moduleProps -CleanOutput

    $dest = Join-Path $modulesRoot $module
    if (Test-Path $dest) {
        try { Remove-Item $dest -Recurse -Force }
        catch { throw "Cannot clean $dest - is $module.exe still running? ($($_.Exception.Message))" }
    }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item "$moduleOutDir\*" $dest -Recurse -Force
    Write-Host "  -> copied to $dest" -ForegroundColor DarkGray
}

Write-Host "Done. Run MainLauncher.exe from $launcherOutDir" -ForegroundColor Green
