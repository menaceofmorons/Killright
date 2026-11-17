param(
    [string]$ExePath = (Join-Path $env:LOCALAPPDATA 'Programs\KillRight\Killright.UI.exe'),
    [int]$WindowTimeoutSeconds = 60,
    [int]$ExitTimeoutSeconds = 40,
    [int]$SettleSeconds = 3
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$data = Join-Path $env:LOCALAPPDATA 'KillRight'
$real = Join-Path $env:LOCALAPPDATA 'KillRight.real'
$dbPath = Join-Path $data 'KillRight.db'
$logPath = Join-Path $data 'killright-engine.log'
$backupFolder = Join-Path $data 'Backups'
$processName = 'Killright.UI'
$currentSchemaVersion = 4
$minimumSqliteVersion = [version]'3.37.0'
$workDir = Join-Path $env:TEMP ("KillRight-InstalledStartup-" + [guid]::NewGuid().ToString('N'))

$script:Failures = $null
$script:Notes = $null
$script:Results = New-Object 'System.Collections.Generic.List[object]'

$automation = [System.Windows.Automation.AutomationElement]
$treeChildren = [System.Windows.Automation.TreeScope]::Children
$treeDescendants = [System.Windows.Automation.TreeScope]::Descendants

function Write-Line([string]$Text, [string]$Color = 'Gray') {
    Write-Host $Text -ForegroundColor $Color
}

function Add-Failure([string]$Message) {
    $script:Failures.Add($Message)
}

function Add-Note([string]$Message) {
    $script:Notes.Add($Message)
}

function Test-Equal($Actual, $Expected, [string]$What) {
    if ("$Actual" -ceq "$Expected") {
        Add-Note "$What = $Actual (expected)"
    }
    else {
        Add-Failure "$What is '$Actual', expected '$Expected'."
    }
}

function Invoke-Python([string]$Code, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & py -c $Code @Arguments 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($exitCode -ne 0) {
        throw "Python failed (exit $exitCode): $($output -join ' ')"
    }

    return ($output -join "`n").Trim()
}

function Get-Scalar([string]$Database, [string]$Sql) {
    Invoke-Python 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute(sys.argv[2]).fetchone()[0]); c.close()' @($Database, $Sql)
}

function Invoke-Sql([string]$Database, [string]$Sql) {
    [void](Invoke-Python 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute(sys.argv[2]); c.commit(); c.close()' @($Database, $Sql))
}

function Get-ScalarFromCopy([string]$Source, [string]$Sql) {
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    $probe = Join-Path $workDir ("probe-" + [guid]::NewGuid().ToString('N') + '.db')
    Copy-Item -LiteralPath $Source -Destination $probe
    try {
        return Get-Scalar $probe $Sql
    }
    finally {
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    }
}

function New-DataCopy {
    if (-not (Test-Path -LiteralPath (Join-Path $real 'KillRight.db'))) {
        throw "Refusing to replace '$data': '$real\KillRight.db' is missing."
    }

    if (Test-Path -LiteralPath $data) {
        Remove-Item -LiteralPath $data -Recurse -Force
        if (Test-Path -LiteralPath $data) {
            throw "Could not remove '$data'."
        }
    }

    Copy-Item -LiteralPath $real -Destination $data -Recurse
}

function Get-FolderFingerprint([string]$Path) {
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force)
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    $db = Get-Item -LiteralPath (Join-Path $Path 'KillRight.db')
    return "$($files.Count) files, $bytes bytes, KillRight.db written $($db.LastWriteTimeUtc.Ticks)"
}

function Get-LogLineCount {
    if (-not (Test-Path -LiteralPath $logPath)) { return 0 }
    return @(Get-Content -LiteralPath $logPath).Count
}

function Get-NewLogLines([int]$Before) {
    if (-not (Test-Path -LiteralPath $logPath)) { return @() }
    $all = @(Get-Content -LiteralPath $logPath)
    if ($all.Count -lt $Before) { return $all }
    return @($all | Select-Object -Skip $Before)
}

function Get-Backups {
    if (-not (Test-Path -LiteralPath $backupFolder)) { return @() }
    return @(Get-ChildItem -LiteralPath $backupFolder -Filter 'KillRight-*.db' | Sort-Object Name)
}

function Get-TopWindows([int]$ProcessIdValue) {
    try {
        $condition = New-Object System.Windows.Automation.PropertyCondition($automation::ProcessIdProperty, $ProcessIdValue)
        return @($automation::RootElement.FindAll($treeChildren, $condition))
    }
    catch {
        return @()
    }
}

function Test-IsDialog($Element) {
    try { return $Element.Current.ClassName -eq '#32770' } catch { return $false }
}

function Find-Dialog([int]$ProcessIdValue) {
    foreach ($window in (Get-TopWindows $ProcessIdValue)) {
        if (Test-IsDialog $window) { return $window }
    }
    return $null
}

function Find-MainWindow([int]$ProcessIdValue) {
    foreach ($window in (Get-TopWindows $ProcessIdValue)) {
        try {
            if (-not (Test-IsDialog $window) -and $window.Current.Name -ceq 'KillRight' -and -not $window.Current.IsOffscreen) {
                return $window
            }
        }
        catch {
        }
    }
    return $null
}

function Get-DialogInfo($Dialog) {
    $texts = @($Dialog.FindAll($treeDescendants, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ClassName -eq 'Static' } |
            ForEach-Object { $_.Current.Name } |
            Where-Object { $_ })
    return [pscustomobject]@{
        Title = $Dialog.Current.Name
        Text  = (($texts -join ' ') -replace '\s+', ' ').Trim()
    }
}

function Close-Dialog($Dialog) {
    $ok = $null
    foreach ($element in @($Dialog.FindAll($treeDescendants, [System.Windows.Automation.Condition]::TrueCondition))) {
        if ($element.Current.ClassName -eq 'Button' -and $element.Current.Name -eq 'OK') { $ok = $element }
    }

    try {
        if ($null -eq $ok) { throw 'no OK button' }
        $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    catch {
        $Dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    }
}

function Get-WindowSummary([int]$ProcessIdValue) {
    $names = @(Get-TopWindows $ProcessIdValue | ForEach-Object {
            try { "'$($_.Current.Name)' ($($_.Current.ClassName))" } catch { }
        })
    if ($names.Count -eq 0) { return 'no windows' }
    return ($names -join ', ')
}

function Start-App {
    return Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath) -PassThru
}

function Wait-AppReady($Process, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            return [pscustomobject]@{ State = 'Exited'; Element = $null }
        }

        $dialog = Find-Dialog $Process.Id
        if ($null -ne $dialog) {
            return [pscustomobject]@{ State = 'Dialog'; Element = $dialog }
        }

        $main = Find-MainWindow $Process.Id
        if ($null -ne $main) {
            return [pscustomobject]@{ State = 'Window'; Element = $main }
        }

        Start-Sleep -Milliseconds 300
    }

    return [pscustomobject]@{ State = 'Timeout'; Element = $null }
}

function Wait-ExitWatching($Process, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $sawMainWindow = $false
    while (-not $Process.HasExited -and (Get-Date) -lt $deadline) {
        if ($null -ne (Find-MainWindow $Process.Id)) { $sawMainWindow = $true }
        Start-Sleep -Milliseconds 200
    }

    return [pscustomobject]@{ Exited = $Process.HasExited; SawMainWindow = $sawMainWindow }
}

function Close-App($Process) {
    $Process.Refresh()
    $sent = $false
    try { $sent = $Process.CloseMainWindow() } catch { }

    if (-not $sent) {
        $main = Find-MainWindow $Process.Id
        if ($null -ne $main) {
            $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        }
    }

    return $Process.WaitForExit($ExitTimeoutSeconds * 1000)
}

function Invoke-NormalRun([string]$Label) {
    $process = Start-App
    $ready = Wait-AppReady $process $WindowTimeoutSeconds

    switch ($ready.State) {
        'Window' {
            Start-Sleep -Seconds $SettleSeconds
            if (Close-App $process) {
                Add-Note "$Label`: window opened, closed with CloseMainWindow, process ended (exit code $($process.ExitCode))"
                return $true
            }
            Add-Failure "$Label`: the process did not end within $ExitTimeoutSeconds seconds after the window was closed."
            return $false
        }
        'Dialog' {
            $info = Get-DialogInfo $ready.Element
            Add-Failure "$Label`: an unexpected dialog appeared: '$($info.Title)' - '$($info.Text)'."
            Close-Dialog $ready.Element
            [void]$process.WaitForExit($ExitTimeoutSeconds * 1000)
            return $false
        }
        'Exited' {
            Add-Failure "$Label`: the process ended before a window opened (exit code $($process.ExitCode))."
            return $false
        }
        default {
            Add-Failure "$Label`: no window or dialog within $WindowTimeoutSeconds seconds; $(Get-WindowSummary $process.Id)."
            return $false
        }
    }
}

function Stop-LeftoverApp {
    $running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
    foreach ($process in $running) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(10000)
    }
    return $running.Count
}

function Invoke-Test([int]$Number, [string]$Title, [scriptblock]$Body) {
    $script:Failures = New-Object 'System.Collections.Generic.List[string]'
    $script:Notes = New-Object 'System.Collections.Generic.List[string]'

    Write-Line "Running test $Number..." 'DarkGray'

    try {
        & $Body
    }
    catch {
        Add-Failure "Script error: $($_.Exception.Message)"
    }

    $leftover = Stop-LeftoverApp
    if ($leftover -gt 0) {
        Add-Failure "The app was still running at the end of the test and was stopped."
    }

    $passed = $script:Failures.Count -eq 0
    $script:Results.Add([pscustomobject]@{ Number = $Number; Title = $Title; Passed = $passed })

    if ($passed) {
        Write-Line "[PASS] $Number. $Title" 'Green'
    }
    else {
        Write-Line "[FAIL] $Number. $Title" 'Red'
    }

    foreach ($note in $script:Notes) { Write-Line "       $note" 'DarkGray' }
    foreach ($failure in $script:Failures) { Write-Line "       FAILED: $failure" 'Red' }
}

function Restore-RealData {
    if (-not (Test-Path -LiteralPath (Join-Path $real 'KillRight.db'))) {
        return $false
    }

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            if (Test-Path -LiteralPath $data) {
                Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction Stop
            }
            Rename-Item -LiteralPath $real -NewName 'KillRight' -ErrorAction Stop
            return $true
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    return $false
}

function Write-ManualRestore {
    Write-Line '' 'Red'
    Write-Line 'YOUR REAL DATA FOLDER WAS NOT PUT BACK AUTOMATICALLY.' 'Red'
    Write-Line "Your data is in: $real" 'Red'
    Write-Line 'Close KillRight, then run these two commands in PowerShell:' 'Red'
    Write-Line "Remove-Item -LiteralPath '$data' -Recurse -Force" 'Yellow'
    Write-Line "Rename-Item -LiteralPath '$real' -NewName 'KillRight'" 'Yellow'
}

Write-Line 'KillRight installed-build startup checks' 'Cyan'
Write-Line "Installed app: $ExePath"
Write-Line 'This moves your real data folder aside and puts it back at the end.'

if (Test-Path -LiteralPath $real) {
    Write-Line '' 'Red'
    Write-Line "REFUSED: '$real' already exists." 'Red'
    Write-Line 'A previous run or manual steps may have been interrupted, and your real data may be in that folder. Nothing was changed.' 'Red'
    Write-Line "Check '$real' and '$data', put your data back under the name KillRight, remove the other folder, then run this script again." 'Red'
    exit 2
}

$preflightProblems = New-Object 'System.Collections.Generic.List[string]'

if (-not (Test-Path -LiteralPath $ExePath)) {
    $preflightProblems.Add("The installed app is missing at '$ExePath'. Run the installer, or pass -ExePath.")
}

if (-not (Test-Path -LiteralPath $dbPath)) {
    $preflightProblems.Add("'$dbPath' is missing: there is no real data to test against. Scan a few pilots with any build, close it, and run again.")
}

if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
    $preflightProblems.Add('KillRight is running. Close it and run again.')
}

try {
    $sqliteVersion = [version](Invoke-Python 'import sqlite3; print(sqlite3.sqlite_version)' @())
    if ($sqliteVersion -lt $minimumSqliteVersion) {
        $preflightProblems.Add("Python's SQLite is $sqliteVersion; $minimumSqliteVersion or newer is needed. Install the current Python from python.org.")
    }
}
catch {
    $preflightProblems.Add("Python (the 'py' command) did not run: $($_.Exception.Message)")
}

$installedSettings = Join-Path (Split-Path $ExePath) 'config\settings.json'
if ((Test-Path -LiteralPath $installedSettings) -and ((Get-Content -LiteralPath $installedSettings -Raw) -match '"BackupFolder"\s*:\s*"[^"]+"')) {
    $preflightProblems.Add("A custom BackupFolder is set in '$installedSettings'; backups would not go to the data copy. Remove it and run again.")
}

if ($preflightProblems.Count -gt 0) {
    Write-Line '' 'Red'
    Write-Line 'REFUSED: preflight failed. Nothing was changed.' 'Red'
    foreach ($problem in $preflightProblems) { Write-Line " - $problem" 'Red' }
    exit 2
}

$exe = Get-Item -LiteralPath $ExePath
Write-Line "App version: $($exe.VersionInfo.ProductVersion), file written $($exe.LastWriteTime)"
Write-Line ''

$moved = $false
$exitCode = 0
$restoreOk = $true
$fingerprintBefore = $null
$fingerprintAfter = $null

try {
    Rename-Item -LiteralPath $data -NewName 'KillRight.real'
    $moved = $true
    $fingerprintBefore = Get-FolderFingerprint $real

    New-DataCopy
    $killmails = [int](Get-Scalar $dbPath 'SELECT COUNT(*) FROM zkill_killmails')
    $pilots = [int](Get-Scalar $dbPath 'SELECT COUNT(*) FROM pilot_identity_cache')
    if ($killmails -lt 1 -or $pilots -lt 1) {
        throw "Your real data has $killmails killmails and $pilots pilot rows; both must be above 0 for these checks. Scan a few pilots with any build, close it, and run again."
    }

    Invoke-Test 1 'Corrupted database restored from backup' {
        New-DataCopy

        if (-not (Invoke-NormalRun 'First start')) { return }

        $backups = @(Get-Backups)
        if ($backups.Count -lt 1) {
            Add-Failure 'No backup file exists after the first clean close.'
            return
        }
        Add-Note "backups after first close = $($backups.Count)"

        $expectedPilots = Get-ScalarFromCopy $backups[-1].FullName 'SELECT COUNT(*) FROM pilot_identity_cache'
        Add-Note "pilot rows in the newest backup ($($backups[-1].Name)) = $expectedPilots"

        $stream = [IO.File]::Open($dbPath, 'Open', 'Write')
        try { $stream.Write((New-Object byte[] 16), 0, 16) } finally { $stream.Close() }

        $logBefore = Get-LogLineCount
        if (-not (Invoke-NormalRun 'Start on damaged database')) { return }

        $setAside = @(Get-ChildItem -LiteralPath $data -Filter 'KillRight.db.corrupt-*')
        Test-Equal $setAside.Count 1 'set-aside files'

        $newLines = Get-NewLogLines $logBefore
        if (@($newLines | Where-Object { $_.Contains('corrupt at startup') }).Count -ge 1) { Add-Note 'log line "corrupt at startup" present' } else { Add-Failure 'Log has no line containing "corrupt at startup".' }
        if (@($newLines | Where-Object { $_.Contains('Restored the database from the latest backup') }).Count -ge 1) { Add-Note 'log line "Restored the database from the latest backup" present' } else { Add-Failure 'Log has no line containing "Restored the database from the latest backup".' }

        Test-Equal (Get-Scalar $dbPath 'SELECT COUNT(*) FROM pilot_identity_cache') $expectedPilots 'pilot rows after restore'
    }

    Invoke-Test 2 'Newer locked database refused with a message' {
        New-DataCopy

        $expectedKillmails = Get-Scalar $dbPath 'SELECT COUNT(*) FROM zkill_killmails'
        Invoke-Sql $dbPath 'UPDATE schema_metadata SET schema_version = 99, alpha_lock = 1'

        $expectedText = "This database's schema version (99) is newer than this build supports ($currentSchemaVersion). Install a newer version of KillRight to continue."
        $logBefore = Get-LogLineCount

        $process = Start-App
        $ready = Wait-AppReady $process $WindowTimeoutSeconds

        $sawMainWindow = $false
        switch ($ready.State) {
            'Dialog' {
                $info = Get-DialogInfo $ready.Element
                Test-Equal $info.Title 'KillRight - Schema Version' 'dialog title'
                Test-Equal $info.Text $expectedText 'dialog text'
                if ($null -ne (Find-MainWindow $process.Id)) { $sawMainWindow = $true }
                Close-Dialog $ready.Element
            }
            'Window' {
                Add-Failure 'The main window opened instead of the refusal dialog.'
                [void](Close-App $process)
            }
            'Exited' { Add-Failure "The process ended without a dialog (exit code $($process.ExitCode))." }
            default { Add-Failure "No dialog within $WindowTimeoutSeconds seconds; $(Get-WindowSummary $process.Id)." }
        }

        if ($ready.State -eq 'Dialog') {
            $exit = Wait-ExitWatching $process $ExitTimeoutSeconds
            if ($exit.SawMainWindow) { $sawMainWindow = $true }
            if ($exit.Exited) { Add-Note 'process ended after the dialog was closed' } else { Add-Failure "The process did not end within $ExitTimeoutSeconds seconds after the dialog was closed." }
            if ($sawMainWindow) { Add-Failure 'A KillRight main window appeared.' } else { Add-Note 'no main window appeared' }
        }

        Test-Equal (Get-Scalar $dbPath 'SELECT schema_version FROM schema_metadata') 99 'schema_version after'
        Test-Equal (Get-Scalar $dbPath 'SELECT alpha_lock FROM schema_metadata') 1 'alpha_lock after'
        Test-Equal (Get-Scalar $dbPath 'SELECT COUNT(*) FROM zkill_killmails') $expectedKillmails 'killmail rows after'

        $newLines = Get-NewLogLines $logBefore
        if (@($newLines | Where-Object { $_.Contains("Startup refused: $expectedText") }).Count -ge 1) { Add-Note 'log line "Startup refused: ..." present with the dialog text' } else { Add-Failure 'Log has no "Startup refused:" line with the dialog text.' }
    }

    Invoke-Test 3 'Unlocked older database rebuilt, then locked by the release build' {
        New-DataCopy

        $expectedPilots = Get-Scalar $dbPath 'SELECT COUNT(*) FROM pilot_identity_cache'
        $killmailsBefore = [int](Get-Scalar $dbPath 'SELECT COUNT(*) FROM zkill_killmails')
        Add-Note "killmail rows before = $killmailsBefore"
        if ($killmailsBefore -lt 1) {
            Add-Failure 'The copy has no killmail rows, so the rebuild cannot be observed.'
            return
        }
        Invoke-Sql $dbPath 'UPDATE schema_metadata SET schema_version = 3, alpha_lock = 0'

        if (-not (Invoke-NormalRun 'Start')) { return }

        Test-Equal (Get-Scalar $dbPath 'SELECT schema_version FROM schema_metadata') $currentSchemaVersion 'schema_version after'
        Test-Equal (Get-Scalar $dbPath 'SELECT alpha_lock FROM schema_metadata') 1 'alpha_lock after'
        Test-Equal (Get-Scalar $dbPath 'SELECT COUNT(*) FROM zkill_killmails') 0 'killmail rows after'
        Test-Equal (Get-Scalar $dbPath 'SELECT COUNT(*) FROM pilot_identity_cache') $expectedPilots 'pilot rows after'
    }

    Invoke-Test 4 'Clean exit writes a backup after a change to a non-killmail table' {
        New-DataCopy

        if (-not (Invoke-NormalRun 'First start')) { return }

        $first = @(Get-Backups)
        if ($first.Count -lt 1) {
            Add-Failure 'No backup file exists after the first clean close.'
            return
        }
        $firstName = $first[-1].Name
        Add-Note "newest backup after first close = $firstName"

        Invoke-Sql $dbPath "INSERT OR REPLACE INTO esi_entity_name_cache (entity_id, entity_type, name) VALUES (2000000001, 'character', 'Integration Card')"
        Start-Sleep -Seconds 2

        if (-not (Invoke-NormalRun 'Second start')) { return }

        $second = @(Get-Backups)
        $secondFile = $second[-1]
        Add-Note "newest backup after second close = $($secondFile.Name)"
        if ($secondFile.Name -ceq $firstName) { Add-Failure 'No new backup was written at the second clean close.' }

        Test-Equal (Get-ScalarFromCopy $secondFile.FullName 'SELECT COUNT(*) FROM esi_entity_name_cache WHERE entity_id = 2000000001') 1 'added row in the newest backup'
    }
}
catch {
    Write-Line ''
    Write-Line "STOPPED: $($_.Exception.Message)" 'Red'
    $exitCode = 2
}
finally {
    [void](Stop-LeftoverApp)

    if ($moved) {
        $restoreOk = Restore-RealData
        if ($restoreOk) {
            $fingerprintAfter = Get-FolderFingerprint $data
        }
    }

    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Line ''
    if ($moved -and $restoreOk) {
        Write-Line "Real data folder restored at $data" 'Green'
        if ($fingerprintBefore -ceq $fingerprintAfter) {
            Write-Line "Real data unchanged ($fingerprintAfter)" 'Green'
        }
        else {
            Write-Line "WARNING: the restored folder differs from before the run. Before: $fingerprintBefore. After: $fingerprintAfter." 'Yellow'
        }
    }
    elseif ($moved) {
        Write-ManualRestore
        $exitCode = 2
    }
}

if ($script:Results.Count -gt 0) {
    $passedCount = @($script:Results | Where-Object { $_.Passed }).Count
    Write-Line ''
    Write-Line "Result: $passedCount of $($script:Results.Count) tests passed" $(if ($passedCount -eq $script:Results.Count -and $exitCode -eq 0) { 'Green' } else { 'Red' })
    if ($passedCount -ne $script:Results.Count -and $exitCode -eq 0) { $exitCode = 1 }
}

exit $exitCode
