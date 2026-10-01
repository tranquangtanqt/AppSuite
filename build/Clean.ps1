<#
.SYNOPSIS
    Xoá output build (bin\ obj\) của các project trong repo - an toàn hơn `git clean -xdf`.

.DESCRIPTION
    Chỉ xoá thư mục bin\ và obj\ nằm ngay cạnh một file .csproj (không đụng thư mục tên "bin" ở chỗ khác).
    KHÔNG xoá file chưa track / bị ignore khác như `git clean -xdf` sẽ xoá: Excel trong
    Modules\Mcf.DbDef.HtmlGenerator\Data\Excel\, *.csproj.user, .vs\ ...

    Mọi thứ bị xoá đều sinh lại được ở lần build sau (lần build đầu sau khi clean sẽ lâu hơn vì phải
    restore + biên dịch lại). Visual Studio đang mở vẫn chạy được - nó tự restore lại obj\.

    Nếu đang có exe chạy từ chính các thư mục sắp xoá (vd MainLauncher F5, module mở qua launcher), script
    dừng và liệt kê tiến trình - tắt chúng rồi chạy lại.

.PARAMETER Targets
    Chỉ clean các project có tên này (tên .csproj, không phân biệt hoa/thường), vd ModuleA, ImageCompare.Tests.
    Bỏ trống = mọi project.

.PARAMETER Application
    Xoá luôn thư mục Application\ (output của Publish-AppSuite.ps1). Mặc định giữ lại.

.PARAMETER WhatIf
    Chỉ liệt kê những gì sẽ xoá + dung lượng, không xoá gì.

.EXAMPLE
    .\build\Clean.ps1 -WhatIf
    Xem trước sẽ xoá gì, giải phóng bao nhiêu.

.EXAMPLE
    .\build\Clean.ps1
    Xoá bin\ obj\ của mọi project (giữ Application\).

.EXAMPLE
    .\build\Clean.ps1 -Targets ModuleA,ImageCompare
    Chỉ clean 2 project.

.EXAMPLE
    .\build\Clean.ps1 -Application
    Xoá bin\ obj\ và cả Application\.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string[]]$Targets,
    [switch]$Application
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$projects = Get-ChildItem $root -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|\.vs)\\' }
if ($Targets) {
    $unknown = $Targets | Where-Object { $t = $_; -not ($projects | Where-Object { $_.BaseName -eq $t }) }
    if ($unknown) { throw "Không có project: $($unknown -join ', '). Có: $(($projects.BaseName | Sort-Object) -join ', ')" }
    $projects = $projects | Where-Object { $Targets -contains $_.BaseName }
}

$dirs = @(foreach ($p in $projects) {
    foreach ($n in 'bin', 'obj') {
        $d = Join-Path $p.DirectoryName $n
        if (Test-Path $d) { $d }
    }
})
if ($Application -and (Test-Path (Join-Path $root 'Application'))) { $dirs += (Join-Path $root 'Application') }

if (-not $dirs) { Write-Host "Không có gì để xoá." -ForegroundColor Green; return }

# Tiến trình đang chạy từ các thư mục này sẽ khoá file -> báo trước thay vì xoá dở dang.
$busy = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $path = $_.Path
    $path -and ($dirs | Where-Object { $path.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) })
}
if ($busy) {
    $busy | ForEach-Object { Write-Host ("  {0} (PID {1}) - {2}" -f $_.ProcessName, $_.Id, $_.Path) -ForegroundColor Yellow }
    throw "Có tiến trình đang chạy từ thư mục sắp xoá - tắt chúng rồi chạy lại."
}

function Get-SizeMB([string]$d) {
    [math]::Round(((Get-ChildItem $d -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum) / 1MB, 1)
}

$total = 0
$failed = @()
foreach ($d in $dirs) {
    $mb = Get-SizeMB $d
    $rel = $d.Substring($root.Length + 1)
    if ($PSCmdlet.ShouldProcess("$rel ($mb MB)", "Xoá")) {
        try {
            Remove-Item $d -Recurse -Force
        } catch {
            # Đường dẫn quá dài / file read-only lạ: thử robocopy /MIR từ thư mục rỗng rồi xoá.
            $empty = Join-Path ([IO.Path]::GetTempPath()) ("clean-empty-" + [guid]::NewGuid())
            New-Item -ItemType Directory $empty | Out-Null
            robocopy $empty $d /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
            [IO.Directory]::Delete($empty)
            try { [IO.Directory]::Delete($d, $true) } catch { $failed += "$rel : $($_.Exception.Message)"; continue }
        }
        Write-Host ("  đã xoá {0,8:N1} MB  {1}" -f $mb, $rel)
    }
    $total += $mb
}

if ($WhatIfPreference) {
    Write-Host ("Sẽ giải phóng khoảng {0:N0} MB ({1} thư mục). Chạy lại không có -WhatIf để xoá." -f $total, $dirs.Count) -ForegroundColor Cyan
} elseif ($failed) {
    $failed | ForEach-Object { Write-Host "  LỖI $_" -ForegroundColor Red }
    throw "Một số thư mục không xoá được (xem ở trên)."
} else {
    Write-Host ("Xong: giải phóng {0:N0} MB ({1} thư mục)." -f $total, $dirs.Count) -ForegroundColor Green
}
