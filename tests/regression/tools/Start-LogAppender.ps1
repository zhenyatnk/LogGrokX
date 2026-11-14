<#
.SYNOPSIS
  Дописывает строки в лог с заданным интервалом (растущий файл).
.EXAMPLE
  ./Start-LogAppender.ps1 -Path $env:TEMP\lgx-grow.log -Count 20 -IntervalMs 500
.NOTES
  Файл открывается с FileShare.ReadWrite, поэтому LogGrokX может читать его одновременно.
  Каждая строка помечена [G-NNN]. Флаг -Truncate перед стартом обрезает файл до нуля.
  Флаг -PartialLast оставляет последнюю строку без перевода строки.
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [int] $Count = 20,
    [int] $IntervalMs = 500,
    [switch] $Truncate,
    [switch] $PartialLast
)
$ErrorActionPreference = 'Stop'
$mode = if ($Truncate) { [System.IO.FileMode]::Create } else { [System.IO.FileMode]::Append }
$fs = [System.IO.FileStream]::new($Path, $mode, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
$enc = [System.Text.UTF8Encoding]::new($false)
try {
    for ($n = 1; $n -le $Count; $n++) {
        $t = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
        $eol = if ($PartialLast -and $n -eq $Count) { '' } else { "`r`n" }
        $bytes = $enc.GetBytes(("{0} INF Thread-01 Grow [G-{1:000}] appended line{2}" -f $t, $n, $eol))
        $fs.Write($bytes, 0, $bytes.Length)
        $fs.Flush()
        Write-Host ("appended [G-{0:000}]" -f $n)
        if ($n -lt $Count) { Start-Sleep -Milliseconds $IntervalMs }
    }
}
finally { $fs.Dispose() }
