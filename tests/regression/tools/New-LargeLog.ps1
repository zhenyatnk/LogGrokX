<#
.SYNOPSIS
  Создаёт большой лог формата Classic (дата + уровень + поток + компонент + сообщение).
.EXAMPLE
  ./New-LargeLog.ps1 -Path $env:TEMP\lgx-large.log -SizeMB 1024
.NOTES
  Первая строка содержит маркер [L-FIRST], строка в середине файла — [L-MID],
  последняя строка — [L-LAST]. Каждая 1000-я строка — уровень ERR с маркером [L-ERR].
  Скрипт выводит итоговое число строк — используйте его как ожидаемое значение.
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [ValidateRange(1, 65536)] [int] $SizeMB = 512
)
$ErrorActionPreference = 'Stop'
$target = [int64]$SizeMB * 1MB
$levels = 'INF', 'DBG', 'WRN', 'INF', 'INF'
$threads = 'Thread-01', 'Thread-02', 'Thread-03', 'Thread-04'
$components = 'Net', 'Db', 'Ui', 'Core'
$start = [datetime]'2026-05-14T00:00:00'
$writer = [System.IO.StreamWriter]::new($Path, $false, [System.Text.UTF8Encoding]::new($false), 1MB)
try {
    $i = 0L
    $midWritten = $false
    $writer.Write("$($start.ToString('yyyy-MM-dd HH:mm:ss.fff')) INF Thread-01 Core [L-FIRST] large log start`r`n")
    $i++
    while ($writer.BaseStream.Length -lt $target - 200) {
        $t = $start.AddMilliseconds($i * 3).ToString('yyyy-MM-dd HH:mm:ss.fff')
        if (-not $midWritten -and $writer.BaseStream.Length -ge $target / 2) {
            $writer.Write("$t INF Thread-01 Core [L-MID] middle of the file line=$i`r`n")
            $midWritten = $true
        }
        elseif ($i % 1000 -eq 0) {
            $writer.Write("$t ERR $($threads[$i % 4]) $($components[$i % 4]) [L-ERR] periodic error line=$i`r`n")
        }
        else {
            $writer.Write("$t $($levels[$i % 5]) $($threads[$i % 4]) $($components[($i -shr 2) % 4]) payload line=$i value=$($i * 7919 % 100000)`r`n")
        }
        $i++
        if ($i % 100000 -eq 0) { $writer.Flush() }
    }
    $t = $start.AddMilliseconds($i * 3).ToString('yyyy-MM-dd HH:mm:ss.fff')
    $writer.Write("$t INF Thread-01 Core [L-LAST] large log end line=$i`r`n")
    $i++
}
finally { $writer.Dispose() }
[pscustomobject]@{ Path = (Resolve-Path $Path).Path; Lines = $i; Bytes = (Get-Item $Path).Length }
