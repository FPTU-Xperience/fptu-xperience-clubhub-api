param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Prompt,
    
    # Default model linked to user's Google Pro account
    [string]$Model = "gemini-3.8-flash-high",

    # Pass -NewSession switch if you explicitly want to wipe context and start a brand new conversation
    [switch]$NewSession
)

$ErrorActionPreference = "Stop"

Write-Host "=== Antigravity CLI (Persistent Context) ===" -ForegroundColor Cyan
Write-Host "Model:   $Model" -ForegroundColor Green
Write-Host "Task:    $Prompt" -ForegroundColor Yellow

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$agyCommand = Get-Command "agy.exe" -ErrorAction SilentlyContinue
if (-not $agyCommand) {
    $agyCommand = Get-Command "agy" -ErrorAction Stop
}

Write-Host "Project: $projectRoot" -ForegroundColor Green

$agyArgs = @(
    "--prompt=$Prompt",
    "--model", "$Model",
    "--mode", "accept-edits",
    "--dangerously-skip-permissions",
    "--print-timeout", "2m0s",
    "--output-format", "text"
)

# If not explicitly requesting a new session, continue the ongoing conversation context
if (-not $NewSession) {
    Write-Host "Context: Continuing active session (--continue)" -ForegroundColor Gray
    $agyArgs = @("-c") + $agyArgs
} else {
    Write-Host "Context: Starting fresh session" -ForegroundColor Gray
    $agyArgs = @("--add-dir", "$projectRoot") + $agyArgs
}

Push-Location -LiteralPath $projectRoot
try {
    & $agyCommand.Source @agyArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Antigravity CLI exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
