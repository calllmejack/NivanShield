@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1"
if errorlevel 1 (
    echo.
    echo Build failed. Review the error above.
    pause
    exit /b 1
)
pause
