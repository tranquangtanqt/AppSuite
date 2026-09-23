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

    Output folders for WinUI/WindowsAppSDK projects include a runtime-identifier segment
    (bin\<Platform>\<Config>\<TFM>\<RID>\) that varies by machine/SDK version, so this script
    discovers the real output folder by locating each built .exe instead of hard-coding the path.

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

function Build-AndLocate {
    param([string]$ProjectPath, [string]$ExeName)

    dotnet build $ProjectPath -c $Configuration -p:Platform=$Platform | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $ProjectPath" }

    $binRoot = Join-Path (Split-Path $ProjectPath) "bin"
    $exe = Get-ChildItem $binRoot -Recurse -Filter $ExeName -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $exe) { throw "Could not find $ExeName under $binRoot after building" }
    return $exe.DirectoryName
}

Write-Host "Building MainLauncher..." -ForegroundColor Cyan
$launcherOutDir = Build-AndLocate -ProjectPath (Join-Path $root "MainLauncher\MainLauncher.csproj") -ExeName "MainLauncher.exe"

foreach ($module in @("ModuleA", "ModuleB", "ModuleC", "Mcf.DbDef.HtmlGenerator", "Rdbms.HtmlGenerator", "CsvEditor", "Mcf.Screen.HtmlGenerator", "Mcf.CrudDiagram.HtmlGenerator", "ScreenCapture")) {
    Write-Host "Building $module..." -ForegroundColor Cyan
    $moduleOutDir = Build-AndLocate -ProjectPath (Join-Path $root "Modules\$module\$module.csproj") -ExeName "$module.exe"

    $dest = Join-Path $launcherOutDir "Modules\$module"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item "$moduleOutDir\*" $dest -Recurse -Force
    Write-Host "  -> copied to $dest" -ForegroundColor DarkGray
}

Write-Host "Done. Run MainLauncher.exe from $launcherOutDir" -ForegroundColor Green
