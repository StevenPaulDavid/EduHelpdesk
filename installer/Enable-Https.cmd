@echo off
rem Switches an installed EduHelpdesk to HTTPS, or replaces its certificate. Double-click it: it asks Windows for
rem administrator rights, then runs Enable-Https.ps1.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Enable-Https.ps1" %*
echo.
pause
