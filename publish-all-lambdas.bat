@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION="
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

call :find_solution
if errorlevel 1 exit /b %ERRORLEVEL%

call :ensure_lambda_projects
if errorlevel 1 exit /b %ERRORLEVEL%

echo Building Lambda solution...
echo Solution: "%SOLUTION%"
echo Configuration: %CONFIGURATION%
if not "%AWS_PROFILE%"=="" echo AWS profile: %AWS_PROFILE%
echo.

dotnet build "%SOLUTION%" -c "%CONFIGURATION%"
if errorlevel 1 (
    echo Build failed. Publish stopped.
    exit /b 1
)

for /d %%D in ("%ROOT%*") do (
    if exist "%%~fD\aws-lambda-tools-defaults.json" (
        call :publish_project "%%~nxD"
        if errorlevel 1 exit /b 1
    )
)

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
echo   1. Builds the .sln file in this folder.
echo   2. Uploads each Lambda project to $LATEST.
echo   3. Waits for each Lambda update to complete.
echo   4. Publishes a new immutable numbered version for each Lambda.
echo.
echo It does not create or update aliases. Use promote-all-lambdas.bat for that.
exit /b 0

:find_solution
for %%S in ("%ROOT%*.sln") do (
    if not defined SOLUTION set "SOLUTION=%%~fS"
)

if "%SOLUTION%"=="" (
    echo No .sln file was found in "%ROOT%".
    exit /b 1
)

exit /b 0

:ensure_lambda_projects
set "LAMBDA_PROJECT_COUNT=0"
for /d %%D in ("%ROOT%*") do (
    if exist "%%~fD\aws-lambda-tools-defaults.json" set /a LAMBDA_PROJECT_COUNT+=1
)

if "%LAMBDA_PROJECT_COUNT%"=="0" (
    echo No Lambda project folders with aws-lambda-tools-defaults.json were found in "%ROOT%".
    exit /b 1
)

echo Found %LAMBDA_PROJECT_COUNT% Lambda project folder(s).
exit /b 0
