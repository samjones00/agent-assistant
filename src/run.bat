@echo off
setlocal enabledelayedexpansion

set MODEL=llama3.1:latest

:parse_args
if "%~1"=="" goto :done_args
if /i "%~1"=="--model" (
    set MODEL=%~2
    shift
    shift
    goto :parse_args
)
shift
goto :parse_args

:done_args
echo EquiTie Investor Assistant - Setup and Run
echo ==========================================
echo.
echo Model: %MODEL%
echo.

where ollama >nul 2>&1
if errorlevel 1 (
    echo ERROR: Ollama not found. Install from https://ollama.com
    exit /b 1
)

echo Checking Ollama server...
curl -s http://localhost:11434/api/tags >nul 2>&1
if errorlevel 1 (
    echo Starting Ollama server...
    start /b ollama serve >nul 2>&1
    timeout /t 3 /nobreak >nul
)

echo Pulling %MODEL% model...
ollama pull %MODEL%

echo.
cd /d "%~dp0InvestorAssistant\InvestorAssistant"
set LLM__Endpoint=http://localhost:11434/v1
dotnet run -v quiet -- --model %MODEL%
