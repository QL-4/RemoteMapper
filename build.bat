@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WINMD=C:\Windows\System32\WinMetadata
set RUNTIME=C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll

"%CSC%" /nologo /target:exe /platform:x64 /codepage:65001 ^
  /r:"%WINMD%\Windows.Devices.winmd" ^
  /r:"%WINMD%\Windows.Foundation.winmd" ^
  /r:"%WINMD%\Windows.Storage.winmd" ^
  /r:"%RUNTIME%" ^
  /r:System.Windows.Forms.dll ^
  /r:System.Drawing.dll ^
  /r:System.Web.Extensions.dll ^
  /r:Microsoft.CSharp.dll ^
  /win32icon:ui\app.ico ^
  /out:RemoteMic.new.exe ^
  src\RemoteMic.cs src\KeyMapConfig.cs src\KeyMapEngine.cs src\KeyMapper.cs src\KeyComboSender.cs src\RemoteCatalog.cs src\KeyMapPanel.cs src\KeySnippet.cs
if errorlevel 1 exit /b %errorlevel%

move /y RemoteMic.new.exe RemoteMic.exe >nul
if errorlevel 1 (
  echo Failed to replace RemoteMic.exe. Stop the running app and retry.
  exit /b 1
)
echo Built RemoteMic.exe
