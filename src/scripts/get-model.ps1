param([string]$ConfigPath)
$content = Get-Content $ConfigPath -Raw
if ($content -match '"ModelId"\s*:\s*"([^"]+)"') {
    $Matches[1]
}
