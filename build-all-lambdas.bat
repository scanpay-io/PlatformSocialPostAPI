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
echo Continues after failures. Logs and sorted summaries: artifacts\lambda-runs.
exit /b 0
