$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\ShiftMon\ShiftMon.csproj"
$dist = Join-Path $root "dist"

Write-Host "Publikuji ShiftMon..."
dotnet publish $project -c Release -o $dist --nologo

$exe = Join-Path $dist "ShiftMon.exe"
if (-not (Test-Path $exe)) { throw "Publikace nevytvořila $exe" }

Write-Host "Instaluji do profilu uživatele..."
& $exe --install
Write-Host "Hotovo. ShiftMon je v Nabídce Start a spouští se po přihlášení."
