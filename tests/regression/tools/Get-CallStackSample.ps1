<#
.SYNOPSIS
  Генерирует лог с реальными кадрами стека для системных DLL текущей машины.
.DESCRIPTION
  Читает PE-заголовки DLL (по умолчанию ntdll.dll и kernel32.dll из System32): из
  debug-каталога берётся CodeView-запись RSDS (GUID и age PDB), из таблицы экспорта —
  RVA экспортируемой функции (не форвардной). Создаётся лог формата Classic с записью,
  содержащей три формы кадров:
     N# module!GUID,age!RVA     (поиск PDB на symbol server по GUID/age)
     N# module!RVA              (без GUID — PDB ищется только в обычной папке)
     N# module+0xRVA
  К RVA добавляется смещение 0x4, поэтому ожидаемый результат — `Function+0x4`.
  Рядом создаётся <Path>.expected.json с ожидаемыми именами функций.
.EXAMPLE
  ./Get-CallStackSample.ps1 -Path $env:TEMP\lgx-callstack.log
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [string[]] $Modules = @("$env:SystemRoot\System32\ntdll.dll", "$env:SystemRoot\System32\kernel32.dll"),
    [string[]] $Candidates = @('RtlGetVersion', 'RtlInitUnicodeString', 'GetTickCount', 'GetCurrentProcessId',
        'GetLastError', 'CloseHandle', 'Sleep', 'LoadLibraryW')
)
$ErrorActionPreference = 'Stop'

function Read-PeInfo([string] $file, [string[]] $names) {
    $b = [System.IO.File]::ReadAllBytes($file)
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    if ([BitConverter]::ToUInt32($b, $pe) -ne 0x00004550) { throw "$file is not a PE file" }
    $sections = [BitConverter]::ToUInt16($b, $pe + 6)
    $optSize = [BitConverter]::ToUInt16($b, $pe + 20)
    $opt = $pe + 24
    $magic = [BitConverter]::ToUInt16($b, $opt)
    $dd = if ($magic -eq 0x20B) { $opt + 112 } else { $opt + 96 }
    $secTable = $opt + $optSize
    $toOffset = {
        param([uint32] $rva)
        for ($i = 0; $i -lt $sections; $i++) {
            $s = $secTable + 40 * $i
            $va = [BitConverter]::ToUInt32($b, $s + 12)
            $size = [Math]::Max([BitConverter]::ToUInt32($b, $s + 8), [BitConverter]::ToUInt32($b, $s + 16))
            if ($rva -ge $va -and $rva -lt $va + $size) { return [int]($rva - $va + [BitConverter]::ToUInt32($b, $s + 20)) }
        }
        throw "RVA 0x{0:X} not mapped" -f $rva
    }
    $readZ = { param([int] $o) $e = $o; while ($b[$e] -ne 0) { $e++ }; [System.Text.Encoding]::ASCII.GetString($b, $o, $e - $o) }

    # CodeView RSDS
    $guid = $null; $age = 0; $pdb = $null
    $dbgRva = [BitConverter]::ToUInt32($b, $dd + 6 * 8); $dbgSize = [BitConverter]::ToUInt32($b, $dd + 6 * 8 + 4)
    if ($dbgRva) {
        $d = & $toOffset $dbgRva
        for ($k = 0; $k -lt $dbgSize / 28; $k++) {
            $e = $d + 28 * $k
            if ([BitConverter]::ToUInt32($b, $e + 12) -eq 2) {
                $raw = [int][BitConverter]::ToUInt32($b, $e + 24)
                if ([System.Text.Encoding]::ASCII.GetString($b, $raw, 4) -eq 'RSDS') {
                    $guid = [Guid]::new([byte[]]$b[($raw + 4)..($raw + 19)])
                    $age = [BitConverter]::ToUInt32($b, $raw + 20)
                    $pdb = & $readZ ($raw + 24)
                }
            }
        }
    }

    # Exports
    $expRva = [BitConverter]::ToUInt32($b, $dd); $expSize = [BitConverter]::ToUInt32($b, $dd + 4)
    $x = & $toOffset $expRva
    $count = [BitConverter]::ToUInt32($b, $x + 24)
    $funcs = & $toOffset ([BitConverter]::ToUInt32($b, $x + 28))
    $namesOff = & $toOffset ([BitConverter]::ToUInt32($b, $x + 32))
    $ords = & $toOffset ([BitConverter]::ToUInt32($b, $x + 36))
    $map = @{}
    for ($i = 0; $i -lt $count; $i++) {
        $n = & $readZ (& $toOffset ([BitConverter]::ToUInt32($b, $namesOff + 4 * $i)))
        if ($names -contains $n) {
            $ord = [BitConverter]::ToUInt16($b, $ords + 2 * $i)
            $rva = [BitConverter]::ToUInt32($b, $funcs + 4 * $ord)
            if ($rva -lt $expRva -or $rva -ge $expRva + $expSize) { $map[$n] = $rva } # не форвард
        }
    }
    $fn = $names | Where-Object { $map.ContainsKey($_) } | Select-Object -First 1
    if (-not $fn) { throw "No non-forwarded candidate export in $file" }
    [pscustomobject]@{ Module = [IO.Path]::GetFileName($file).ToLowerInvariant(); Guid = $guid; Age = $age; Pdb = $pdb
        Function = $fn; Rva = $map[$fn] }
}

$infos = foreach ($m in $Modules) { Read-PeInfo $m $Candidates }
$frames = [System.Collections.Generic.List[string]]::new()
$expected = [System.Collections.Generic.List[object]]::new()
$n = 0
foreach ($i in $infos) {
    $off = '{0:x}' -f ($i.Rva + 4)
    if ($i.Guid) {
        $frames.Add((' {0}# {1}!{2},{3:x}!{4}' -f $n, $i.Module, $i.Guid.ToString().ToUpperInvariant(), $i.Age, $off))
        $expected.Add([pscustomobject]@{ Frame = $n; Form = 'module!GUID,age!RVA'; Expected = "$($i.Module -replace '\.dll$')!$($i.Function)+0x4" }); $n++
    }
    $frames.Add((' {0}# {1}!{2}' -f $n, $i.Module, $off))
    $expected.Add([pscustomobject]@{ Frame = $n; Form = 'module!RVA'; Expected = 'resolved only if the PDB lies in a plain folder of the symbol path' }); $n++
    $frames.Add((' {0}# {1}+0x{2}' -f $n, $i.Module, $off))
    $expected.Add([pscustomobject]@{ Frame = $n; Form = 'module+0xRVA'; Expected = 'resolved only if the PDB lies in a plain folder of the symbol path' }); $n++
}
$lines = @(
    '2026-05-14 10:00:00.000 INF Thread-01 Core [S00] before the call stack',
    '2026-05-14 10:00:01.000 ERR Thread-01 Core [S01] unhandled exception, Call stack:'
) + $frames + @('2026-05-14 10:00:02.000 INF Thread-01 Core [S02] after the call stack')
[System.IO.File]::WriteAllText($Path, ($lines -join "`r`n") + "`r`n", [System.Text.UTF8Encoding]::new($false))
$expected | ConvertTo-Json | Set-Content -Path "$Path.expected.json" -Encoding utf8
$infos | Format-Table Module, Function, @{ n = 'Rva'; e = { '0x{0:x}' -f $_.Rva } }, Guid, Age, Pdb -AutoSize | Out-String | Write-Host
Get-Content $Path
