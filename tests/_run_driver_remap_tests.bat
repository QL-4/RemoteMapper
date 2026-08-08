@echo off
setlocal
cd /d "%~dp0\.."
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
cl.exe /nologo /W4 /WX /Itests /Fe:tests\remap_test.exe tests\remap_test.c driver\MiRemoteHidFilter\remap.c
if errorlevel 1 exit /b %errorlevel%
tests\remap_test.exe
