@echo off
setlocal EnableExtensions EnableDelayedExpansion
if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if /I "%~1"=="--list" (
 powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-runner.ps1" -Mode Publish -Root "%~dp0." -List
 exit /b !ERRORLEVEL!
)
if "%~1"=="" goto :missing
set "CONFIGURATION=%~2"
set "PROFILE=%~3"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
if "%PROFILE%"=="" set "PROFILE=default"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-runner.ps1" -Mode Publish -Root "%~dp0." -ReleaseId "%~1" -Configuration "%CONFIGURATION%" -AwsProfile "%PROFILE%"
exit /b %ERRORLEVEL%
:missing
echo ERROR: Release ID required.
call :help
exit /b 1
:help
echo Usage: publish-all-lambdas.bat release-id [configuration] [aws-profile]
echo Example: publish-all-lambdas.bat 20260911-001 Release default
echo Builds/packages, uploads code, publishes versions, and stores an S3 release manifest.
echo Does not move aliases. Configure release-settings.json or GANY_RELEASE_* variables.
echo Use --list to preview configured platforms without contacting AWS.
exit /b 0
