# Build a self-contained Windows delivery. No SDK or system registration is needed to run the result.
param([switch]$SkipEngines)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repository 'artifacts/Dilim-Windows'
if (-not $SkipEngines) {
    & (Join-Path $PSScriptRoot 'fetch-engines.ps1') -Only @('ffmpeg', '7zip')
}
& dotnet publish (Join-Path $repository 'src/Filee.App') -c Release -r win-x64 --self-contained true -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
& dotnet publish (Join-Path $repository 'src/Filee.Share') -c Release -r win-x64 --self-contained true -o (Join-Path $destination 'share')
if ($LASTEXITCODE -ne 0) { throw 'Share helper publish failed.' }
foreach ($engine in @('ffmpeg', '7zip')) {
    $source = Join-Path $repository "engines/$engine"
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing engine: $engine" }
    New-Item -ItemType Directory -Path (Join-Path $destination 'engines') -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'engines') -Recurse -Force
}
$data = Join-Path $destination 'data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$settings = Join-Path $data 'settings.json'
if (-not (Test-Path -LiteralPath $settings)) {
    @{ language = 'tr'; firstRunCompleted = $true; checkForUpdates = $false; closeAction = 'Tray';
       theme = @{ mode = 'Light'; accent = '#F35B28' } } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settings -Encoding utf8
}
Copy-Item -LiteralPath (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $repository 'docs/DILIM-REHBER.md') -Destination (Join-Path $destination 'KULLANIM-REHBERI.md') -Force
Copy-Item -LiteralPath (Join-Path $repository 'docs/DILIM-REHBER.html') -Destination (Join-Path $destination 'KULLANIM-REHBERI.html') -Force
Copy-Item -LiteralPath (Join-Path $repository 'docs/DILIM-FILECONVERTER-KARSILASTIRMA.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Dilim.vbs') -Destination (Join-Path $destination 'Dilim Başlat.vbs') -Force
Write-Output "Ready: $destination"
