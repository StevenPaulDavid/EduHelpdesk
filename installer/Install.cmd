@echo off
rem Installs EduHelpdesk, or upgrades it to this version. Double-click it: it asks Windows for administrator rights,
rem then runs Install.ps1. For an unattended install:  Install.cmd -AnswersFile install-answers.json
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
echo.
pause
