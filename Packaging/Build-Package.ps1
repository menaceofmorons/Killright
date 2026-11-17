$ErrorActionPreference = 'Stop'

$version   = '0.5.0'
$codeRoot  = Split-Path $PSScriptRoot -Parent
$engineDir = Join-Path $codeRoot 'killright_engine'
$uiProject = Join-Path $codeRoot 'KillRight\Killright.UI\Killright.UI.csproj'
$out       = Join-Path $PSScriptRoot 'out'
$appDir    = Join-Path $out 'app'
$setupExe  = Join-Path $out "KillRight-v$version-Setup.exe"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $appDir | Out-Null

Push-Location $engineDir
$ErrorActionPreference = 'Continue'
try { cargo build --release; if ($LASTEXITCODE -ne 0) { throw 'cargo build --release failed' } } finally { Pop-Location; $ErrorActionPreference = 'Stop' }
$engineTargetDir = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $engineDir 'target' }
$engineDll = Join-Path $engineTargetDir 'release\killright_engine.dll'

dotnet publish $uiProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SatelliteResourceLanguages=en -p:DebugType=none -p:DebugSymbols=false -p:ReleaseBuild=true -p:Version=$version -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item $engineDll (Join-Path $appDir 'killright_engine.dll') -Force
if ((Get-FileHash $engineDll).Hash -ne (Get-FileHash (Join-Path $appDir 'killright_engine.dll')).Hash) { throw 'engine DLL copy mismatch' }

$makensis = Join-Path $env:LOCALAPPDATA 'tauri\NSIS\makensis.exe'
if (-not (Test-Path $makensis)) { throw "makensis.exe not found at $makensis" }
& $makensis "/DAPP_DIR=$appDir" "/DOUT_FILE=$setupExe" "/DAPP_VERSION=$version" (Join-Path $PSScriptRoot 'KillRight.nsi')
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
Write-Host "Built $setupExe ($([math]::Round((Get-Item $setupExe).Length / 1MB, 1)) MB)"
