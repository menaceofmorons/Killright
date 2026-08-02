$OutputFile = "KillRight_ProjectStructure.txt"

tree . /F /A | Out-File -FilePath $OutputFile -Encoding utf8

Write-Host ""
Write-Host "Project structure exported to:"
Write-Host $OutputFile
Write-Host ""