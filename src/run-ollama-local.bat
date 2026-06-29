@echo off
setlocal

echo EquiTie Investor Assistant - Setup and Run
echo ==========================================
echo.

where ollama >nul 2>&1
if errorlevel 1 (
    echo ERROR: Ollama not found. Install from https://ollama.com
    exit /b 1
)

echo Checking if Ollama is already running...
curl -s http://localhost:11434/api/tags >nul 2>&1
if errorlevel 1 (
    echo Starting Ollama...
    start /b ollama serve
    echo Waiting for Ollama API at http://localhost:11434 ...
    :wait_loop
    timeout /t 2 /nobreak >nul
    curl -s http://localhost:11434/api/tags >nul 2>&1
    if errorlevel 1 goto wait_loop
)

echo Ollama API is ready.
echo Pulling llama3.1 model...
ollama pull llama3.1

echo Preloading model...
curl -s -X POST http://localhost:11434/api/generate -d "{\"model\":\"llama3.1\",\"prompt\":\"hello\",\"keep_alive\":\"10m\",\"stream\":false}" >nul 2>&1
echo Model loaded.

echo.
cd /d "%~dp0InvestorAssistant\InvestorAssistant"
dotnet run -- Ollama
