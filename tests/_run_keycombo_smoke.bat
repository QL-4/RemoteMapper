@echo off
cd /d "%~dp0\.."
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /codepage:65001 /r:System.Windows.Forms.dll /out:tests\KeyComboSenderSmoke.exe tests\KeyComboSenderSmoke.cs src\KeyComboSender.cs
if errorlevel 1 exit /b %errorlevel%
tests\KeyComboSenderSmoke.exe
