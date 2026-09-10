@echo off
setlocal EnableExtensions

rem ============================================================
rem GiveAnywhere SocialPost - Build All Lambdas
rem ============================================================
rem
rem Usage:
rem   build-all-lambdas.bat
rem   build-all-lambdas.bat Release
rem   build-all-lambdas.bat Debug
rem
rem Behavior:
rem   - Validates dotnet is available
rem   - Validates the solution exists
rem   - Cleans the solution
rem   - Restores the solution
rem   - Builds the solution
rem   - Stops immediately on failure
rem
rem ============================================================


rem ============================================================
rem ROOT CONFIGURATION
rem ============================================================

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"


rem ============================================================
rem ARGUMENTS
rem ============================================================

set "CONFIGURATION=%~1"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if /I "%~1"=="-h" goto :help

if "%CONFIGURATION%"=="" (
    set "CONFIGURATION=Release"
)


rem ============================================================
rem HEADER
rem ============================================================

echo.
echo ============================================================
echo GiveAnywhere SocialPost - Build All Lambdas
echo ============================================================
echo.
echo Solution:      "%SOLUTION%"
echo Configuration: %CONFIGURATION%
echo.
echo ============================================================
echo.


rem ============================================================
rem VALIDATE DOTNET
rem ============================================================

where dotnet >nul 2>nul

if errorlevel 1 (
    echo.
    echo ============================================================
    echo ERROR: dotnet was not found.
    echo ============================================================
    echo.
    echo Make sure the .NET SDK is installed and dotnet is in PATH.
    echo.
    exit /b 1
)


rem ============================================================
rem VALIDATE SOLUTION
rem ============================================================

if not exist "%SOLUTION%" (
    echo.
    echo ============================================================
    echo ERROR: Solution not found.
    echo ============================================================
    echo.
    echo "%SOLUTION%"
    echo.
    exit /b 1
)


rem ============================================================
rem CLEAN
rem ============================================================

echo Cleaning solution...
echo.

dotnet clean "%SOLUTION%" -c "%CONFIGURATION%"

if errorlevel 1 (
    echo.
    echo ============================================================
    echo ERROR: CLEAN FAILED
    echo ============================================================
    echo.
    exit /b 1
)


rem ============================================================
rem RESTORE
rem ============================================================

echo.
echo Restoring solution...
echo.

dotnet restore "%SOLUTION%"

if errorlevel 1 (
    echo.
    echo ============================================================
    echo ERROR: RESTORE FAILED
    echo ============================================================
    echo.
    exit /b 1
)


rem ============================================================
rem BUILD
rem ============================================================

echo.
echo Building solution...
echo.

dotnet build "%SOLUTION%" ^
    -c "%CONFIGURATION%" ^
    --no-restore -m:1

if errorlevel 1 (
    echo.
    echo ============================================================
    echo ERROR: BUILD FAILED
    echo ============================================================
    echo.
    exit /b 1
)


rem ============================================================
rem SUCCESS
rem ============================================================

echo.
echo ============================================================
echo SUCCESS
echo ============================================================
echo.
echo All SocialPost Lambda projects built successfully.
echo.
echo Configuration: %CONFIGURATION%
echo.
echo ============================================================

exit /b 0


rem ============================================================
rem HELP
rem ============================================================

:help

echo.
echo GiveAnywhere SocialPost - Build All Lambdas
echo.
echo Usage:
echo.
echo   build-all-lambdas.bat [configuration]
echo.
echo Examples:
echo.
echo   build-all-lambdas.bat
echo.
echo   build-all-lambdas.bat Release
echo.
echo   build-all-lambdas.bat Debug
echo.
echo Parameters:
echo.
echo   configuration
echo       Optional.
echo       Defaults to Release.
echo.

exit /b 0