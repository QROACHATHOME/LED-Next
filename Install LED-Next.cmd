@echo off
rem Builds LED-Next and installs it (default D:\LED-Next) plus the LaunchBox/BigBox plugin.
rem Options are passed on, e.g.:  "Install LED-Next.cmd" -InstallDir E:\LED-Next -LaunchBox E:\LaunchBox
rem Remove:  "Install LED-Next.cmd" -Uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" %*
echo.
pause
