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
        |-- Modules\ModuleF\ModuleF.exe
        |-- Modules\ModuleG\ModuleG.exe
        |-- Modules\ModuleH\ModuleH.exe
        `-- Config\modules.json

.DESCRIPTION
    Each project is published independently with `dotnet publish` (framework-dependent, single
    RID). MainLauncher is never given a ProjectReference to any module, so this script - not
    MSBuild - is what ties the three executables into one deployable folder.

    Use -Targets to publish only a subset (faster than rebuilding everything while iterating on
    one module). Names are matched case-insensitively; "MainLauncher" and "ModuleA".."ModuleH" are
    valid. Omit -Targets to publish everything, same as before.

.EXAMPLE
    .\build\Publish-AppSuite.ps1
    .\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64 -OutputDir .\Application
    .\build\Publish-AppSuite.ps1 -Targets ModuleD
    .\build\Publish-AppSuite.ps1 -Targets ModuleD,ModuleE,MainLauncher

.NOTES
    Publishing all 9 projects takes a while - pass -Targets to publish only the project(s) you're
    iterating on (e.g. -Targets ModuleD) instead of waiting on a full run every time.

    If PowerShell refuses to run this script with an error like "cannot be loaded because
    running scripts is disabled on this system", either:
    - Run it once with a bypassed policy for just this invocation:
        powershell -ExecutionPolicy Bypass -File .\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64
    - Or allow locally-authored scripts for your user going forward:
        Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\Application"),
    [string[]]$Targets
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

# Ordered so MainLauncher publishes first when no -Targets filter is given (cosmetic only - each
# entry is independent, so filtering/reordering here never breaks anything).
$allProjects = [ordered]@{
    "MainLauncher" = @{ ProjectPath = "MainLauncher\MainLauncher.csproj"; DestSubfolder = "." }
    "ModuleA"      = @{ ProjectPath = "Modules\ModuleA\ModuleA.csproj"; DestSubfolder = "Modules\ModuleA" }
    "ModuleB"      = @{ ProjectPath = "Modules\ModuleB\ModuleB.csproj"; DestSubfolder = "Modules\ModuleB" }
    "ModuleC"      = @{ ProjectPath = "Modules\ModuleC\ModuleC.csproj"; DestSubfolder = "Modules\ModuleC" }
    "ModuleD"      = @{ ProjectPath = "Modules\ModuleD\ModuleD.csproj"; DestSubfolder = "Modules\ModuleD" }
    "ModuleE"      = @{ ProjectPath = "Modules\ModuleE\ModuleE.csproj"; DestSubfolder = "Modules\ModuleE" }
    "ModuleF"      = @{ ProjectPath = "Modules\ModuleF\ModuleF.csproj"; DestSubfolder = "Modules\ModuleF" }
    "ModuleG"      = @{ ProjectPath = "Modules\ModuleG\ModuleG.csproj"; DestSubfolder = "Modules\ModuleG" }
    "ModuleH"      = @{ ProjectPath = "Modules\ModuleH\ModuleH.csproj"; DestSubfolder = "Modules\ModuleH" }
}

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

$selectedNames = $allProjects.Keys
if ($Targets) {
    $selectedNames = foreach ($name in $Targets) {
        $match = $allProjects.Keys | Where-Object { $_ -eq $name }
        if (-not $match) {
            throw "Unknown target '$name'. Valid targets: $($allProjects.Keys -join ', ')"
        }
        $match
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($name in $selectedNames) {
    $project = $allProjects[$name]
    Publish-Project -ProjectPath (Join-Path $root $project.ProjectPath) -DestSubfolder $project.DestSubfolder
}

Write-Host "Done. Deployment output at $OutputDir" -ForegroundColor Green
