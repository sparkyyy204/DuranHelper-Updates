[Setup]
AppId={{1CC9ED7B-9EBB-4A0F-BC15-68633B07AB08}
AppName=DURAN HELPER
AppVersion=Stable
AppPublisher=Владислав Мякишев
DefaultDirName={pf}\DURAN HELPER
DefaultGroupName=DURAN HELPER
OutputDir=Output
OutputBaseFilename=Setup_DuranHelper
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
UsePreviousAppDir=no
DisableDirPage=no
WizardStyle=modern
SetupIconFile=SetupAssets\icoduran.ico
WizardImageFile=SetupAssets\big_image.bmp
WizardSmallImageFile=SetupAssets\small_image.bmp
LicenseFile=SetupAssets\license.txt

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\DURAN HELPER"; Filename: "{app}\DURANHELPER.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\DURAN HELPER"; Filename: "{app}\DURANHELPER.exe"; WorkingDir: "{app}"
Name: "{group}\Удалить DURAN HELPER"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\DURANHELPER.exe"; Description: "Запустить DURAN HELPER"; Flags: nowait postinstall skipifsilent