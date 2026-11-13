; Установщик Kotodama (Inno Setup 6). Собирается скриптом build-installer.cmd из папки release\Kotodama-portable.
; Установка для текущего пользователя, без прав администратора; данные хранятся в %AppData%\Matsubakira.

#define AppName "Kotodama"
#define AppVersion "2.1.0"
#define AppExe "Kotodama.exe"

[Setup]
AppId={{6E2B4C1A-3F7D-4B8E-9A51-7C0D2E4F6A13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Kotodama
AppPublisherURL=https://github.com/disconnected-cloud/kotodama-japanese
AppSupportURL=https://github.com/disconnected-cloud/kotodama-japanese/issues
AppUpdatesURL=https://github.com/disconnected-cloud/kotodama-japanese/releases
DefaultDirName={localappdata}\Programs\Kotodama
DefaultGroupName=Kotodama
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release
OutputBaseFilename=Kotodama-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"

[Files]
; Переносная сборка без файла-метки portable (данные — в профиле пользователя)
Source: "..\release\Kotodama-portable\*"; DestDir: "{app}"; Excludes: "portable,ПРОЧТИ.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Kotodama"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить Kotodama"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Kotodama"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить Kotodama"; Flags: nowait postinstall skipifsilent
