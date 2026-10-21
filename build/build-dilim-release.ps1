# Build a clean Dilim contributors installer, portable ZIP, source ZIP and checksums. Never packages local user data.
[CmdletBinding()]
param([string]$Version, [string]$RepositoryUrl = '', [switch]$BundleEngines, [switch]$SkipEngines, [switch]$IncludeModernMenu, [switch]$FastCompression)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskProps = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props'))
if (-not $Version) { $Version = [string]$taskProps.Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') { throw 'Use a semantic version, for example 0.1.0.' }
if ($RepositoryUrl -and $RepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') { throw 'RepositoryUrl must be the actual GitHub repository URL.' }
$taskProduct = [string]$taskProps.Project.PropertyGroup.Product
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/Dilim-release/app'))
$taskSetupStage = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/Dilim-release/setup'))
foreach ($taskPath in @($taskStage, $taskSetupStage)) {
    if (-not $taskPath.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Build directory is outside workspace: $taskPath" }
    if (Test-Path -LiteralPath $taskPath) { Remove-Item -LiteralPath $taskPath -Recurse -Force }
    New-Item -ItemType Directory -Path $taskPath -Force | Out-Null
}
$taskOutput = Join-Path $taskRoot 'Releases'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
function Invoke-DilimChecked([scriptblock]$Action) { & $Action; if ($LASTEXITCODE -ne 0) { throw "Build command failed ($LASTEXITCODE)." } }
Push-Location $taskRoot
try {
    Invoke-DilimChecked { dotnet publish src/Filee.App -c Release -r win-x64 --self-contained true -p:FileeRelease=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" "-p:FileeRepositoryUrl=$RepositoryUrl" -o $taskStage }
    Invoke-DilimChecked { dotnet publish src/Filee.Share -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o (Join-Path $taskStage 'share') }
    Invoke-DilimChecked { dotnet publish src/Filee.Cli -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $taskStage }
    New-Item -ItemType Directory -Path (Join-Path $taskStage 'cli') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $taskStage 'cli/dilim.cmd') -Value '@"%~dp0..\filee-cli.exe" %*' -Encoding ascii
    if ($BundleEngines -and -not $SkipEngines) { & (Join-Path $PSScriptRoot 'fetch-engines.ps1') -Only @('rhwp','ffmpeg','7zip') }
    foreach ($taskEngine in @(if ($BundleEngines) { 'rhwp'; 'ffmpeg'; '7zip' })) {
        $taskSource = Join-Path $taskRoot "engines/$taskEngine"
        if (-not (Test-Path -LiteralPath $taskSource)) { throw "Missing bundled engine $taskEngine. Run without -SkipEngines." }
        New-Item -ItemType Directory -Path (Join-Path $taskStage 'engines') -Force | Out-Null
        Copy-Item -LiteralPath $taskSource -Destination (Join-Path $taskStage 'engines') -Recurse -Force
    }
    & (Join-Path $PSScriptRoot 'fetch-engines.ps1') -Only @('vcruntime') -RuntimeTo @($taskStage)
    if ($IncludeModernMenu) { & (Join-Path $PSScriptRoot 'build-explorer-menu.ps1') -Destination $taskStage -Version $Version }
    foreach ($taskFile in @('LICENSE','THIRD-PARTY-NOTICES.md','README.md','OPEN-SOURCE.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskFile) -Destination $taskStage -Force }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/DILIM-REHBER.html') -Destination (Join-Path $taskStage 'KULLANIM-REHBERI.html') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/DILIM-REHBER.md') -Destination (Join-Path $taskStage 'KULLANIM-REHBERI.md') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/Filee.Engines/Infrastructure/engines.json') -Destination (Join-Path $taskStage 'ENGINE-VERSIONS.json') -Force
    if (Test-Path -LiteralPath (Join-Path $taskStage 'data')) { throw 'User data must never be included in a release.' }
    Invoke-DilimChecked { dotnet publish src/Filee.Setup -c Release -r win-x64 -p:FileeSetupPublish=true "-p:Version=$Version" "-p:FileeRepositoryUrl=$RepositoryUrl" -o $taskSetupStage }
    $taskInstaller = Join-Path $taskOutput "$taskProduct-$Version-win-Setup.exe"
    $taskFast = @(if ($FastCompression) { '--fast' })
    Invoke-DilimChecked { dotnet run --no-cache build/tools/make-setup.cs -- (Join-Path $taskSetupStage 'Filee.Setup.exe') $taskStage $Version $taskInstaller @taskFast }
    # Only the portable ZIP gets the launcher which redirects data beside the executable.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Dilim.vbs') -Destination (Join-Path $taskStage 'Dilim Başlat.vbs') -Force
    $taskZip = Join-Path $taskOutput "$taskProduct-$Version-win-Portable.zip"
    if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($taskStage, $taskZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $taskSourceZip = Join-Path $taskOutput "$taskProduct-$Version-source.zip"
    if (Test-Path -LiteralPath $taskSourceZip) { Remove-Item -LiteralPath $taskSourceZip -Force }
    $taskArchive = [IO.Compression.ZipFile]::Open($taskSourceZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $taskTracked = & git -c core.quotepath=false ls-files --cached --others --exclude-standard
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
        foreach ($taskRelative in ($taskTracked | Sort-Object -Unique)) {
            $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $taskRelative))
            if (-not $taskFull.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid source path.' }
            if (Test-Path -LiteralPath $taskFull -PathType Leaf) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskFull, $taskRelative.Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        }
    } finally { $taskArchive.Dispose() }
    $taskFiles = @($taskInstaller, $taskZip, $taskSourceZip)
    $taskSums = Join-Path $taskOutput "$taskProduct-$Version-SHA256SUMS.txt"
    $taskFiles | ForEach-Object { "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" } | Set-Content -LiteralPath $taskSums -Encoding ascii
    Get-Item -LiteralPath ($taskFiles + $taskSums) | Select-Object Name,Length
} finally { Pop-Location }
