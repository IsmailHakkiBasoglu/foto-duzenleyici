; Foto Düzenleyici kurulumu (Inno Setup 6). publish.ps1 derler:
;   ISCC /DAppVersion=0.6.0 /DSourceDir=..\dist\setup /DOutputDir=..\dist FotoDuzenleyici.iss
; Kurulum uygulamaya internetten hiçbir şey indirmez; ayar klasörü yalnız kaldırmada "Evet" denirse silinir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\setup"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#define AppGuid "8F3C2A51-6B7E-4D2A-9C41-5E0D7A3B9F12"
#define AppName "Foto Düzenleyici"
#define AppExe "FotoDuzenleyici.exe"
#define RunningMutex "FotoDuzenleyici.Calisiyor"

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=İsmail Hakkı Başoğlu
AppPublisherURL=https://github.com/IsmailHakkiBasoglu/foto-duzenleyici
AppSupportURL=https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/issues
AppUpdatesURL=https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Varsayılan: yalnız bu kullanıcı, yönetici izni yok. Kurulumun başında "tüm kullanıcılar" seçilebilir.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=FotoDuzenleyici-Setup-{#AppVersion}
SetupIconFile=..\FotoDuzenleyici.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
; Uygulama ya da komut satırı açıksa kurulum önce kapatılmasını ister.
AppMutex={#RunningMutex},Global\{#RunningMutex}
ChangesEnvironment=yes
ShowLanguageDialog=no

[Languages]
; İlk dil, sistem dili eşleşmezse kullanılır: Türkçe dışındaki her sistemde İngilizce.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.Integration=Windows integration:
turkish.Integration=Windows ile bütünleşme:
english.ContextMenu=Add "Open with Foto Düzenleyici" to the folder and drive right-click menu
turkish.ContextMenu=Klasör ve sürücü sağ tık menüsüne "Foto Düzenleyici ile aç" ekle
english.OpenWith=Open with Foto Düzenleyici
turkish.OpenWith=Foto Düzenleyici ile aç
english.AddToPath=Add the command-line tool (fotoduzenle) to PATH
turkish.AddToPath=Komut satırı aracını (fotoduzenle) PATH'e ekle
english.NewerInstalled=A newer version (%1) is already installed. Install the older version {#AppVersion} anyway?
turkish.NewerInstalled=Daha yeni bir sürüm (%1) zaten kurulu. Yine de eski sürüm {#AppVersion} kurulsun mu?
english.DeleteSettings=Also delete settings and caches?%n%n%1%n%nYour photos, archive folders and journals (_Gunluk) are never touched.
turkish.DeleteSettings=Ayarlar ve önbellekler de silinsin mi?%n%n%1%n%nFotoğraflarına, arşiv klasörlerine ve günlüklere (_Gunluk) hiçbir durumda dokunulmaz.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:Integration}"
Name: "addtopath"; Description: "{cm:AddToPath}"; GroupDescription: "{cm:Integration}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Sağ tık: klasör ve sürücü (SD kart kökü). Görev seçilmezse (güncellemede kaldırılırsa) eski kayıt silinir.
Root: HKA; Subkey: "Software\Classes\Directory\shell\FotoDuzenleyici"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWith}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\FotoDuzenleyici"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\FotoDuzenleyici\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Drive\shell\FotoDuzenleyici"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWith}"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Drive\shell\FotoDuzenleyici"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Drive\shell\FotoDuzenleyici\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\FotoDuzenleyici"; ValueType: none; Flags: deletekey; Tasks: not contextmenu
Root: HKA; Subkey: "Software\Classes\Drive\shell\FotoDuzenleyici"; ValueType: none; Flags: deletekey; Tasks: not contextmenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';

{ ---- PATH: kullanıcı kurulumunda HKCU, tüm kullanıcılarda HKLM; yalnız kendi girdimize dokunulur. ---- }

function EnvRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKLM else Result := HKCU;
end;

function EnvKey: String;
begin
  if IsAdminInstallMode then
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
  else
    Result := 'Environment';
end;

function SamePath(A, B: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Trim(A)), RemoveBackslashUnlessRoot(Trim(B))) = 0;
end;

{ Dir dışındaki girdiler sırasıyla korunur; Dir varsa çıkarılır. Found: Dir listede miydi. }
function PathWithout(Path, Dir: String; var Found: Boolean): String;
var
  Part: String;
  P: Integer;
begin
  Result := '';
  Found := False;
  while Length(Path) > 0 do
  begin
    P := Pos(';', Path);
    if P = 0 then
    begin
      Part := Path;
      Path := '';
    end else begin
      Part := Copy(Path, 1, P - 1);
      Delete(Path, 1, P);
    end;
    if SamePath(Part, Dir) then
      Found := True
    else if Trim(Part) <> '' then
    begin
      if Result <> '' then Result := Result + ';';
      Result := Result + Part;
    end;
  end;
end;

procedure AddToPath(Dir: String);
var
  Path, Rest: String;
  Found: Boolean;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Path := '';
  Rest := PathWithout(Path, Dir, Found);
  if Found then Exit;
  if Rest <> '' then Rest := Rest + ';';
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Rest + Dir);
end;

procedure RemoveFromPath(Dir: String);
var
  Path, Rest: String;
  Found: Boolean;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Exit;
  Rest := PathWithout(Path, Dir, Found);
  if Found then RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Rest);
end;

{ ---- Sürüm: daha yenisi kuruluysa sor (sessiz kurulumda eskiye dönülmez). ---- }

function NextPart(var S: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P = 0 then
  begin
    Result := StrToIntDef(S, 0);
    S := '';
  end else begin
    Result := StrToIntDef(Copy(S, 1, P - 1), 0);
    Delete(S, 1, P);
  end;
end;

function CompareVersions(A, B: String): Integer;
var
  I, X, Y: Integer;
begin
  Result := 0;
  for I := 1 to 4 do
  begin
    X := NextPart(A);
    Y := NextPart(B);
    if X <> Y then
    begin
      if X > Y then Result := 1 else Result := -1;
      Exit;
    end;
  end;
end;

function InstalledVersion: String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', Result) then
      Result := '';
end;

function InitializeSetup: Boolean;
var
  V: String;
begin
  Result := True;
  V := InstalledVersion;
  if (V <> '') and (CompareVersions(V, '{#AppVersion}') > 0) then
    Result := SuppressibleMsgBox(FmtMessage(CustomMessage('NewerInstalled'), [V]),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if WizardIsTaskSelected('addtopath') then
      AddToPath(ExpandConstant('{app}'))
    else
      RemoveFromPath(ExpandConstant('{app}'));
end;

{ ---- Kaldırma: PATH girdisi silinir; ayarlar yalnız "Evet" denirse (varsayılan Hayır, sessizde sorulmaz). ---- }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Settings: String;
begin
  if CurUninstallStep = usUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    Settings := ExpandConstant('{localappdata}\FotoDuzenleyici');
    if DirExists(Settings) then
      if MsgBox(FmtMessage(CustomMessage('DeleteSettings'), [Settings]),
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Settings, True, True, True);
  end;
end;
