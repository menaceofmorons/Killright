$ErrorActionPreference = 'Stop'

$version   = '0.4.1'
$codeRoot  = Split-Path $PSScriptRoot -Parent
$engineDir = Join-Path $codeRoot 'killright_engine'
$uiProject = Join-Path $codeRoot 'KillRight\Killright.UI\Killright.UI.csproj'
$out       = Join-Path $PSScriptRoot 'out'
$appDir    = Join-Path $out 'app'
$setupExe  = Join-Path $out "KillRight-Alpha-v$version-Setup.exe"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $appDir | Out-Null

# 1. Release engine
Push-Location $engineDir
$ErrorActionPreference = 'Continue'
try { cargo build --release; if ($LASTEXITCODE -ne 0) { throw 'cargo build --release failed' } } finally { Pop-Location; $ErrorActionPreference = 'Stop' }
$engineDll = Join-Path $engineDir 'target\release\killright_engine.dll'

# 2. Release UI, framework-dependent single file (requires the .NET 9 Desktop Runtime), English resources only, no symbols, ALPHA_RELEASE defined
dotnet publish $uiProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SatelliteResourceLanguages=en -p:DebugType=none -p:DebugSymbols=false -p:AlphaRelease=true -p:Version=$version -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# 3. Killright.UI.csproj links the debug engine DLL; replace it with the Release one
Copy-Item $engineDll (Join-Path $appDir 'killright_engine.dll') -Force
if ((Get-FileHash $engineDll).Hash -ne (Get-FileHash (Join-Path $appDir 'killright_engine.dll')).Hash) { throw 'engine DLL copy mismatch' }

# 4. NSIS installer (compiler cached by Tauri builds)
$makensis = Join-Path $env:LOCALAPPDATA 'tauri\NSIS\makensis.exe'
if (-not (Test-Path $makensis)) { throw "makensis.exe not found at $makensis" }
& $makensis "/DAPP_DIR=$appDir" "/DOUT_FILE=$setupExe" "/DAPP_VERSION=$version" (Join-Path $PSScriptRoot 'KillRight.nsi')
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
Write-Host "Built $setupExe ($([math]::Round((Get-Item $setupExe).Length / 1MB, 1)) MB)"
