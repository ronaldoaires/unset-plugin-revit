; =============================================================================
; UnSet BIM - Revit add-in installer (Inno Setup 6)
;
; Per-user install, no administrator rights (see the installer brief):
;   Binaries : %LocalAppData%\UnSetBIM\<year>\<version>\
;   Manifest : %AppData%\Autodesk\Revit\Addins\<year>\UnSetBIM.addin
;
; Every version goes to its own folder, so an update can run silently while Revit
; is open (no locked DLL is overwritten). The new .addin points to the new folder
; and Revit loads it on the next start. Older version folders are removed by the
; next install (a folder still in use is skipped and removed later).
;
; Build:  ISCC.exe /DAppVersion=1.2.3 installer\UnSetBIM.iss
;         (expects artifacts\Release.R25, Release.R26, Release.R27 from dotnet build;
;          a missing year is skipped at compile time)
; Silent: UnSetBIM-Setup-1.2.3.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
; =============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName       "UnSet BIM"
#define AppPublisher  "UnSet BIM"
#define AppUrl        "https://unset.com.br"
#define ArtifactsDir  "..\artifacts"
#define AddinFileName "UnSetBIM.addin"
#define HostDll       "UnSetBim.Revit.dll"

[Setup]
; Installer identity. Never change it: it links updates and the uninstaller.
AppId={{6F1C2B7E-3A1D-4E8B-9C2F-5D7A0B1E4C93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}

PrivilegesRequired=lowest
DefaultDirName={localappdata}\UnSetBIM
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Revit may stay open: nothing in use is overwritten.
CloseApplications=no
RestartApplications=no

OutputDir=output
OutputBaseFilename=UnSetBIM-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName={#AppName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; Manifest template (single source of truth, also used by the debug build).
Source: "..\UnSetBim.Revit\UnSetBim.addin"; Flags: dontcopy

#if DirExists(ArtifactsDir + "\Release.R25")
Source: "{#ArtifactsDir}\Release.R25\*"; DestDir: "{app}\2025\{#AppVersion}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallYear('2025')
#endif
#if DirExists(ArtifactsDir + "\Release.R26")
Source: "{#ArtifactsDir}\Release.R26\*"; DestDir: "{app}\2026\{#AppVersion}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallYear('2026')
#endif
#if DirExists(ArtifactsDir + "\Release.R27")
Source: "{#ArtifactsDir}\Release.R27\*"; DestDir: "{app}\2027\{#AppVersion}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: ShouldInstallYear('2027')
#endif

[UninstallDelete]
; Brief: uninstall removes binaries and all user data, including the stored token.
Type: filesandordirs; Name: "{app}"

[Messages]
brazilianportuguese.WelcomeLabel2=Este assistente vai instalar o plugin [name/ver] no Revit.%n%nO Revit pode continuar aberto: a nova versão é carregada na próxima vez que ele for iniciado.

[Code]
const
  // Years this build can install. Add a year here and a [Files] block above.
  SupportedYears = '2025,2026,2027';
  // Lowest and highest year looked at on uninstall (covers future builds too).
  UninstallFirstYear = 2025;
  UninstallLastYear = 2035;

function YearList(): TStringList;
begin
  Result := TStringList.Create;
  Result.Delimiter := ',';
  Result.StrictDelimiter := True;
  Result.DelimitedText := SupportedYears;
end;

// Revit <year> is installed when its product key exists in the registry
// or Revit.exe is in the default folder.
function IsRevitInstalled(const Year: String): Boolean;
begin
  Result :=
    RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\Revit\' + Year) or
    FileExists(ExpandConstant('{commonpf64}\Autodesk\Revit ' + Year + '\Revit.exe'));
end;

// True when the payload for this year was compiled into the installer.
function IsYearBundled(const Year: String): Boolean;
begin
  Result := False;
#if DirExists(ArtifactsDir + "\Release.R25")
  if Year = '2025' then Result := True;
#endif
#if DirExists(ArtifactsDir + "\Release.R26")
  if Year = '2026' then Result := True;
#endif
#if DirExists(ArtifactsDir + "\Release.R27")
  if Year = '2027' then Result := True;
#endif
end;

function ShouldInstallYear(const Year: String): Boolean;
begin
  Result := IsYearBundled(Year) and IsRevitInstalled(Year);
end;

function AddinFolder(const Year: String): String;
begin
  Result := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\') + Year;
end;

// ── Setup start ─────────────────────────────────────────────────────────────

function InitializeSetup(): Boolean;
var
  Years: TStringList;
  I: Integer;
  Found: String;
  Legacy: String;
begin
  Result := True;
  Found := '';
  Legacy := '';
  Years := YearList();
  try
    for I := 0 to Years.Count - 1 do
    begin
      if ShouldInstallYear(Years[I]) then
        Found := Found + ' ' + Years[I];

      // The old machine-wide plugin (admin installer) registers the same AddInId.
      if FileExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\') + Years[I] + '\UnSet BIM.addin') then
        Legacy := Legacy + ' ' + Years[I];
    end;
  finally
    Years.Free;
  end;

  if Found = '' then
  begin
    SuppressibleMsgBox(
      'Nenhuma versão compatível do Autodesk Revit foi encontrada neste computador.' + #13#10 + #13#10 +
      'O UnSet BIM funciona no Revit 2025 ou superior.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  Log('Revit years to install:' + Found);

  if Legacy <> '' then
    SuppressibleMsgBox(
      'Foi encontrada uma versão antiga do plugin (instalada para todos os usuários) no Revit' + Legacy + '.' + #13#10 + #13#10 +
      'Desinstale "UnSet Revit Models" em Configurações > Aplicativos para evitar que o Revit carregue as duas versões.',
      mbInformation, MB_OK, IDOK);
end;

// ── After files are copied ──────────────────────────────────────────────────

procedure WriteAddinManifest(const Year: String);
var
  Template: AnsiString;
  Xml: String;
  Lines: TArrayOfString;
  DllPath: String;
begin
  DllPath := ExpandConstant('{app}\') + Year + '\{#AppVersion}\{#HostDll}';

  if not LoadStringFromFile(ExpandConstant('{tmp}\UnSetBim.addin'), Template) then
    RaiseException('Could not read the .addin template.');

  Xml := String(Template);
  StringChangeEx(Xml, '__ASSEMBLY__', DllPath, True);

  ForceDirectories(AddinFolder(Year));
  SetArrayLength(Lines, 1);
  Lines[0] := Xml;

  // UTF-8 so user folders with accents (e.g. C:\Users\José) stay valid.
  if not SaveStringsToUTF8File(AddinFolder(Year) + '\{#AddinFileName}', Lines, False) then
    RaiseException('Could not write ' + AddinFolder(Year) + '\{#AddinFileName}');

  Log('Manifest written for Revit ' + Year + ': ' + DllPath);
end;

// Remove version folders other than the one just installed. A folder in use by an
// open Revit cannot be deleted: it is skipped now and removed by the next install.
procedure RemoveOldVersions(const Year: String);
var
  Base: String;
  Rec: TFindRec;
begin
  Base := ExpandConstant('{app}\') + Year;
  if FindFirst(Base + '\*', Rec) then
  try
    repeat
      if ((Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         (Rec.Name <> '.') and (Rec.Name <> '..') and (Rec.Name <> '{#AppVersion}') then
      begin
        if DelTree(Base + '\' + Rec.Name, True, True, True) then
          Log('Removed old version ' + Year + '\' + Rec.Name)
        else
          Log('Old version in use, kept for now: ' + Year + '\' + Rec.Name);
      end;
    until not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Years: TStringList;
  I: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  ExtractTemporaryFile('UnSetBim.addin');

  Years := YearList();
  try
    for I := 0 to Years.Count - 1 do
      if ShouldInstallYear(Years[I]) then
      begin
        WriteAddinManifest(Years[I]);
        RemoveOldVersions(Years[I]);
      end;
  finally
    Years.Free;
  end;
end;

// ── Uninstall ───────────────────────────────────────────────────────────────

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Year: Integer;
  Manifest: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  for Year := UninstallFirstYear to UninstallLastYear do
  begin
    Manifest := AddinFolder(IntToStr(Year)) + '\{#AddinFileName}';
    if FileExists(Manifest) then
      DeleteFile(Manifest);
  end;
end;
