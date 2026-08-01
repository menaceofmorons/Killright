$ErrorActionPreference = "Stop"

$root = "C:\Users\TomMulledy\OneDrive\Documents\Code\PilotIntel\Code"
$oldEngineRoot = Join-Path $root "engine\killright_engine"
$newEngineRoot = Join-Path $root "killright_engine"
$oldEngineContainer = Join-Path $root "engine"

Set-Location $root

function Assert-ProcessClosed {
    param([string]$Name)

    $process = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        throw "Close $Name before running this guide. The process is still running."
    }
}

function Assert-DirectoryExists {
    param([string]$Path)

    if (!(Test-Path $Path)) {
        throw "Expected directory not found: $Path"
    }
}

function Assert-FileExists {
    param([string]$Path)

    if (!(Test-Path $Path)) {
        throw "Expected file not found: $Path"
    }
}

Assert-ProcessClosed -Name "rider"
Assert-ProcessClosed -Name "rustrover"

if (Test-Path $newEngineRoot) {
    Write-Host "Already exists: $newEngineRoot"
} else {
    Assert-DirectoryExists -Path $oldEngineRoot
    Move-Item -Path $oldEngineRoot -Destination $newEngineRoot
}

Assert-DirectoryExists -Path $newEngineRoot
Assert-FileExists -Path (Join-Path $newEngineRoot "Cargo.toml")
Assert-FileExists -Path (Join-Path $newEngineRoot "Cargo.lock")
Assert-FileExists -Path (Join-Path $newEngineRoot "src\lib.rs")
Assert-FileExists -Path (Join-Path $newEngineRoot "src\main.rs")
Assert-FileExists -Path (Join-Path $newEngineRoot "src\kr_engine.rs")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\config")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\contracts")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\fleet_analysis")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\group_analysis")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\recent_style")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\repositories")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\shared")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\ship_library")
Assert-DirectoryExists -Path (Join-Path $newEngineRoot "src\threat_analysis")

if (Test-Path (Join-Path $newEngineRoot "src\pintel_engine")) {
    throw "Unexpected old nested module folder still exists under the flattened Rust engine: src\pintel_engine"
}

if (Test-Path $oldEngineRoot) {
    throw "Old Rust engine path still exists after move: $oldEngineRoot"
}

if (Test-Path $oldEngineContainer) {
    $remainingItems = Get-ChildItem -Path $oldEngineContainer -Force
    if ($remainingItems.Count -eq 0) {
        Remove-Item -Path $oldEngineContainer -Force
    } else {
        Write-Host "The old engine container was not removed because it still contains the following items:"
        $remainingItems | ForEach-Object { Write-Host $_.FullName }
        throw "Remove or relocate the remaining items from the old engine container, then rerun this script."
    }
}

Set-Location $newEngineRoot
cargo fmt --check
cargo build
Set-Location $root

Write-Host "18.20.01 Rust repository flattening completed."