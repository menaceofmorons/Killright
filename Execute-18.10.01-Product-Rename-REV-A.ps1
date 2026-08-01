$ErrorActionPreference = "Stop"

$root = "C:\Users\TomMulledy\OneDrive\Documents\Code\PilotIntel\Code"
Set-Location $root

function Assert-ProcessClosed {
    param([string]$Name)

    $process = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        throw "Close $Name before running this guide. The process is still running."
    }
}

function Rename-PathIfNeeded {
    param(
        [string]$OldPath,
        [string]$NewName
    )

    $parent = Split-Path -Parent $OldPath
    $newPath = Join-Path $parent $NewName

    if (Test-Path $newPath) {
        Write-Host "Already exists: $newPath"
        return
    }

    if (!(Test-Path $OldPath)) {
        throw "Expected path not found: $OldPath"
    }

    Rename-Item -Path $OldPath -NewName $NewName
}

function Replace-InFile {
    param(
        [string]$Path,
        [string]$OldValue,
        [string]$NewValue
    )

    if (!(Test-Path $Path)) {
        throw "Expected file not found: $Path"
    }

    $content = Get-Content -Path $Path -Raw
    $updated = $content.Replace($OldValue, $NewValue)
    if ($updated -ne $content) {
        Set-Content -Path $Path -Value $updated -NoNewline
    }
}

function Replace-InFiles {
    param(
        [string]$Path,
        [string[]]$Include,
        [string]$OldValue,
        [string]$NewValue
    )

    Get-ChildItem -Path $Path -Recurse -File -Include $Include |
        Where-Object { $_.FullName -notmatch "\\(bin|obj|\.git|\.idea)\\" } |
        ForEach-Object {
            Replace-InFile -Path $_.FullName -OldValue $OldValue -NewValue $NewValue
        }
}

Assert-ProcessClosed -Name "rider"
Assert-ProcessClosed -Name "rustrover"

Rename-PathIfNeeded -OldPath ".\src" -NewName "KillRight"
Rename-PathIfNeeded -OldPath ".\engine\PIntelEngine" -NewName "killright_engine"

Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.Core" -NewName "Killright.Core"
Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.Integration" -NewName "Killright.Integration"
Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.Shared" -NewName "Killright.Shared"
Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.Storage" -NewName "Killright.Storage"
Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.UI" -NewName "Killright.UI"

Rename-PathIfNeeded -OldPath ".\KillRight\PilotIntel.sln" -NewName "KillRight.sln"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.Core\PilotIntel.Core.csproj" -NewName "Killright.Core.csproj"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.Integration\PilotIntel.Integration.csproj" -NewName "Killright.Integration.csproj"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.Shared\PilotIntel.Shared.csproj" -NewName "Killright.Shared.csproj"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.Storage\PilotIntel.Storage.csproj" -NewName "Killright.Storage.csproj"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.UI\PilotIntel.UI.csproj" -NewName "Killright.UI.csproj"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.UI\Analysis\IPIntelEngineRuntime.cs" -NewName "IKillrightEngineRuntime.cs"
Rename-PathIfNeeded -OldPath ".\KillRight\Killright.UI\Analysis\PIntelEngineRuntime.cs" -NewName "KillrightEngineRuntime.cs"

$slnPath = ".\KillRight\KillRight.sln"
Replace-InFile -Path $slnPath -OldValue "PilotIntel.Core" -NewValue "Killright.Core"
Replace-InFile -Path $slnPath -OldValue "PilotIntel.Integration" -NewValue "Killright.Integration"
Replace-InFile -Path $slnPath -OldValue "PilotIntel.Shared" -NewValue "Killright.Shared"
Replace-InFile -Path $slnPath -OldValue "PilotIntel.Storage" -NewValue "Killright.Storage"
Replace-InFile -Path $slnPath -OldValue "PilotIntel.UI" -NewValue "Killright.UI"

Replace-InFiles -Path ".\KillRight" -Include @("*.csproj") -OldValue "PilotIntel.Core" -NewValue "Killright.Core"
Replace-InFiles -Path ".\KillRight" -Include @("*.csproj") -OldValue "PilotIntel.Integration" -NewValue "Killright.Integration"
Replace-InFiles -Path ".\KillRight" -Include @("*.csproj") -OldValue "PilotIntel.Shared" -NewValue "Killright.Shared"
Replace-InFiles -Path ".\KillRight" -Include @("*.csproj") -OldValue "PilotIntel.Storage" -NewValue "Killright.Storage"
Replace-InFiles -Path ".\KillRight" -Include @("*.csproj") -OldValue "PilotIntel.UI" -NewValue "Killright.UI"

Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.Core" -NewValue "Killright.Core"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.Integration" -NewValue "Killright.Integration"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.Shared" -NewValue "Killright.Shared"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.Storage" -NewValue "Killright.Storage"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.UI" -NewValue "Killright.UI"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "IPIntelEngineRuntime" -NewValue "IKillrightEngineRuntime"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PIntelEngineRuntime" -NewValue "KillrightEngineRuntime"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "pintelengine.dll" -NewValue "killright_engine.dll"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel/0.1" -NewValue "KillRight/0.1"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel/1.0" -NewValue "KillRight/1.0"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel.duckdb" -NewValue "KillRight.duckdb"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue '"PilotIntel"' -NewValue '"KillRight"'
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel Diagnostics" -NewValue "KillRight Diagnostics"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel Developer Diagnostics" -NewValue "KillRight Developer Diagnostics"
Replace-InFiles -Path ".\KillRight" -Include @("*.cs", "*.xaml") -OldValue "PilotIntel" -NewValue "KillRight"

$cargoToml = @'
[package]
name = "killright_engine"
version = "0.1.0"
edition = "2021"

[lib]
name = "killright_engine"
crate-type = ["cdylib"]

[[bin]]
name = "killright_engine_cli"
path = "src/main.rs"

[dependencies]
serde = { version = "1", features = ["derive"] }
serde_json = "1"
duckdb = { version = "1", features = ["bundled"] }
chrono = { version = "0.4", features = ["clock"] }
'@

Set-Content -Path ".\engine\killright_engine\Cargo.toml" -Value $cargoToml -NoNewline

if (Test-Path ".\engine\killright_engine\Cargo.toml.txt") {
    Remove-Item ".\engine\killright_engine\Cargo.toml.txt"
}

if (Test-Path ".\KillRight\Killright.Core\Class1.cs") {
    Remove-Item ".\KillRight\Killright.Core\Class1.cs"
}

Set-Location ".\engine\killright_engine"
cargo generate-lockfile
Set-Location $root

Write-Host "18.10.01 Product Rename REV-A completed."