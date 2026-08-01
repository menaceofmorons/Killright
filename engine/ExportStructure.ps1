$exclude = @(
    "target",
    "bin",
    "obj",
    ".idea",
    ".vs"
)

function Show-Tree {
    param(
        [string]$Path,
        [string]$Indent = ""
    )

    Get-ChildItem $Path | Where-Object {
        $exclude -notcontains $_.Name
    } | Sort-Object PSIsContainer, Name -Descending | ForEach-Object {

        Write-Output "$Indent+---$($_.Name)"

        if ($_.PSIsContainer) {
            Show-Tree $_.FullName "$Indent|   "
        }
    }
}

Show-Tree "." | Out-File project-structure.txt