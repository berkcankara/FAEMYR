#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by build-installer.ps1
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be supplied by build-installer.ps1
#endif

[Setup]
AppId={{143BE633-E847-4713-BCF1-23517D58B46E}
AppName=Faemyr
AppVersion={#AppVersion}
AppPublisher=Faemyr
AppPublisherURL=https://github.com/berkcankara/FAEMYR-Lossless-Music-Player
AppSupportURL=https://github.com/berkcankara/FAEMYR-Lossless-Music-Player/issues
DefaultDirName={localappdata}\Programs\Faemyr
DefaultGroupName=Faemyr
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableWelcomePage=yes
DisableDirPage=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
WizardStyle=modern
SetupIconFile=..\Source\Assets\Faemyr.ico
UninstallDisplayIcon={app}\Faemyr-brand-v2.ico
OutputDir={#ReleaseDir}
OutputBaseFilename=Faemyr-{#AppVersion}-Windows-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Faemyr.exe,Lyra.exe
RestartApplications=no
UsePreviousTasks=yes
SetupLogging=yes
#ifdef TestBuild
CreateUninstallRegKey=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.Shortcuts=Shortcuts:
english.StartMenuShortcut=Create a Start menu shortcut
english.DesktopShortcut=Create a desktop shortcut
english.Launch=Open Faemyr
turkish.Shortcuts=Kısayollar:
turkish.StartMenuShortcut=Başlat menüsü kısayolu oluştur
turkish.DesktopShortcut=Masaüstü kısayolu oluştur
turkish.Launch=Faemyr'i aç

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:Shortcuts}"
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Keep existing shortcuts/pins working on upgrades without shipping the old application.
Source: "{#PayloadDir}\Faemyr.exe"; DestDir: "{app}"; DestName: "Lyra.exe"; Flags: ignoreversion; Check: IsLegacyInstall

[InstallDelete]
Type: files; Name: "{app}\Lyra.dll"; Check: IsLegacyInstall
Type: files; Name: "{app}\Lyra.deps.json"; Check: IsLegacyInstall
Type: files; Name: "{app}\Lyra.runtimeconfig.json"; Check: IsLegacyInstall
Type: files; Name: "{app}\Lyra.pdb"; Check: IsLegacyInstall

[Icons]
Name: "{userprograms}\Faemyr\Faemyr"; Filename: "{app}\Faemyr.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Faemyr-brand-v2.ico"; Tasks: startmenu
Name: "{userdesktop}\Faemyr"; Filename: "{app}\Faemyr.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Faemyr-brand-v2.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Faemyr.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

; There is deliberately no recursive uninstall-delete rule: the data folder
; is created by Faemyr, never shipped here, and retained on upgrade/uninstall.

[Code]
function IsLegacyInstall: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\Lyra.exe'));
end;
