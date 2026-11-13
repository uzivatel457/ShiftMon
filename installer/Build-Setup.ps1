$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\ShiftMon\ShiftMon.csproj"
$publish = Join-Path $root "dist\publish"
$setup = Join-Path $root "dist\ShiftMon-Setup.exe"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $root "dist") | Out-Null

Write-Host "Publikuji samostatny ShiftMon pro Windows x64..."
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $publish --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$app = Join-Path $publish "ShiftMon.exe"
if (-not (Test-Path $app)) { throw "Publikace nevytvorila $app" }
Copy-Item $app $setup -Force

$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
if ([string]::IsNullOrWhiteSpace($info.ProductVersion)) { throw "Balicek nema verzi produktu." }
Write-Host "Balicek: $setup ($($info.ProductVersion), $((Get-Item $setup).Length) B)"
