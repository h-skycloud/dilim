# Starts the Dilim UI development build with its own settings and single instance.
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$dataDirectory = Join-Path $repository 'artifacts/dev-data'
$settingsPath = Join-Path $dataDirectory 'settings.json'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $settingsPath)) {
    # Only seed a fresh development profile. Subsequent launches keep the user's choices.
    @{
        language = 'tr'
        firstRunCompleted = $true
        checkForUpdates = $false
        theme = @{ mode = 'Light'; accent = '#F35B28' }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settingsPath -Encoding utf8
}

$previousDataDirectory = $env:DILIM_DATA_DIR
try {
    # Dilim also uses this to leave Windows startup and Explorer registrations alone.
    $env:DILIM_DATA_DIR = $dataDirectory
    $runArguments = @('run', '--project', (Join-Path $repository 'src/Dilim.App'))
    if ($NoBuild) { $runArguments += '--no-build' }
    & dotnet @runArguments
    if ($LASTEXITCODE -ne 0) { throw "Dilim exited with code $LASTEXITCODE." }
}
finally {
    $env:DILIM_DATA_DIR = $previousDataDirectory
}
