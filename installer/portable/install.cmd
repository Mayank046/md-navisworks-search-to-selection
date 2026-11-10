@echo off
rem MD Search to Selection - Install (no admin). Author: MD (Mayank Dholariya)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0md_searchtoselection.ps1" -Action Install
echo.
pause
