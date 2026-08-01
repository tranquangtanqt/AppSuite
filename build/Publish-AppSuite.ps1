<#
.SYNOPSIS
    Publishes MainLauncher and every module into the deployment layout:

        Application\
        |-- MainLauncher.exe
        |-- Modules\ModuleA\ModuleA.exe
        |-- Modules\ModuleB\ModuleB.exe
        |-- Modules\ModuleC\ModuleC.exe
        |-- Modules\ModuleD\ModuleD.exe
        |-- Modules\ModuleE\ModuleE.exe
        `-- Config\modules.json

.DESCRIPTION
    Each project is published independently with `dotnet publish` (framework-dependent, single
    RID). MainLauncher is never given a ProjectReference to any module, so this script - not
    MSBuild - is what ties the three executables into one deployable folder.

.EXAMPLE
    .\build\Publish-AppSuite.ps1
    .\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64 -OutputDir .\Application
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\Application")
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

function Publish-Project {
    param([string]$ProjectPath, [string]$DestSubfolder)

    $dest = Join-Path $OutputDir $DestSubfolder
    Write-Host "Publishing $ProjectPath -> $dest" -ForegroundColor Cyan

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:WindowsPackageType=None `
        -p:PublishSingleFile=false `
        -o $dest
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Publish-Project -ProjectPath (Join-Path $root "MainLauncher\MainLauncher.csproj") -DestSubfolder "."
Publish-Project -ProjectPath (Join-Path $root "Modules\ModuleA\ModuleA.csproj") -DestSubfolder "Modules\ModuleA"
Publish-Project -ProjectPath (Join-Path $root "Modules\ModuleB\ModuleB.csproj") -DestSubfolder "Modules\ModuleB"
Publish-Project -ProjectPath (Join-Path $root "Modules\ModuleC\ModuleC.csproj") -DestSubfolder "Modules\ModuleC"
Publish-Project -ProjectPath (Join-Path $root "Modules\ModuleD\ModuleD.csproj") -DestSubfolder "Modules\ModuleD"
Publish-Project -ProjectPath (Join-Path $root "Modules\ModuleE\ModuleE.csproj") -DestSubfolder "Modules\ModuleE"

Write-Host "Done. Deployment output at $OutputDir" -ForegroundColor Green
