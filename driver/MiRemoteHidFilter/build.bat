@echo off
setlocal
cd /d "%~dp0"
set MSBUILD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe
if not exist "%MSBUILD%" (
  echo MSBuild not found.
  exit /b 1
)
set CONFIGURATION=%~1
if "%CONFIGURATION%"=="" set CONFIGURATION=Debug
"%MSBUILD%" MiRemoteHidFilter.vcxproj /m /p:Configuration=%CONFIGURATION% /p:Platform=x64
if errorlevel 1 exit /b %errorlevel%

if /I "%CONFIGURATION%"=="Release" (
  if not exist package mkdir package
  copy /y "x64\Release\MiRemoteHidFilter\MiRemoteHidFilter.inf" "package\" >nul
  copy /y "x64\Release\MiRemoteHidFilter\MiRemoteHidFilter.sys" "package\" >nul
  copy /y "x64\Release\MiRemoteHidFilter\miremotehidfilter.cat" "package\" >nul
  copy /y "x64\Release\MiRemoteHidFilter.cer" "package\MiRemoteHidFilter.cer" >nul
  echo Refreshed package\ from Release build.
)
