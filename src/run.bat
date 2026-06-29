@echo off
setlocal

echo EquiTie Investor Assistant - Setup and Run
echo ==========================================
echo.

where podman >nul 2>&1
if errorlevel 1 (
    echo ERROR: Podman not found. Install from https://podman.io
    exit /b 1
)

echo Checking Podman connection...
podman info >nul 2>&1
if errorlevel 1 (
    echo Starting Podman machine...
    podman machine start
)

echo.
podman rm -f llm-server >nul 2>&1

echo Starting Ollama container...
podman run -d --name llm-server -p 11434:11434 ollama/ollama
if errorlevel 1 (
    echo Port 11434 may be in use. Check with: netstat -ano ^| findstr :11434
    exit /b 1
)

echo Waiting for Ollama API...
:wait_loop
timeout /t 2 /nobreak >nul
curl -s http://localhost:11434/api/tags >nul 2>&1
if errorlevel 1 goto wait_loop

for /f "usebackq delims=" %%i in (`powershell -NoProfile -File "%~dp0scripts\get-model.ps1" -ConfigPath "%~dp0InvestorAssistant\InvestorAssistant\appsettings.json"`) do set "MODEL=%%i"

echo Ollama API is ready.
echo Pulling %MODEL% model...
podman exec llm-server ollama pull %MODEL%

echo.
cd /d "%~dp0InvestorAssistant\InvestorAssistant"
set LLM__Endpoint=http://localhost:11434/v1
dotnet run

echo.
set /p CLEANUP=Stop and remove the container (y/N)?
if /i "!CLEANUP!"=="y" (
    podman stop llm-server
    podman rm llm-server
)
