@echo off
setlocal

echo EquiTie Investor Assistant - Hosted (default LLM)
echo =================================================
echo.

cd /d "%~dp0src\InvestorAssistant"

echo Building...
dotnet build -v q --nologo >nul 2>&1
if errorlevel 1 (
    echo Build failed. Run dotnet build manually for details.
    dotnet build
    exit /b 1
)

dotnet run