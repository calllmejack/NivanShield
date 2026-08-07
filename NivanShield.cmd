@echo off
setlocal
cd /d "%~dp0"
set "EXPECTED_VERSION=6.0.5"
set "BUILT_VERSION="

if not exist "%~dp0NivanShield.exe" goto build
if not exist "%~dp0app\build-version.txt" goto build
set /p BUILT_VERSION=<"%~dp0app\build-version.txt"
if /i not "%BUILT_VERSION%"=="%EXPECTED_VERSION%" goto build
goto launch

:build
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Build.ps1" -Quiet
if errorlevel 1 exit /b 1

:launch
start "" "%~dp0NivanShield.exe"
exit /b 0
