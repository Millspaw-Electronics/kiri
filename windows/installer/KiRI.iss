; Inno Setup script for the KiRI Windows app. Build it with build.ps1, which
; publishes the app to ..\publish first and passes the version in.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{6E2B7C1A-4F3D-4B8E-9A51-2D7C0F4E8B19}
AppName=KiRI
AppVersion={#AppVersion}
AppVerName=KiRI {#AppVersion}
AppPublisher=KiRI contributors
AppPublisherURL=https://github.com/leoheck/kiri
AppComments=KiCad Revision Inspector: compare the schematics and layouts of every commit of a KiCad project
DefaultDirName={autopf}\KiRI
DefaultGroupName=KiRI
DisableProgramGroupPage=yes
; Install for the current user without admin rights, or for everyone if the user chooses
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\assets\favicon.ico
UninstallDisplayIcon={app}\KiRI.exe
OutputDir=..\dist
OutputBaseFilename=KiRI-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "Add ""Open in KiRI"" to the right-click menu of folders"; GroupDescription: "Explorer:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KiRI"; Filename: "{app}\KiRI.exe"; Comment: "KiCad Revision Inspector"
Name: "{autodesktop}\KiRI"; Filename: "{app}\KiRI.exe"; Tasks: desktopicon

[Registry]
; "Open in KiRI" on folders and on the background of an open folder
Root: HKA; Subkey: "Software\Classes\Directory\shell\KiRI"; ValueType: string; ValueData: "Open in KiRI"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\KiRI"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\KiRI.exe"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\KiRI\command"; ValueType: string; ValueData: """{app}\KiRI.exe"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\KiRI"; ValueType: string; ValueData: "Open in KiRI"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\KiRI"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\KiRI.exe"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\KiRI\command"; ValueType: string; ValueData: """{app}\KiRI.exe"" ""%V"""; Tasks: contextmenu

[Run]
Filename: "{app}\KiRI.exe"; Description: "{cm:LaunchProgram,KiRI}"; Flags: nowait postinstall skipifsilent
