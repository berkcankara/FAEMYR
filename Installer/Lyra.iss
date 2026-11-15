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
AppName=Lyra
AppVersion={#AppVersion}
AppPublisher=Lyra
AppPublisherURL=https://github.com/berkcankara/LYRA-Lossless-Music-Player
AppSupportURL=https://github.com/berkcankara/LYRA-Lossless-Music-Player/issues
DefaultDirName={localappdata}\Programs\Lyra
DefaultGroupName=Lyra
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
SetupIconFile=..\Source\Assets\Lyra.ico
UninstallDisplayIcon={app}\Lyra.exe
OutputDir={#ReleaseDir}
OutputBaseFilename=Lyra-{#AppVersion}-Windows-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Lyra.exe
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
english.Launch=Open Lyra
turkish.Shortcuts=Kısayollar:
turkish.StartMenuShortcut=Başlat menüsü kısayolu oluştur
turkish.DesktopShortcut=Masaüstü kısayolu oluştur
turkish.Launch=Lyra'yı aç

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:Shortcuts}"
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Lyra\Lyra"; Filename: "{app}\Lyra.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Lyra.exe"; Tasks: startmenu
Name: "{userdesktop}\Lyra"; Filename: "{app}\Lyra.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Lyra.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Lyra.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

; There is deliberately no recursive uninstall-delete rule: the data folder
; is created by Lyra, never shipped here, and retained on upgrade/uninstall.
