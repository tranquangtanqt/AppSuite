<#
.SYNOPSIS
    Tách 1 module ra một thư mục riêng (repo mới / máy khác) mà vẫn build + chạy được, không cần phần còn lại
    của AppSuite.

        <Dest>\
        |-- Directory.Build.props        (thuộc tính chung)
        |-- Directory.Packages.props     (version package - Central Package Management)
        |-- Common\                      (+ mọi project khác mà module ProjectReference tới, đệ quy)
        |-- SharedUI\
        |-- Modules\Directory.Build.props   (khuôn chung của module WinUI + Windows App SDK)
        |-- Modules\<Name>\
        `-- <Name>.sln

.DESCRIPTION
    Module chỉ ProjectReference tới Common / SharedUI (quy tắc kiến trúc), nhưng script vẫn đọc ProjectReference
    thật (đệ quy) để chép đủ, thay vì giả định. Không chép bin\ obj\ .vs\ *.user.

    -Inline: ghi version thẳng vào từng csproj / Modules\Directory.Build.props rồi bỏ Directory.Packages.props
    - dùng khi muốn bản tách hoàn toàn tự đứng, không theo Central Package Management nữa.

    Mặc định build thử bản tách (x64 Debug) để chứng minh nó tự đủ; -NoBuild để bỏ qua.

.EXAMPLE
    .\build\Export-Module.ps1 -Name ImageCompare -Dest D:\work\ImageCompare
    .\build\Export-Module.ps1 -Name CsvEditor -Dest D:\work\CsvEditor -Inline
#>
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Dest,
    [switch]$Inline,
    [switch]$NoBuild,
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$moduleProj = Join-Path $root "Modules\$Name\$Name.csproj"
if (-not (Test-Path $moduleProj)) {
    $valid = (Get-ChildItem (Join-Path $root "Modules") -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }).Name
    throw "Không thấy module '$Name'. Có: $($valid -join ', ')"
}
$Dest = [IO.Path]::GetFullPath($Dest)
if ((Test-Path $Dest) -and (Get-ChildItem $Dest -Force | Select-Object -First 1)) { throw "Thư mục đích '$Dest' đã có nội dung - chọn thư mục trống." }
if ($Dest.StartsWith($root + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "Thư mục đích phải nằm ngoài repo (MSBuild sẽ lẫn với Directory.*.props của repo)." }
# bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Microsoft.WindowsAppRuntime.Bootstrap.dll đã ~130 ký tự: đích dài quá
# thì vượt MAX_PATH 260 và app crash lúc khởi động (DllNotFoundException 0x800700CE "filename too long") - đã gặp.
$probe = Join-Path $Dest "Modules\$Name\bin\$Platform\Debug\net8.0-windows10.0.19041.0\win-$($Platform.ToLowerInvariant())\Microsoft.WindowsAppRuntime.Bootstrap.dll"
if ($probe.Length -gt 240) { Write-Warning "Đường dẫn đích dài ($($probe.Length) ký tự tới DLL output) - app có thể không khởi động được (MAX_PATH 260). Nên chọn thư mục ngắn, vd D:\work\$Name." }

# Thu thập project cần chép: module + ProjectReference đệ quy.
$projects = [ordered]@{}
$queue = New-Object System.Collections.Generic.Queue[string]
$queue.Enqueue((Resolve-Path $moduleProj).Path)
while ($queue.Count) {
    $p = $queue.Dequeue()
    if ($projects.Contains($p)) { continue }
    if (-not $p.StartsWith($root + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "ProjectReference ra ngoài repo: $p" }
    $projects[$p] = $true
    [xml]$x = Get-Content $p -Raw
    foreach ($r in $x.SelectNodes('//ProjectReference')) {
        $queue.Enqueue([IO.Path]::GetFullPath((Join-Path (Split-Path $p) $r.Include)))
    }
}
if ($projects.Keys | Where-Object { $_ -match '\\MainLauncher\\' }) { throw "Module reference MainLauncher - vi phạm quy tắc kiến trúc, không export." }

function Copy-Dir([string]$src, [string]$dst) {
    robocopy $src $dst /E /XD bin obj .vs /XF *.user /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy lỗi ($LASTEXITCODE): $src" }
}

New-Item -ItemType Directory -Force $Dest | Out-Null
foreach ($p in $projects.Keys) {
    $rel = (Split-Path $p).Substring($root.Length + 1)
    Write-Host "Chép $rel" -ForegroundColor Cyan
    Copy-Dir (Split-Path $p) (Join-Path $Dest $rel)
}
# Props mà MSBuild tự import theo thư mục cha: chép mọi file Directory.*.props nằm trên đường từ project lên gốc repo.
$propsFiles = foreach ($p in $projects.Keys) {
    $d = Split-Path $p
    while ($d.Length -ge $root.Length) {
        Get-ChildItem $d -File -Filter 'Directory.*.props' -ErrorAction SilentlyContinue
        Get-ChildItem $d -File -Filter 'Directory.*.targets' -ErrorAction SilentlyContinue
        $d = Split-Path $d
    }
}
foreach ($f in ($propsFiles | Sort-Object FullName -Unique)) {
    $rel = $f.FullName.Substring($root.Length + 1)
    Write-Host "Chép $rel" -ForegroundColor Cyan
    Copy-Item $f.FullName (Join-Path $Dest $rel) -Force
}
foreach ($f in '.gitignore', 'global.json', 'nuget.config', 'NuGet.Config') {
    if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) (Join-Path $Dest $f) }
}

if ($Inline) {
    $cpm = Join-Path $Dest 'Directory.Packages.props'
    [xml]$px = Get-Content $cpm -Raw
    $versions = @{}
    foreach ($v in $px.SelectNodes('//PackageVersion')) { $versions[$v.Include] = $v.Version }
    $targets = @(Get-ChildItem $Dest -Recurse -File -Include *.csproj, Directory.Build.props | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
    foreach ($t in $targets) {
        $text = [IO.File]::ReadAllText($t.FullName)
        $new = [regex]::Replace($text, '<PackageReference Include="([^"]+)"(?![^>]*\bVersion=)', {
            param($m)
            $id = $m.Groups[1].Value
            if (-not $versions.ContainsKey($id)) { throw "Không có version cho '$id' trong Directory.Packages.props" }
            "<PackageReference Include=`"$id`" Version=`"$($versions[$id])`""
        })
        if ($new -ne $text) { [IO.File]::WriteAllText($t.FullName, $new, (New-Object System.Text.UTF8Encoding $false)) }
    }
    Remove-Item $cpm
    Write-Host "Inline: đã ghi version vào csproj, bỏ Directory.Packages.props" -ForegroundColor Cyan
}

Push-Location $Dest
try {
    dotnet new sln -n $Name --force | Out-Null
    foreach ($p in $projects.Keys) { dotnet sln "$Name.sln" add $p.Substring($root.Length + 1) | Out-Null }
} finally { Pop-Location }

if (-not $NoBuild) {
    Write-Host "Build thử bản tách ($Platform Debug)..." -ForegroundColor Cyan
    dotnet build (Join-Path $Dest "Modules\$Name\$Name.csproj") -c Debug -p:Platform=$Platform | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Build bản tách thất bại - xem log ở trên." }
}

Write-Host "Xong: $Dest (mở $Name.sln, đặt $Name làm Startup Project, F5)" -ForegroundColor Green
