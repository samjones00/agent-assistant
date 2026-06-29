param(
    [string]$Model = "llama3.1:latest",
    [string]$InvestorId = "1",
    [string]$Question = "What is my portfolio overview?",
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$projectDir = "$PSScriptRoot\InvestorAssistant\InvestorAssistant"

Write-Host "=== InvestorAssistant Regression Test ===" -ForegroundColor Cyan
Write-Host "Model:      $Model"
Write-Host "Investor:   $InvestorId"
Write-Host "Question:   $Question"
Write-Host "Timeout:    ${TimeoutSeconds}s"
Write-Host ""

# Check Ollama
try {
    $null = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 3
} catch {
    Write-Host "ERROR: Ollama not running at localhost:11434" -ForegroundColor Red
    exit 1
}

# Check model exists
$models = (Invoke-RestMethod -Uri "http://localhost:11434/api/tags").models.name
if ($models -notcontains $Model) {
    Write-Host "ERROR: Model '$Model' not found. Pull it first: ollama pull $Model" -ForegroundColor Red
    exit 1
}

$env:LLM__Endpoint = "http://localhost:11434/v1"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$input = "${InvestorId}`n${Question}"
$output = $input | dotnet run --project $projectDir -- --model $Model 2>&1 | Out-String
$sw.Stop()

$elapsed = $sw.Elapsed.TotalSeconds
$passed = $elapsed -le $TimeoutSeconds -and $output -notmatch "ERROR|exception|timed out"

Write-Host $output
Write-Host "-----------------------------------" -ForegroundColor DarkGray
Write-Host "Time:       $($elapsed.ToString('F1'))s" -ForegroundColor $(if ($elapsed -le 30) { "Green" } elseif ($elapsed -le 60) { "Yellow" } else { "Red" })
Write-Host "Status:     $(if ($passed) { 'PASS' } else { 'FAIL' })" -ForegroundColor $(if ($passed) { "Green" } else { "Red" })

if (-not $passed) { exit 1 }
