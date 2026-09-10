@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ============================================================
rem GiveAnywhere SocialPost Lambda Deployment
rem ============================================================
rem
rem Usage:
rem   deploy-all-lambdas.bat <alias> [configuration] [aws-profile]
rem
rem Examples:
rem   deploy-all-lambdas.bat development Release
rem   deploy-all-lambdas.bat production Release
rem   deploy-all-lambdas.bat development Release default
rem
rem Behavior:
rem   - Supports working source without an initial commit
rem   - Honors Git ignore rules when fingerprinting source
rem   - Builds solution once before deployment
rem   - Computes a deterministic working-source build ID for all Lambdas
rem   - Includes all API source, PlatformLibrary and configuration in build ID
rem   - Does NOT deploy when code has not changed
rem   - Does NOT publish a Lambda version when code has not changed
rem   - Publishes exactly one version when relevant code changes
rem   - Updates the requested alias to the published version
rem   - SocialPosts build ID in alias Description
rem   - Runs each Lambda deployment in a fresh child batch invocation
rem     to avoid repeated CALL :label failures in cmd.exe
rem
rem ============================================================

if /I "%~1"=="__worker" goto :WORKER_ENTRY

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%SocialPostAPIService.sln"
set "SHARED_PROJECT=SocialPostAPIService"

set "ALIAS_NAME=%~1"
set "CONFIGURATION=%~2"
set "AWS_PROFILE=%~3"

if /I "%~1"=="--help" goto :HELP
if /I "%~1"=="/?" goto :HELP
if /I "%~1"=="-h" goto :HELP

if "%ALIAS_NAME%"=="" (
    echo.
    echo ERROR: Lambda alias is required.
    echo.
    goto :HELP
)

if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
if "%AWS_PROFILE%"=="" set "AWS_PROFILE=default"

echo.
echo ============================================================
echo GiveAnywhere SocialPost Lambda Deployment
echo ============================================================
echo.
echo Solution:      "%SOLUTION%"
echo Configuration: %CONFIGURATION%
echo Target alias:  %ALIAS_NAME%
echo AWS profile:   %AWS_PROFILE%
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ============================================================
    echo ERROR: dotnet was not found.
    echo ============================================================
    exit /b 1
)

where aws >nul 2>nul
if errorlevel 1 (
    echo ============================================================
    echo ERROR: AWS CLI was not found.
    echo ============================================================
    exit /b 1
)

where git >nul 2>nul
if errorlevel 1 (
    echo ============================================================
    echo ERROR: Git was not found.
    echo ============================================================
    exit /b 1
)

if not exist "%SOLUTION%" (
    echo ============================================================
    echo ERROR: Solution not found.
    echo.
    echo "%SOLUTION%"
    echo ============================================================
    exit /b 1
)

if not exist "%ROOT%%SHARED_PROJECT%\" (
    echo ============================================================
    echo ERROR: Shared project directory not found.
    echo.
    echo "%ROOT%%SHARED_PROJECT%"
    echo ============================================================
    exit /b 1
)

dotnet lambda help >nul 2>nul
if errorlevel 1 (
    echo ============================================================
    echo ERROR: Amazon Lambda .NET Global Tool is not available.
    echo.
    echo Install with:
    echo.
    echo   dotnet tool install -g Amazon.Lambda.Tools
    echo.
    echo Or update with:
    echo.
    echo   dotnet tool update -g Amazon.Lambda.Tools
    echo ============================================================
    exit /b 1
)

pushd "%ROOT%"
if errorlevel 1 (
    echo ERROR: Unable to enter repository directory.
    exit /b 1
)

rem Hash working files, including uncommitted and untracked source in both repositories.
rem Git ignore rules exclude build output and the repository migration backup.
set "SOURCE_HASH="
for /f "delims=" %%H in ('powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $rows=New-Object System.Collections.Generic.List[string]; foreach($repo in @($env:ROOT, (Join-Path $env:ROOT '..\PlatformLibrary'))) { $files=@(git -C $repo -c core.quotepath=false ls-files --cached --others --exclude-standard); if($LASTEXITCODE -ne 0){throw 'Cannot enumerate source files'}; foreach($file in ($files | Sort-Object -Unique)) { $path=Join-Path $repo $file; if(Test-Path -LiteralPath $path -PathType Leaf){$rows.Add($file + ':' + [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($path))))} } }; if($rows.Count -eq 0){throw 'No source files found'}; $sha=[Security.Cryptography.SHA256]::Create(); [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join [char]10)))).Replace('-','').ToLowerInvariant()"') do set "SOURCE_HASH=%%H"
if not defined SOURCE_HASH (
    echo ERROR: Unable to calculate local source fingerprint.
    popd
    exit /b 1
)
echo Source hash:   %SOURCE_HASH%
echo.
echo ============================================================
echo Building SocialPost solution
echo ============================================================
echo.

call "%ROOT%build-all-lambdas.bat" "%CONFIGURATION%"
if errorlevel 1 (
    popd
    exit /b 1
)

echo.

echo ============================================================
echo Processing Lambda functions
echo ============================================================

call "%~f0" __worker SocialPostAPIAuthorizeSocialConnection "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIAuthorizeSocialConnection"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPICancelSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPICancelSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPICreateSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPICreateSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIDeleteSocialConnection "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIDeleteSocialConnection"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIDeleteSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIDeleteSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialConnection "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialConnection"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialConnections "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialConnections"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialPostAnalytics "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialPostAnalytics"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialPosts "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialPosts"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIGetSocialPostStatus "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIGetSocialPostStatus"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIPublishSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIPublishSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIScheduleSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIScheduleSocialPost"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPISocialConnectionCallback "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPISocialConnectionCallback"
    goto :DEPLOYMENT_FAILED
)

call "%~f0" __worker SocialPostAPIUpdateSocialPost "%ALIAS_NAME%" "%CONFIGURATION%" "%AWS_PROFILE%"
if errorlevel 1 (
    set "FAILED_PROJECT=SocialPostAPIUpdateSocialPost"
    goto :DEPLOYMENT_FAILED
)

echo.
echo ============================================================
echo SUCCESS
echo ============================================================
echo.
echo All SocialPost Lambda functions processed successfully.
echo.
echo Alias:         %ALIAS_NAME%
echo Configuration: %CONFIGURATION%
echo AWS profile:   %AWS_PROFILE%
echo Source hash:   %SOURCE_HASH%
echo.
echo Lambdas whose code did not change were NOT deployed and
echo did NOT receive a new Lambda version.
echo.
echo ============================================================

popd
exit /b 0

:DEPLOYMENT_FAILED

echo.
echo ============================================================
echo DEPLOYMENT FAILED
echo ============================================================
echo.
echo Failed project:
echo   %FAILED_PROJECT%
echo.
echo Deployment stopped.
echo No subsequent Lambda functions were processed.
echo.

popd
exit /b 1

:WORKER_ENTRY

set "PROJECT=%~2"
set "ALIAS_NAME=%~3"
set "CONFIGURATION=%~4"
set "AWS_PROFILE=%~5"

if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
if "%AWS_PROFILE%"=="" set "AWS_PROFILE=default"

set "ROOT=%~dp0"
set "SHARED_PROJECT=SocialPostAPIService"
set "PROJECT_DIR=%ROOT%%PROJECT%"
set "DEFAULTS_FILE=%PROJECT_DIR%\aws-lambda-tools-defaults.json"

set "FUNCTION_NAME="
set "REGION="
set "PROJECT_HASH="
set "SHARED_HASH="
set "BUILD_ID="
set "CURRENT_VERSION="
set "ALIAS_DESCRIPTION="
set "PUBLISHED_VERSION="
set "DEPLOY_EXIT="

pushd "%ROOT%"
if errorlevel 1 (
    echo.
    echo ERROR: Worker could not enter repository root.
    exit /b 1
)

echo.
echo ------------------------------------------------------------
echo Processing %PROJECT%
echo ------------------------------------------------------------

if not exist "%PROJECT_DIR%\" (
    echo.
    echo ERROR: Project directory does not exist:
    echo.
    echo   "%PROJECT_DIR%"
    echo.
    popd
    exit /b 1
)

if not exist "%DEFAULTS_FILE%" (
    echo.
    echo ERROR: aws-lambda-tools-defaults.json does not exist:
    echo.
    echo   "%DEFAULTS_FILE%"
    echo.
    popd
    exit /b 1
)

for /f "usebackq tokens=1,* delims=:" %%A in ("%DEFAULTS_FILE%") do (
    set "JSON_KEY=%%A"
    set "JSON_VALUE=%%B"

    set "JSON_KEY=!JSON_KEY:"=!"
    set "JSON_KEY=!JSON_KEY: =!"
    set "JSON_KEY=!JSON_KEY:	=!"

    if /I "!JSON_KEY!"=="function-name" set "FUNCTION_NAME=!JSON_VALUE!"
    if /I "!JSON_KEY!"=="region" set "REGION=!JSON_VALUE!"
)

set "FUNCTION_NAME=!FUNCTION_NAME:,=!"
set "FUNCTION_NAME=!FUNCTION_NAME:"=!"
set "FUNCTION_NAME=!FUNCTION_NAME: =!"
set "FUNCTION_NAME=!FUNCTION_NAME:	=!"

set "REGION=!REGION:,=!"
set "REGION=!REGION:"=!"
set "REGION=!REGION: =!"
set "REGION=!REGION:	=!"

if "!FUNCTION_NAME!"=="" (
    echo.
    echo ERROR: Unable to read function-name from:
    echo.
    echo   "%DEFAULTS_FILE%"
    echo.
    popd
    exit /b 1
)

if "!REGION!"=="" set "REGION=us-east-1"

echo Function:       !FUNCTION_NAME!
echo Region:         !REGION!

set "BUILD_ID=source-v1:!SOURCE_HASH!-%CONFIGURATION%"
echo Build ID:       !BUILD_ID!

for /f "usebackq delims=" %%V in (`aws lambda get-alias ^
    --function-name "!FUNCTION_NAME!" ^
    --name "%ALIAS_NAME%" ^
    --region "!REGION!" ^
    --profile "%AWS_PROFILE%" ^
    --query FunctionVersion ^
    --output text 2^>nul`) do (
    set "CURRENT_VERSION=%%V"
)

for /f "usebackq delims=" %%D in (`aws lambda get-alias ^
    --function-name "!FUNCTION_NAME!" ^
    --name "%ALIAS_NAME%" ^
    --region "!REGION!" ^
    --profile "%AWS_PROFILE%" ^
    --query Description ^
    --output text 2^>nul`) do (
    set "ALIAS_DESCRIPTION=%%D"
)

if /I "!ALIAS_DESCRIPTION!"=="None" set "ALIAS_DESCRIPTION="

if not "!CURRENT_VERSION!"=="" (
    echo Current alias:   %ALIAS_NAME% -^> !CURRENT_VERSION!
) else (
    echo Current alias:   not found
)

if not "!CURRENT_VERSION!"=="" (
    if "!ALIAS_DESCRIPTION!"=="!BUILD_ID!" (
        echo.
        echo NO CODE CHANGE
        echo.
        echo Project:      %PROJECT%
        echo Lambda:       !FUNCTION_NAME!
        echo Alias:        %ALIAS_NAME%
        echo Version:      !CURRENT_VERSION!
        echo.
        echo NO deployment.
        echo NO publish-version.
        echo NO version bump.
        echo.
        popd
        exit /b 0
    )
)

echo.
echo CODE CHANGE DETECTED
echo.
echo Project:          %PROJECT%
echo Lambda:           !FUNCTION_NAME!
echo Deploying updated code to $LATEST...
echo.

pushd "%PROJECT_DIR%"
if errorlevel 1 (
    echo.
    echo ERROR: Unable to enter Lambda project directory.
    echo Project: %PROJECT%
    echo.
    popd
    exit /b 1
)

dotnet lambda deploy-function ^
    --function-name "!FUNCTION_NAME!" ^
    --region "!REGION!" ^
    --configuration "%CONFIGURATION%" ^
    --disable-interactive true ^
    --profile "%AWS_PROFILE%"

set "DEPLOY_EXIT=!ERRORLEVEL!"
popd

if not "!DEPLOY_EXIT!"=="0" (
    echo.
    echo ERROR: Lambda deployment failed.
    echo Project: %PROJECT%
    echo Lambda:  !FUNCTION_NAME!
    echo Exit:    !DEPLOY_EXIT!
    echo.
    popd
    exit /b 1
)

echo.
echo Waiting for Lambda update to complete...

aws lambda wait function-updated ^
    --function-name "!FUNCTION_NAME!" ^
    --region "!REGION!" ^
    --profile "%AWS_PROFILE%"

if errorlevel 1 (
    echo.
    echo ERROR: Lambda update wait failed.
    echo Project: %PROJECT%
    echo Lambda:  !FUNCTION_NAME!
    echo.
    popd
    exit /b 1
)

echo.
echo Publishing new Lambda version...

for /f "usebackq delims=" %%V in (`aws lambda publish-version ^
    --function-name "!FUNCTION_NAME!" ^
    --description "!BUILD_ID!" ^
    --region "!REGION!" ^
    --profile "%AWS_PROFILE%" ^
    --query Version ^
    --output text`) do (
    set "PUBLISHED_VERSION=%%V"
)

if "!PUBLISHED_VERSION!"=="" (
    echo.
    echo ERROR: Unable to determine published Lambda version.
    echo Project: %PROJECT%
    echo Lambda:  !FUNCTION_NAME!
    echo.
    popd
    exit /b 1
)

echo Published:      !PUBLISHED_VERSION!

if "!CURRENT_VERSION!"=="" (
    echo Creating alias %ALIAS_NAME%...

    aws lambda create-alias ^
        --function-name "!FUNCTION_NAME!" ^
        --name "%ALIAS_NAME%" ^
        --function-version "!PUBLISHED_VERSION!" ^
        --description "!BUILD_ID!" ^
        --region "!REGION!" ^
        --profile "%AWS_PROFILE%" >nul

    if errorlevel 1 (
        echo.
        echo ERROR: Unable to create Lambda alias.
        echo Project: %PROJECT%
        echo Lambda:  !FUNCTION_NAME!
        echo Alias:   %ALIAS_NAME%
        echo.
        popd
        exit /b 1
    )
) else (
    echo Updating alias %ALIAS_NAME%...

    aws lambda update-alias ^
        --function-name "!FUNCTION_NAME!" ^
        --name "%ALIAS_NAME%" ^
        --function-version "!PUBLISHED_VERSION!" ^
        --description "!BUILD_ID!" ^
        --region "!REGION!" ^
        --profile "%AWS_PROFILE%" >nul

    if errorlevel 1 (
        echo.
        echo ERROR: Unable to update Lambda alias.
        echo Project: %PROJECT%
        echo Lambda:  !FUNCTION_NAME!
        echo Alias:   %ALIAS_NAME%
        echo.
        popd
        exit /b 1
    )
)

echo.
echo DEPLOYED
echo.
echo Project:        %PROJECT%
echo Lambda:         !FUNCTION_NAME!
echo Version:        !PUBLISHED_VERSION!
echo Alias:          %ALIAS_NAME%
echo Build ID:       !BUILD_ID!
echo.

popd
exit /b 0

:HELP

echo.
echo GiveAnywhere SocialPost Lambda Deployment
echo.
echo Usage:
echo.
echo   deploy-all-lambdas.bat ^<alias^> [configuration] [aws-profile]
echo.
echo Examples:
echo.
echo   deploy-all-lambdas.bat development Release
echo   deploy-all-lambdas.bat production Release
echo   deploy-all-lambdas.bat development Release default
echo.
echo Parameters:
echo.
echo   alias
echo       Required.
echo       Lambda alias to create/update.
echo.
echo   configuration
echo       Optional.
echo       Defaults to Release.
echo.
echo   aws-profile
echo       Optional.
echo       Defaults to default.
echo.

exit /b 1
