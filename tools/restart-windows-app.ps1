# Closes Mon Album PC (Windows app), rebuilds it from the sources and starts it again.
# Used by the "Relancer Mon Album PC" task of VS Code. Pass -NoBuild to only restart.
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\windows\src\MonAlbumPc.App'
$exe = Join-Path $project 'bin\Debug\net10.0-windows\MonAlbumPc.exe'

$running = Get-Process MonAlbumPc -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Fermeture de Mon Album PC…"
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (-not $NoBuild) {
    Write-Host "Compilation…"
    dotnet build $project --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué : l'app n'est pas relancée." }
}

Write-Host "Démarrage…"
Start-Process $exe
Write-Host "Mon Album PC est relancé."
