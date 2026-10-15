@echo off
setlocal EnableExtensions EnableDelayedExpansion
if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if /I "%~1"=="--list" (
 powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0lambda-runner.ps1" -Mode Build -Root "%~dp0."  -List
 exit /b !ERRORLEVEL!
)
set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0lambda-runner.ps1" -Mode Build -Root "%~dp0."  -Configuration "%CONFIGURATION%"
exit /b %ERRORLEVEL%
:help
echo Usage: build-all-lambdas.bat [configuration]
echo Use --list to preview Lambdas. Default configuration: Release.
echo Continues after Lambda failures. Logs: artifacts\lambda-runs.
echo Per run and API folder: success.log, failed.log, results.csv, summary.txt.
echo Exit code 1 reports failures after all eligible Lambdas are processed.
exit /b 0
