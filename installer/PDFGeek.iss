; PDFGeek installer - Inno Setup script
;
; Build it with:   iscc installer\PDFGeek.iss and Inno Setup 7
; Or just run build.ps1 from the repo root, which publishes and then compiles this.
;
; Design decisions worth knowing:
;   * PrivilegesRequired=lowest - installs per-user by default, so NO UAC prompt.
;     An unsigned installer that also demands elevation is exactly the combination that makes people cancel.
;     Admins can still do a machine-wide install; the dialog offers it.
;   * No bundled anything. No toolbars, no offers, no third-party installers, ever.
;   * The .pdf shell integration is an OPTIONAL task and is OFF by default. 
;     Hijacking someone's PDF association without asking is the behaviour we are positioning against.

#define AppName        "PDFGeek"
#define AppSourceDir   "..\publish"
#define AppExeName     "PDFGeek.exe"

#define AppVersion() GetVersionComponents(AppSourceDir + "\" + AppExeName, Local[0], Local[1], Local[2], Local[3]), str(Local[0]) + "." + str(Local[1]) + "." + str(Local[2])

#define AppPublisher   "TechyGeeksHome"
#define AppURL         "https://techygeekshome.info"
#define AppSupportURL  "https://github.com/techygeekshome/PDFGeek/issues"
#define AppUpdatesURL  "https://github.com/techygeekshome/PDFGeek/releases"
#define FirstYear      "2026"
#define CurrentYear    GetDateTimeString('yyyy','','')

; From 2027 onward show a range rather than only the current year
; so the copyright reads 2026-2027 and so on.
#if CurrentYear == FirstYear
  #define CopyrightYears FirstYear
#else
  #define CopyrightYears FirstYear + "-" + CurrentYear
#endif

#include "PDFGeek_languages.iss"

[Setup]
AppId={{7DF6A1C2-4E3B-4F0A-9B5E-2C81D4F60A37}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}

AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppSupportURL}
AppUpdatesURL={#AppUpdatesURL}

AppCopyright={#CopyrightYears} {#AppPublisher}

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer

ShowLanguageDialog=yes
UsePreviousLanguage=no
LanguageDetectionMethod=uilanguage
WizardStyle=modern

UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

LicenseFile=..\LICENSE.rtf

SetupIconFile=..\icons\pdfgeek.ico

OutputDir=..\dist
OutputBaseFilename=PDFGeekSetup

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
AllowNoIcons=yes

; Per-user by default: no UAC prompt, no admin needed.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog

Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"
Name: "pdfcontextmenu"; Description: "{cm:AddOpenWithPDFGeek}"; GroupDescription: "{cm:Integrations}"; Flags: unchecked

[Files]
Source: "{#AppSourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md";  Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:AppWebSite}"; Filename: "{#AppURL}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Optional "Open with PDFGeek" verb on .pdf files. Deliberately does NOT change the default
; handler - it only adds an entry to the context menu, and only if the user ticked the task.
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\PDFGeek"; \
    ValueType: string; ValueName: ""; ValueData: "Open with PDFGeek"; \
    Flags: uninsdeletekey; Tasks: pdfcontextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\PDFGeek"; \
    ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExeName},0"; \
    Tasks: pdfcontextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\PDFGeek\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; \
    Flags: uninsdeletekey; Tasks: pdfcontextmenu

; Register the app so "Open with" lists it properly.
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; \
    ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; \
    Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; \
    ValueType: string; ValueName: ".pdf"; ValueData: ""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
