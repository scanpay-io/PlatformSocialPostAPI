@echo off
setlocal

set "ROOT=%~dp0"
set "ALIAS_NAME=%~1"
set "AWS_PROFILE=%~2"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help

if "%ALIAS_NAME%"=="" (
    echo Alias is required.
    echo.
    goto :help
)

set "PROFILE_ARG="
if not "%AWS_PROFILE%"=="" set "PROFILE_ARG=--profile %AWS_PROFILE%"

where aws >nul 2>nul
if errorlevel 1 (
    echo AWS CLI was not found on PATH.
    exit /b 1
)

where powershell >nul 2>nul
if errorlevel 1 (
    echo powershell was not found on PATH.
    exit /b 1
)

echo Promoting latest published SocialPost Lambda versions...
echo Target alias: %ALIAS_NAME%
if not "%AWS_PROFILE%"=="" echo AWS profile: %AWS_PROFILE%
echo.

call :promote_project SocialPostAPIAuthorizeSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPICancelSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPICreateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIDeleteSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIDeleteSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialConnections
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialPostAnalytics
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialPosts
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIGetSocialPostStatus
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIPublishSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIScheduleSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPISocialConnectionCallback
if errorlevel 1 exit /b %ERRORLEVEL%
call :promote_project SocialPostAPIUpdateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo All Lambda aliases were promoted to the latest published versions.
exit /b 0

:promote_project
set "PROJECT=%~1"
set "PROJECT_DIR=%ROOT%%PROJECT%"
set "DEFAULTS_FILE=%PROJECT_DIR%\aws-lambda-tools-defaults.json"
set "FUNCTION_NAME="
set "REGION="
set "LATEST_VERSION="

echo.
echo ============================================================
echo Promoting %PROJECT%
echo ============================================================

if not exist "%DEFAULTS_FILE%" (
    echo Missing aws-lambda-tools-defaults.json for %PROJECT%.
    exit /b 1
)

call :read_defaults
if errorlevel 1 exit /b %ERRORLEVEL%

for /f "usebackq delims=" %%V in (`aws lambda list-versions-by-function --function-name "%FUNCTION_NAME%" --region "%REGION%" %PROFILE_ARG% --query "Versions[?Version!='$LATEST'].Version | [-1]" --output text`) do set "LATEST_VERSION=%%V"

if "%LATEST_VERSION%"=="" (
    echo No published numbered version found for %FUNCTION_NAME%.
    exit /b 1
)

aws lambda get-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --region "%REGION%" %PROFILE_ARG% >nul 2>nul
if errorlevel 1 (
    echo Creating alias %ALIAS_NAME% -^> version %LATEST_VERSION%...
    aws lambda create-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --function-version "%LATEST_VERSION%" --region "%REGION%" %PROFILE_ARG% >nul
) else (
    echo Updating alias %ALIAS_NAME% -^> version %LATEST_VERSION%...
    aws lambda update-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --function-version "%LATEST_VERSION%" --region "%REGION%" %PROFILE_ARG% >nul
)

if errorlevel 1 (
    echo Failed to create or update alias %ALIAS_NAME% for %FUNCTION_NAME%.
    exit /b 1
)

echo Promoted %FUNCTION_NAME%:%ALIAS_NAME% to version %LATEST_VERSION%.
exit /b 0

:read_defaults
for /f "usebackq delims=" %%F in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$json = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath $env:DEFAULTS_FILE); $json.'function-name'"`) do set "FUNCTION_NAME=%%F"
for /f "usebackq delims=" %%R in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$json = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath $env:DEFAULTS_FILE); $json.region"`) do set "REGION=%%R"

if "%FUNCTION_NAME%"=="" (
    echo Could not read function-name from %DEFAULTS_FILE%.
    exit /b 1
)

if "%REGION%"=="" set "REGION=us-east-1"
exit /b 0

:help
echo Usage:
echo   promote-all-lambdas.bat alias [aws-profile]
echo.
echo Examples:
echo   promote-all-lambdas.bat development default
echo   promote-all-lambdas.bat gany_prod prod
echo.
echo What it does:
echo   1. Finds the latest published numbered version for each SocialPost Lambda.
echo   2. Creates or updates the requested alias to point to that version.
echo.
echo It does not build, upload code, or publish new versions.
exit /b 0
