# Exercises the real Setup payload without writing the actual Windows registry or shortcuts.
[CmdletBinding()]
param([string]$Installer)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Installer) { $Installer = (Get-ChildItem (Join-Path $taskRoot 'Releases/*-win-Setup.exe') | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
if (-not $Installer -or -not (Test-Path -LiteralPath $Installer)) { throw 'Build the installer first.' }
$taskSandbox = Join-Path $taskRoot ('artifacts/installer-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskSandbox -Force | Out-Null
$taskApp = Join-Path $taskSandbox 'LocalAppData/Programs/Dilim'
$taskData = Join-Path $taskSandbox 'AppData/Dilim'
New-Item -ItemType Directory -Path $taskData -Force | Out-Null
Set-Content -LiteralPath (Join-Path $taskData 'keep.txt') -Value 'user settings stand-in'
# A separate upstream install must remain untouched.
$taskUpstream = Join-Path $taskSandbox 'LocalAppData/Programs/Filee'
New-Item -ItemType Directory -Path $taskUpstream -Force | Out-Null
Set-Content -LiteralPath (Join-Path $taskUpstream 'Filee.exe') -Value 'upstream stand-in'
function Invoke-DilimSetup([string]$Program, [string[]]$Extra) {
    $taskArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NOLAUNCH','/NORESTART',('/SANDBOX="' + $taskSandbox + '"')) + $Extra
    $taskProcess = Start-Process -FilePath $Program -ArgumentList $taskArgs -PassThru -Wait -WindowStyle Hidden
    if ($taskProcess.ExitCode -ne 0) { throw "Setup failed: $($taskProcess.ExitCode). Logs: $taskSandbox" }
}
Invoke-DilimSetup $Installer @('/TASKS=contextmenu,cli,desktopicon')
foreach ($taskFile in @('Filee.exe','uninstall.exe','uninstall.json','cli/dilim.cmd','ENGINE-VERSIONS.json','LICENSE','KULLANIM-REHBERI.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskApp $taskFile))) { throw "Missing installed file: $taskFile" }
}
$taskRegistry = Get-Content -LiteralPath (Join-Path $taskSandbox 'registry.json') -Raw
if ($taskRegistry -notmatch 'Dilim' -or $taskRegistry -notmatch '70647F27-43AE-40CB-8B5B-647F493B0678') { throw 'Product identity missing from sandbox registry.' }
Set-Content -LiteralPath (Join-Path $taskApp 'foreign.txt') -Value 'not owned by installer'
Invoke-DilimSetup $Installer @()
if (-not (Test-Path -LiteralPath (Join-Path $taskApp 'foreign.txt'))) { throw 'Update removed a foreign file.' }
Invoke-DilimSetup (Join-Path $taskApp 'uninstall.exe') @('/UNINSTALL','/FIRSTPHASE=0')
if (Test-Path -LiteralPath (Join-Path $taskApp 'Filee.exe')) { throw 'Uninstall left the application executable.' }
foreach ($taskKept in @((Join-Path $taskApp 'foreign.txt'),(Join-Path $taskData 'keep.txt'),(Join-Path $taskUpstream 'Filee.exe'))) {
    if (-not (Test-Path -LiteralPath $taskKept)) { throw "Uninstall removed an unrelated/preserved file: $taskKept" }
}
$taskResult = [ordered]@{ installer = [IO.Path]::GetFileName($Installer); sha256 = (Get-FileHash -LiteralPath $Installer).Hash; install = 'passed'; update = 'passed'; uninstall = 'passed'; dataPreserved = $true; upstreamPreserved = $true; sandbox = $taskSandbox }
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/dilim-installer-validation.json') -Encoding utf8
$taskResult
