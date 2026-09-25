@echo off
rem Compares load/search performance of the current checkout against master,
rem in all parse/index modes. Usage: tools\perf-compare.cmd [lines] [runs] [logfile]
setlocal enabledelayedexpansion

set LINES=%1
if "%LINES%"=="" set LINES=2000000
set RUNS=%2
if "%RUNS%"=="" set RUNS=4
set LOGFILE=%3
if "%LOGFILE%"=="" set LOGFILE=%TEMP%\loggrokx-perf-%LINES%.log

for /f "delims=" %%i in ('git rev-parse --show-toplevel') do set REPO=%%i
set HARNESS=%REPO%/tools/PerfHarness/PerfHarness.csproj
set BASEDIR=%TEMP%\loggrokx-baseline

if not exist "%BASEDIR%" (
  echo == preparing baseline worktree ^(master^) -^> %BASEDIR%
  git -C "%REPO%" worktree add "%BASEDIR%" master || exit /b 1
)

call :run "A. baseline (master)"                          "%BASEDIR%/LogGrokX.Data/LogGrokX.Data.csproj" ""  ""
call :run "B. branch: sequential parse + sequential index" "%REPO%/LogGrokX.Data/LogGrokX.Data.csproj"    "0" "0"
call :run "C. branch: sequential parse + parallel index"   "%REPO%/LogGrokX.Data/LogGrokX.Data.csproj"    "0" "1"
call :run "D. branch: parallel parse + parallel index"     "%REPO%/LogGrokX.Data/LogGrokX.Data.csproj"    "1" "1"
call :run "E. branch: parallel parse + sequential index"   "%REPO%/LogGrokX.Data/LogGrokX.Data.csproj"    "1" "0"
call :run "F. branch: defaults (auto)"                     "%REPO%/LogGrokX.Data/LogGrokX.Data.csproj"    ""  ""

echo.
echo done. Remove the baseline worktree with: git worktree remove "%BASEDIR%"
exit /b 0

:run
echo.
echo == %~1
set "LOGGROKX_PARALLEL_PARSING=%~3"
set "LOGGROKX_PARALLEL_INDEXING=%~4"
rmdir /s /q "%REPO%\tools\PerfHarness\obj" 2>nul
rmdir /s /q "%REPO%\tools\PerfHarness\bin" 2>nul
dotnet run --project "%HARNESS%" -c Release "-p:DataProj=%~2" -- "%LOGFILE%" %LINES% %RUNS%
set "LOGGROKX_PARALLEL_PARSING="
set "LOGGROKX_PARALLEL_INDEXING="
exit /b 0
