; Inno Setup Script for Repliq Quick (Lightweight Standalone Toolbar)
; Developed by RetsardCo 2026

#define MyAppName "Repliq Quick"
#define MyAppVersion "0.1.0-alpha"
#define MyAppPublisher "RetsardCo"
#define MyAppURL "https://github.com/RetsardCo/Repliq"
#define MyAppExeName "Repliq.Quick.exe"

[Setup]
AppId={{5792F53E-74B0-4A04-9E11-20E15AE0E1B8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\RepliqQuick
DefaultGroupName=Repliq
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\releases\v0.1.0-alpha
OutputBaseFilename=RepliqQuick-v0.1.0-alpha-Lightweight-Installer
SetupIconFile=..\assets\logo.ico
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\publish\RepliqQuick\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
