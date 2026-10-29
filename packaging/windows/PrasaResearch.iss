#ifndef PublishDir
  #error PublishDir must point to the self-contained win-x64 publish directory
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
[Setup]
AppId={{9AE793C6-A7E0-4DBC-BBFD-05833E025D49}
AppName=PrasaResearch
AppVersion={#AppVersion}
AppPublisher=PrasaResearch
DefaultDirName={localappdata}\Programs\PrasaResearch
DefaultGroupName=PrasaResearch
OutputDir=..\..\Releases\Windows
OutputBaseFilename=PrasaResearch-Setup-Windows
SetupIconFile=..\branding\app.ico
UninstallDisplayIcon={app}\PrasaResearch.exe
WizardStyle=modern dark
WizardImageFile=..\branding\wizard.bmp
WizardSmallImageFile=..\branding\wizard-small.bmp
WizardImageStretch=yes
WizardBackColor=$200E06
WizardImageBackColor=$200E06
WizardSmallImageBackColor=$200E06
LicenseFile=..\Distribution-Notice.txt
InfoAfterFile=..\THIRD-PARTY-NOTICES.txt
DisableWelcomePage=no
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
UninstallDisplayName=PrasaResearch
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Installation options:"; Flags: unchecked
Name: offlineocr; Description: "Install bundled offline OCR and English language data"; GroupDescription: "Installation options:"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "ocr\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\ocr\*"; DestDir: "{app}\ocr"; Tasks: offlineocr; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\PrasaResearch"; Filename: "{app}\PrasaResearch.exe"
Name: "{group}\Uninstall PrasaResearch"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PrasaResearch"; Filename: "{app}\PrasaResearch.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\PrasaResearch.exe"; Description: "Launch PrasaResearch"; Flags: nowait postinstall skipifsilent
[Messages]
WelcomeLabel1=Welcome to PrasaResearch
WelcomeLabel2=Install your revenue intelligence workspace.%n%nExplore official UK filings, process company lists and inspect the evidence behind every financial result.
FinishedHeadingLabel=PrasaResearch is ready
FinishedLabel=Installation has completed. Your settings and processing history are retained during upgrades and uninstall.
; Deliberately no [UninstallDelete] entry for settings, database, credential store or user documents.
