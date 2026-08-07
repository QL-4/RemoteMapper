@echo off
cd /d "%~dp0\.."
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /codepage:65001 /r:System.Windows.Forms.dll /out:tools\RemoteKeyTest.exe tools\RemoteKeyTest.cs
