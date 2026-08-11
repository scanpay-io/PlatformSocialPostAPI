@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"
set "CONFIGURATION=%~1"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found on PATH.
    exit /b 1
)

echo Building all SocialPost Lambda projects...
echo Solution: "%SOLUTION%"
echo Configuration: %CONFIGURATION%
echo.

dotnet build "%SOLUTION%" -c "%CONFIGURATION%"
exit /b %ERRORLEVEL%

:help
echo Usage:
echo   build-all-lambdas.bat [configuration]
echo.
echo Examples:
echo   build-all-lambdas.bat
echo   build-all-lambdas.bat Release
exit /b 0