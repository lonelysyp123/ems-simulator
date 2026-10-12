@echo off
cd /d "%~dp0"
title EssSimulator (B/S)
echo Starting EssSimulator (B/S architecture)...
echo Web UI: http://localhost:5050
echo Modbus TCP ports: see appsettings.json -^> Simulator.Protocol
echo.
start "" http://localhost:5050

:run
EssSimulator.exe
echo.

if exist "%~dp0.restart" (
    echo [Restart] Reinitialize requested, restarting backend...
    del "%~dp0.restart" 2>nul
    timeout /t 2 /nobreak >nul
    goto run
)

echo Program exited. Press any key to close.
pause >nul
