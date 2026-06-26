@echo off
setlocal

echo EquiTie Investor Assistant - Setup and Run
echo ==========================================
echo.

REM Try native Ollama first
where ollama >nul 2>&1
if not errorlevel 1 goto native

REM Fallback to Podman
where podman >nul 2>&1
if errorlevel 1 (
    echo ERROR: Neither Ollama nor Podman found.
    echo Install Ollama from https://ollama.com
    exit /b 1
)
goto podman

REM -------------------------------------------------------
:native
echo Using native Ollama...
ollama serve >nul 2>&1
if errorlevel 1 (
    echo Starting ollama.exe in background...
    start /b ollama serve
)
echo Waiting for Ollama API at http://localhost:11434 ...
:wait_native
timeout /t 2 /nobreak >nul
curl -s http://localhost:11434/api/tags >nul 2>&1
if errorlevel 1 goto wait_native
set LLM_ENDPOINT=http://localhost:11434/v1
goto ready

REM -------------------------------------------------------
:podman
echo Using Podman...
podman machine info >nul 2>&1
if errorlevel 1 (
    echo Starting Podman machine...
    podman machine start
)
podman rm -f llm-server >nul 2>&1
echo Starting ollama container...
podman run -d --name llm-server -p 11434:11434 ollama/ollama
if errorlevel 1 (
    echo Failed to start container. Try native Ollama instead.
    exit /b 1
)
echo Waiting for LLM at http://localhost:11434 ...
:wait_podman
timeout /t 2 /nobreak >nul
curl -s http://localhost:11434/api/tags >nul 2>&1
if errorlevel 1 goto wait_podman
set LLM_ENDPOINT=http://localhost:11434/v1
goto ready

REM -------------------------------------------------------
:ready
echo LLM API is ready at %LLM_ENDPOINT%

echo Pulling phi4-mini model...
ollama pull phi4-mini

echo.
cd /d "%~dp0InvestorAssistant\InvestorAssistant"
set LLM__Endpoint=%LLM_ENDPOINT%
dotnet run
