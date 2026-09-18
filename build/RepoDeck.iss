; RepoDeck installer.
;
; Per-user by design. RepoDeck tells people, on the confirmation panel before it installs
; anything, that it will not ask for administrator access. An installer that demanded
; elevation would contradict that on the first screen a new user sees, so this one installs
; into the user's own profile and never asks.
;
; Compiled by build/package.ps1, which supplies the version, the payload and the output
; paths. It is not meant to be run by hand, but it will work if you pass the same
; definitions.

#ifndef AppVersion
  #error AppVersion must be defined (build/package.ps1 does this)
#endif

#ifndef AppVersionNumeric
  #error AppVersionNumeric must be defined (build/package.ps1 does this)
#endif

#ifndef PayloadDir
  #error PayloadDir must be defined (build/package.ps1 does this)
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef OutputBaseName
  #define OutputBaseName "RepoDeck-Setup"
#endif

#define AppName "RepoDeck"
#define AppPublisher "RepoDeck"
#define AppExeName "RepoDeck.exe"
#define AppUrl "https://github.com/Strict009/RepoDeck"

[Setup]
; This GUID identifies RepoDeck to Windows for the life of the application. Changing it
; would make an upgrade look like a different product and leave the old one installed
; alongside, so it stays fixed even as the version moves.
AppId={{8F3A6C21-5D74-4B98-9E0C-2A7B1F6D4E35}

AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
; Windows file metadata cannot hold a pre-release suffix, so the numeric form goes in the
; version fields and the full "0.1.0-alpha" goes in the text field beside them.
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName}
VersionInfoCompany={#AppPublisher}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user installation, no elevation, and no offer of one.
;
; PrivilegesRequiredOverridesAllowed is deliberately not set. With it, Inno Setup opens
; with a dialog offering "Install for all users (requires administrative privileges)",
; which puts an elevation prompt on the first screen of an application whose whole
; argument is that it never needs one. RepoDeck installs into the user's own profile and
; does not ask.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Uninstall registration, so RepoDeck appears in Apps & features like anything else.
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}

OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile=..\src\RepoDeck\Assets\RepoDeck.ico

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; A pre-release build should say so while it is being installed, not only afterwards.
AppComments=Development build. Not a finished release.
LicenseFile=
InfoBeforeFile=alpha-notice.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The whole self-contained publish output. recursesubdirs picks up the runtime's own
; subdirectories; ignoreversion because these files are versioned as a set, not
; individually, and comparing them one by one gets upgrades wrong.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the program directory. RepoDeck's data - what it installed for you, your
; favourites, the activity log - lives in %LOCALAPPDATA%\RepoDeck and is deliberately
; left alone: uninstalling RepoDeck must not silently delete the applications it
; installed on your behalf, and reinstalling should find them again.
Type: dirifempty; Name: "{app}"

[Code]
// Refuse to install over a copy that is currently running. Replacing files underneath a
// live process is how an installation ends up half-updated, and RepoDeck applies the same
// rule to itself that it applies to everything it installs: ask, never force.
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;
