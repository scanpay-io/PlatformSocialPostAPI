@echo off
setlocal EnableExtensions EnableDelayedExpansion
if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if "%~1"=="" goto :missing
if "%~2"=="" goto :missing
set "PROFILE=%~3"
if "%PROFILE%"=="" set "PROFILE=default"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-runner.ps1" -Mode Deploy -Root "%~dp0." -AliasName "%~1" -ReleaseId "%~2" -AwsProfile "%PROFILE%"
exit /b %ERRORLEVEL%
:missing
echo ERROR: Alias and release ID required.
call :help
exit /b 1
:help
echo Usage: deploy-all-lambdas.bat alias release-id [aws-profile]
echo Example: deploy-all-lambdas.bat development 20260911-001 default
echo Promotes exact versions from a Ready S3 manifest. Never builds or publishes.
echo Rollback uses the same command with an earlier release ID.
echo IMPORTANT: The second argument is now a release ID, not Release/Debug.
exit /b 0
