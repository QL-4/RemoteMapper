@echo off
cd /d "%~dp0\.."
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /codepage:65001 /r:System.Web.Extensions.dll /out:tests\KeyMapConfigTests.exe tests\KeyMapConfigTests.cs src\KeyMapConfig.cs src\KeyMapEngine.cs src\KeyMapper.cs src\RemoteCatalog.cs
if errorlevel 1 exit /b %errorlevel%
tests\KeyMapConfigTests.exe
