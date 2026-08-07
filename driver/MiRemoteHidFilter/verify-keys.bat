@echo off
cd /d "%~dp0\..\.."
echo Expected with MiRemoteHidFilter loaded:
echo   Direction Up = VK_UP, VK 0x26
echo   Volume Up    = F13,   VK 0x7C
echo   Volume Down  = F14,   VK 0x7D
echo   Back         = F15,   VK 0x7E
echo.
tools\RemoteKeyTest.exe
pause
