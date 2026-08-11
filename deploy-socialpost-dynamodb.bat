@echo off
setlocal

set "ROOT=%~dp0"
set "STACK_NAME=%~1"
set "AWS_PROFILE=%~2"
set "REGION=%~3"
set "TEMPLATE=%ROOT%infra\socialpost-dynamodb.yaml"

if /I "%~1"=="--help" goto :help
if /I "%~1"=="/?" goto :help
if "%STACK_NAME%"=="" set "STACK_NAME=platform-socialpost-dynamodb"
if "%REGION%"=="" set "REGION=us-east-1"

set "PROFILE_ARG="
if not "%AWS_PROFILE%"=="" set "PROFILE_ARG=--profile %AWS_PROFILE%"

where aws >nul 2>nul
if errorlevel 1 (
    echo AWS CLI was not found on PATH.
    exit /b 1
)

if not exist "%TEMPLATE%" (
    echo Missing template: "%TEMPLATE%"
    exit /b 1
)

echo Deploying PlatformSocialPostAPI DynamoDB stack...
echo Stack: %STACK_NAME%
echo Region: %REGION%
if not "%AWS_PROFILE%"=="" echo AWS profile: %AWS_PROFILE%
echo Template: "%TEMPLATE%"
echo.

aws cloudformation deploy ^
    --stack-name "%STACK_NAME%" ^
    --template-file "%TEMPLATE%" ^
    --region "%REGION%" ^
    %PROFILE_ARG%

exit /b %ERRORLEVEL%

:help
echo Usage:
echo   deploy-socialpost-dynamodb.bat [stack-name] [aws-profile] [region]
echo.
echo Examples:
echo   deploy-socialpost-dynamodb.bat
echo   deploy-socialpost-dynamodb.bat platform-socialpost-dynamodb default us-east-1
exit /b 0
