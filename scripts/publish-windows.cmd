@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-windows.ps1" %*
set "RC=%ERRORLEVEL%"
exit /b %RC%
