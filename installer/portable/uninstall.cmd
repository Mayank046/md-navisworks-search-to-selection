@echo off
rem MD Search to Selection - Uninstall (no admin). Author: MD (Mayank Dholariya)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0md_searchtoselection.ps1" -Action Uninstall
echo.
pause
