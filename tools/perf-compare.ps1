<#
.SYNOPSIS
  Compares load/search performance of the current checkout against another git
  revision (master by default), in all parse/index modes.
.EXAMPLE
  pwsh tools/perf-compare.ps1
  pwsh tools/perf-compare.ps1 -Baseline origin/master -Lines 4000000
#>
param(
    [string]$Baseline = 'master',
    [int]$Lines = 2000000,
    [int]$Runs = 4,
    [string]$LogFile = (Join-Path ([IO.Path]::GetTempPath()) "loggrokx-perf-$Lines.log")
)

$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel)
$harness = Join-Path $repo 'tools/PerfHarness/PerfHarness.csproj'
$baseDir = Join-Path ([IO.Path]::GetTempPath()) "loggrokx-baseline"

if (-not (Test-Path $baseDir)) {
    Write-Host "== preparing baseline worktree ($Baseline) -> $baseDir"
    git -C $repo worktree add $baseDir $Baseline | Out-Host
}

function Invoke-Harness([string]$Title, [string]$DataProj, [string]$Parse, [string]$Index) {
    Write-Host ""
    Write-Host "== $Title" -ForegroundColor Cyan
    $env:LOGGROKX_PARALLEL_PARSING = $Parse
    $env:LOGGROKX_PARALLEL_INDEXING = $Index
    Remove-Item -Recurse -Force (Join-Path $repo 'tools/PerfHarness/obj'), (Join-Path $repo 'tools/PerfHarness/bin') -ErrorAction SilentlyContinue
    dotnet run --project $harness -c Release "-p:DataProj=$DataProj" -- $LogFile $Lines $Runs | Out-Host
    Remove-Item Env:LOGGROKX_PARALLEL_PARSING, Env:LOGGROKX_PARALLEL_INDEXING -ErrorAction SilentlyContinue
}

$baseData = Join-Path $baseDir 'LogGrokX.Data/LogGrokX.Data.csproj'
$headData = Join-Path $repo 'LogGrokX.Data/LogGrokX.Data.csproj'

Invoke-Harness "A. baseline ($Baseline)"                 $baseData '' ''
Invoke-Harness 'B. branch: sequential parse + sequential index' $headData '0' '0'
Invoke-Harness 'C. branch: sequential parse + parallel index'   $headData '0' '1'
Invoke-Harness 'D. branch: parallel parse + parallel index'     $headData '1' '1'
Invoke-Harness 'E. branch: parallel parse + sequential index'   $headData '1' '0'
Invoke-Harness 'F. branch: defaults (auto)'                     $headData '' ''

Write-Host ""
Write-Host "done. Remove the baseline worktree with: git worktree remove $baseDir" -ForegroundColor DarkGray
