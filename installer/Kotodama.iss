; Установщик Kotodama (Inno Setup 6). Собирается скриптом build-installer.cmd из папки release\Kotodama-portable.
; Установка для текущего пользователя, без прав администратора; данные хранятся в %AppData%\Matsubakira.

#define AppName "Kotodama"
#define AppVersion "4.1.0"
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

[Types]
Name: "full"; Description: "Всё сразу: программа, голоса и распознавание речи"
Name: "compact"; Description: "Только программа (голоса и модель можно скачать потом из программы)"
Name: "custom"; Description: "Выбрать самому"; Flags: iscustom

[Components]
Name: "main"; Description: "Kotodama"; Types: full compact custom; Flags: fixed
; Голоса VOICEVOX: у каждого персонажа свои условия, распространять файлы голосов в установщике нельзя без проверки
; лицензии — поэтому они скачиваются с официального GitHub VOICEVOX при первом запуске программы (с проверкой SHA-256)
Name: "voices"; Description: "Голоса VOICEVOX — японская озвучка без интернета (~330 МБ, скачаются при первом запуске)"; Types: full
Name: "asr"; Description: "Модель распознавания речи Whisper small (190 МБ) — проверка произношения сразу"; Types: full; ExtraDiskSpaceRequired: 190085487

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"
Name: "autostart"; Description: "Запускать вместе с Windows (напоминания и слово дня)"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; Переносная сборка без файла-метки portable (данные — в профиле пользователя)
Source: "..\release\Kotodama-portable\*"; DestDir: "{app}"; Excludes: "portable,ПРОЧТИ.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Kotodama"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить Kotodama"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Kotodama"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Kotodama"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

[Dirs]
Name: "{userappdata}\Matsubakira\models"; Components: asr

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить Kotodama"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Загрузка', 'Скачиваю модель распознавания речи…', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Model: String;
begin
  Result := True;
  if (CurPageID = wpReady) and WizardIsComponentSelected('asr') then
  begin
    Model := ExpandConstant('{userappdata}\Matsubakira\models\ggml-small-q5_1.bin');
    if not FileExists(Model) then
    begin
      DownloadPage.Clear;
      // SHA-256 из таблицы программы (Downloader.Sha256): подменённый файл не установится
      DownloadPage.Add('https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin', 'ggml-small-q5_1.bin', 'ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          ForceDirectories(ExtractFileDir(Model));
          if not FileCopy(ExpandConstant('{tmp}\ggml-small-q5_1.bin'), Model, False) then
            MsgBox('Не удалось сохранить модель. Её можно скачать позже в программе.', mbInformation, MB_OK);
        except
          MsgBox('Модель не скачалась (' + GetExceptionMessage + '). Её можно скачать позже в программе: Настройки → Микрофон.', mbInformation, MB_OK);
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // метка для программы: при первом запуске она сама поставит рекомендуемые голоса VOICEVOX в фоне
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('voices') then
  begin
    ForceDirectories(ExpandConstant('{userappdata}\Matsubakira'));
    SaveStringToFile(ExpandConstant('{userappdata}\Matsubakira\install-voicevox'), 'recommended', False);
  end;
end;
