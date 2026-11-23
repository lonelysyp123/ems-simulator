@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-up.ps1" %*
set "RC=%ERRORLEVEL%"
exit /b %RC%
