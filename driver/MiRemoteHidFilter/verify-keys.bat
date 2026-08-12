@echo off
cd /d "%~dp0\..\.."
echo Stop RemoteMic first so its global mappings do not run during this test.
echo Expected with MiRemoteHidFilter loaded:
echo   Direction Up = VK_UP, VK 0x26
echo   Volume Up    = F13,   VK 0x7C
echo   Volume Down  = F14,   VK 0x7D
echo   Back         = F15,   VK 0x7E
echo   Home         = F16,   VK 0x7F
echo   Menu         = F17,   VK 0x80
echo   Live         = F18,   VK 0x81
echo   Power        = F19,   VK 0x82
echo   Voice        = F20,   VK 0x83
echo.
tools\RemoteKeyTest.exe
pause
