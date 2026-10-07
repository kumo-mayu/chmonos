@echo off
rem Counts purchase records whose price is empty. Changes nothing.
chcp 65001 >nul
"%~dp0NullPriceFix.exe"
echo.
pause
