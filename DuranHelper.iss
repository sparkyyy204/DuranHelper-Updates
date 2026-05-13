[Setup]
; ВАЖНО: Сгенерируй свой собственный AppId в Inno Setup (Tools -> Generate GUID) и замени этот!
AppId={{1CC9ED7B-9EBB-4A0F-BC15-68633B07AB08}}
AppName=DURAN HELPER
AppVersion=Stable
AppPublisher=Владислав Мякишев
DefaultDirName={pf}\DURAN HELPER
DefaultGroupName=DURAN HELPER
OutputDir=Output
; Изменили название, чтобы сбросить кэш иконок Windows:
OutputBaseFilename=Setup_DuranHelper
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin

; Заставляем ВСЕГДА спрашивать путь установки (даже при обновлении):
UsePreviousAppDir=no
DisableDirPage=no

; === ВИЗУАЛ ===
WizardStyle=modern
; ВАЖНО: Убедись, что файл иконки в папке называется ИМЕННО icon.ico
SetupIconFile=SetupAssets\icoduran.ico
WizardImageFile=SetupAssets\big_image.bmp
WizardSmallImageFile=SetupAssets\small_image.bmp
LicenseFile=SetupAssets\license.txt

; === ЯЗЫК УСТАНОВЩИКА ===
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

; === ГАЛОЧКИ ПРИ УСТАНОВКЕ (Создать ярлык) ===
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Берем ВСЕ файлы и папки из publish_output
Source: "publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Добавлен WorkingDir: "{app}", чтобы программа всегда правильно видела свои файлы
Name: "{autodesktop}\DURAN HELPER"; Filename: "{app}\DURANHELPER.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\DURAN HELPER"; Filename: "{app}\DURANHELPER.exe"; WorkingDir: "{app}"
Name: "{group}\Удалить DURAN HELPER"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\DURANHELPER.exe"; Description: "Запустить DURAN HELPER"; Flags: nowait postinstall skipifsilent