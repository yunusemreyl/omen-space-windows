[Setup]
AppName=OmenSpace
AppVersion=1.0.0
AppPublisher=OmenSpace Open Source Project
AppPublisherURL=https://github.com/yeyil
DefaultDirName={autopf}\OmenSpace
DefaultGroupName=OmenSpace
UninstallDisplayIcon={app}\OmenSpace.App.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=OmenSpace_Setup
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
DisableWelcomePage=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "OmenSpace.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "OmenSpace.App\icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "pawnio\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion
; Also ensure Worker exe is included if it was copied there, the publish dir contains everything needed if project references are correct.

[Icons]
Name: "{group}\OmenSpace"; Filename: "{app}\OmenSpace.App.exe"; IconFilename: "{app}\icons\omen-space.ico"
Name: "{group}\{cm:UninstallProgram,OmenSpace}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OmenSpace"; Filename: "{app}\OmenSpace.App.exe"; IconFilename: "{app}\icons\omen-space.ico"; Tasks: desktopicon

[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; Description: "Installing PawnIO..."; Flags: waituntilterminated
Filename: "{app}\OmenSpace.App.exe"; Description: "{cm:LaunchProgram,OmenSpace}"; Flags: nowait postinstall skipifsilent shellexec
