@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help

where git >nul 2>nul
if errorlevel 1 (
    echo git was not found on PATH.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found on PATH.
    exit /b 1
)

pushd "%ROOT%"

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo This folder is not a Git repository: "%ROOT%"
    popd
    exit /b 1
)

for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%B"

echo Fetching latest code for branch %BRANCH%...
git fetch --all --prune
if errorlevel 1 (
    popd
    exit /b 1
)

echo Pulling latest commits with fast-forward only...
git pull --ff-only --recurse-submodules
if errorlevel 1 (
    echo.
    echo Could not fast-forward. Commit, stash, or resolve local changes, then try again.
    popd
    exit /b 1
)

echo Updating submodules...
git submodule update --init --recursive
if errorlevel 1 (
    popd
    exit /b 1
)

echo Restoring NuGet packages...
dotnet restore "%SOLUTION%"
set "RESTORE_EXIT=%ERRORLEVEL%"

popd
exit /b %RESTORE_EXIT%

:help
echo Usage:
echo   get-latest.bat
echo.
echo What it does:
echo   1. Fetches remote changes.
echo   2. Pulls the current branch using --ff-only.
echo   3. Updates Git submodules.
echo   4. Restores NuGet packages.
exit /b 0
