; Volitelny Inno Setup. Oficialni balicek je dist\ShiftMon-Setup.exe z installer\Build-Setup.ps1.
; Program si pri startu sam zapise spousteni a polozku pro odebrani v Nastaveni Windows.

#define AppName "ShiftMon"
#define AppVersion "1.1.0"
#define AppExe "ShiftMon.exe"

[Setup]
AppId={{8F5A6B1E-7C24-4E1A-9B3D-6A1F0E4C2D77}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ShiftMon
DefaultDirName={localappdata}\ShiftMon
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ShiftMon-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Spustit ShiftMon"; Flags: nowait postinstall skipifsilent
