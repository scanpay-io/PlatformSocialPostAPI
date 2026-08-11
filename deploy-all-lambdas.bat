@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"
set "ALIAS_NAME=%~1"
set "AWS_PROFILE=%~2"
set "CONFIGURATION=%~3"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

if "%ALIAS_NAME%"=="" (
    echo Alias is required.
    echo.
    goto :help
)

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
echo Target alias: %ALIAS_NAME%
if not "%AWS_PROFILE%"=="" echo AWS profile: %AWS_PROFILE%
echo.

dotnet build "%SOLUTION%" -c "%CONFIGURATION%"
if errorlevel 1 (
    echo Build failed. Deployment stopped.
    exit /b 1
)

call :deploy_and_promote SocialPostAPIAuthorizeSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPICancelSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPICreateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIDeleteSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIDeleteSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialConnection
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialConnections
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialPostAnalytics
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialPosts
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIGetSocialPostStatus
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIPublishSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIScheduleSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPISocialConnectionCallback
if errorlevel 1 exit /b %ERRORLEVEL%
call :deploy_and_promote SocialPostAPIUpdateSocialPost
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo All Lambda deployments completed and alias "%ALIAS_NAME%" was promoted.
exit /b 0

:deploy_and_promote
set "PROJECT=%~1"
set "PROJECT_DIR=%ROOT%%PROJECT%"
set "DEFAULTS_FILE=%PROJECT_DIR%\aws-lambda-tools-defaults.json"
set "FUNCTION_NAME="
set "REGION="
set "PUBLISHED_VERSION="

echo.
echo ============================================================
echo Deploying %PROJECT%
echo ============================================================

if not exist "%DEFAULTS_FILE%" (
    echo Missing aws-lambda-tools-defaults.json for %PROJECT%.
    exit /b 1
)

for /f "usebackq delims=" %%F in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$json = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath $env:DEFAULTS_FILE); $json.'function-name'"`) do set "FUNCTION_NAME=%%F"
for /f "usebackq delims=" %%R in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$json = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath $env:DEFAULTS_FILE); $json.region"`) do set "REGION=%%R"

if "%FUNCTION_NAME%"=="" (
    echo Could not read function-name from %DEFAULTS_FILE%.
    exit /b 1
)

if "%REGION%"=="" set "REGION=us-east-1"

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

aws lambda get-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --region "%REGION%" %PROFILE_ARG% >nul 2>nul
if errorlevel 1 (
    echo Creating alias %ALIAS_NAME% -^> version %PUBLISHED_VERSION%...
    aws lambda create-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --function-version "%PUBLISHED_VERSION%" --region "%REGION%" %PROFILE_ARG% >nul
) else (
    echo Updating alias %ALIAS_NAME% -^> version %PUBLISHED_VERSION%...
    aws lambda update-alias --function-name "%FUNCTION_NAME%" --name "%ALIAS_NAME%" --function-version "%PUBLISHED_VERSION%" --region "%REGION%" %PROFILE_ARG% >nul
)

if errorlevel 1 (
    echo Failed to create or update alias %ALIAS_NAME% for %FUNCTION_NAME%.
    exit /b 1
)

echo Promoted %FUNCTION_NAME%:%ALIAS_NAME% to version %PUBLISHED_VERSION%.
exit /b 0

:help
echo Usage:
echo   deploy-all-lambdas.bat alias [aws-profile] [configuration]
echo.
echo Examples:
echo   deploy-all-lambdas.bat development default Release
echo   deploy-all-lambdas.bat gany_dev default Release
echo   deploy-all-lambdas.bat gany_prod prod Release
echo.
echo What it does:
echo   1. Builds SocialPostAPIService.sln.
echo   2. Deploys each Lambda project to $LATEST with dotnet lambda deploy-function.
echo   3. Waits for the Lambda update to complete.
echo   4. Publishes a new immutable Lambda version.
echo   5. Creates or updates the requested alias to point to that version.
exit /b 0
