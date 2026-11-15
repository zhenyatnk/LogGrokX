<#
.SYNOPSIS
  Удерживает файл открытым с заданным режимом совместного доступа, пока не нажата Enter
  (или не истёк -Seconds).
.EXAMPLE
  ./Lock-LogFile.ps1 -Path .\testdata\tab-basic.log -Share Read     # другой процесс пишет, чтение разрешено
  ./Lock-LogFile.ps1 -Path .\testdata\tab-basic.log -Share None     # эксклюзивная блокировка
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [ValidateSet('None', 'Read', 'ReadWrite')] [string] $Share = 'None',
    [int] $Seconds = 0
)
$ErrorActionPreference = 'Stop'
$fs = [System.IO.File]::Open((Resolve-Path $Path).Path, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::$Share)
try {
    Write-Host "LOCKED ($Share): $Path"
    if ($Seconds -gt 0) { Start-Sleep -Seconds $Seconds } else { Read-Host 'Press Enter to release' | Out-Null }
}
finally { $fs.Dispose(); Write-Host 'RELEASED' }
