@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"
set "AWS_PROFILE=%~1"
set "CONFIGURATION=%~2"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

set "PROFILE_ARG="
if not "%AWS_PROFILE%"=="" set "PROFILE_ARG=--profile %AWS_PROFILE%"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found on PATH.
    exit /b 1
)

where aws >nul 2>nul
if errorlevel 1 (
    echo AWS CLI was not found on PATH.
    exit /b 1
)

dotnet lambda help >nul 2>nul
if errorlevel 1 (
    echo Amazon.Lambda.Tools is not installed or not available.
    echo Install it with: dotnet tool install -g Amazon.Lambda.Tools
    exit /b 1
)

echo Building all SocialPost Lambda projects...
echo Solution: "%SOLUTION%"
echo Configuration: %CONFIGURATION%
if not "%AWS_PROFILE%"=="" echo AWS profile: %AWS_PROFILE%
echo.

dotnet build "%SOLUTION%" -c "%CONFIGURATION%"
if errorlevel 1 (
    echo Build failed. Publish stopped.
    exit /b 1
)

call :publish_project SocialPostAPIAuthorizeSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPICancelSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPICreateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIDeleteSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIDeleteSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialConnections
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialPostAnalytics
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialPosts
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIGetSocialPostStatus
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIPublishSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIScheduleSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPISocialConnectionCallback
if errorlevel 1 exit /b %ERRORLEVEL%
call :publish_project SocialPostAPIUpdateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo All Lambda functions were uploaded to $LATEST and published as immutable versions.
exit /b 0

:publish_project
set "PROJECT=%~1"
set "PROJECT_DIR=%ROOT%%PROJECT%"
set "DEFAULTS_FILE=%PROJECT_DIR%\aws-lambda-tools-defaults.json"
set "FUNCTION_NAME="
set "REGION="
set "PUBLISHED_VERSION="

echo.
echo ============================================================
echo Publishing %PROJECT%
echo ============================================================

if not exist "%DEFAULTS_FILE%" (
    echo Missing aws-lambda-tools-defaults.json for %PROJECT%.
    exit /b 1
)

call :read_defaults
if errorlevel 1 exit /b %ERRORLEVEL%

pushd "%PROJECT_DIR%"
dotnet lambda deploy-function --configuration "%CONFIGURATION%" --disable-interactive true %PROFILE_ARG%
set "DEPLOY_EXIT=%ERRORLEVEL%"
popd

if not "%DEPLOY_EXIT%"=="0" (
    echo Deployment failed for %PROJECT%.
    exit /b %DEPLOY_EXIT%
)

echo Waiting for %FUNCTION_NAME% update to complete...
aws lambda wait function-updated --function-name "%FUNCTION_NAME%" --region "%REGION%" %PROFILE_ARG%
if errorlevel 1 (
    echo AWS wait failed for %FUNCTION_NAME%.
    exit /b 1
)

echo Publishing immutable version for %FUNCTION_NAME%...
for /f "usebackq delims=" %%V in (`aws lambda publish-version --function-name "%FUNCTION_NAME%" --region "%REGION%" %PROFILE_ARG% --query Version --output text`) do set "PUBLISHED_VERSION=%%V"

if "%PUBLISHED_VERSION%"=="" (
    echo Failed to publish a version for %FUNCTION_NAME%.
    exit /b 1
)

echo Published %FUNCTION_NAME% version %PUBLISHED_VERSION%.
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
echo   publish-all-lambdas.bat [aws-profile] [configuration]
echo.
echo Examples:
echo   publish-all-lambdas.bat default Release
echo   publish-all-lambdas.bat prod Release
echo.
echo What it does:
echo   1. Builds SocialPostAPIService.sln.
echo   2. Uploads each Lambda project to $LATEST.
echo   3. Waits for each Lambda update to complete.
echo   4. Publishes a new immutable numbered version for each Lambda.
echo.
echo It does not create or update aliases. Use promote-all-lambdas.bat for that.
exit /b 0
