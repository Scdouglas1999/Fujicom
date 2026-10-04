#define AppVersion "3.0.2"
#define BuildRoot "..\artifacts\package"

[Setup]
AppId={{9B13C34D-96B0-49CF-BF3F-E53E9979AE81}
AppName=ASCOM Fujicom Camera Driver
AppVersion={#AppVersion}
AppPublisher=Sean Douglas
AppPublisherURL=https://github.com/Scdouglas1999/Fujicom
AppSupportURL=https://github.com/Scdouglas1999/Fujicom/issues
AppUpdatesURL=https://github.com/Scdouglas1999/Fujicom/releases
DefaultDirName={commonpf64}\ASCOM\Camera\Fujicom
DefaultGroupName=ASCOM Fujicom Camera Driver
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\release
OutputBaseFilename=Fujicom.Setup.v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
WizardImageFile=..\InstallerResources\WizardImage.bmp
UninstallDisplayIcon={app}\ASCOM.ScdouglasFujifilm.exe
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Sean Douglas
VersionInfoDescription=ASCOM Camera driver for Fujifilm X and GFX cameras
VersionInfoProductName=Fujicom ASCOM Camera Driver

[Files]
Source: "{#BuildRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{app}\ASCOM.ScdouglasFujifilm.exe"; Parameters: "/register"; StatusMsg: "Registering the Fujicom ASCOM driver..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{app}\ASCOM.ScdouglasFujifilm.exe"; Parameters: "/unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterFujicom"

[Icons]
Name: "{group}\Fujicom project page"; Filename: "https://github.com/Scdouglas1999/Fujicom"
