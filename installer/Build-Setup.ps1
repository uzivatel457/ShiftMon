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

$iexpress = Join-Path $env:SystemRoot "System32\iexpress.exe"
$sed = Join-Path $root "dist\ShiftMon-Setup.sed"
$sedText = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Nainstalovat ShiftMon pro tohoto uzivatele? Spravce neni potreba. Program pujde odebrat v Nastaveni Windows, v casti Aplikace.
DisplayLicense=
FinishMessage=ShiftMon je nainstalovany. Spousti se po prihlaseni a nova verze se nabidne z GitHubu.
TargetName=$setup
FriendlyName=ShiftMon Setup
AppLaunched=ShiftMon.exe
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="ShiftMon.exe"
[SourceFiles]
SourceFiles0=$publish
[SourceFiles0]
%FILE0%=
"@
Set-Content -Path $sed -Value $sedText -Encoding ASCII

if (Test-Path $setup) { Remove-Item $setup -Force }
& $iexpress /N /Q $sed
if (-not (Test-Path $setup)) {
    Write-Host "IExpress nevytvoril balicek, pouziji samostatny soubor."
    Copy-Item $app $setup -Force
}

Write-Host "Balicek: $setup"
Get-Item $setup | Select-Object FullName, Length
