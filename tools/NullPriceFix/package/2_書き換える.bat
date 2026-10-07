@echo off
rem Rewrites empty prices to 0 after asking y/N. Makes a backup first.
chcp 65001 >nul
"%~dp0NullPriceFix.exe" --apply
echo.
pause
