[Setup]
AppId={{B98E4B17-48DF-4F15-8AE4-03A849F2D87B}}
AppName=AutoCAD Plugin DICTA
AppVersion=1.0
DefaultDirName={commonappdata}\Autodesk\ApplicationPlugins\DICTA.bundle
OutputDir=C:\Users\dicta\Downloads
OutputBaseFilename=AutoCADPluginInstaller
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes


; =========================
; VARIABLES
; =========================

#define BasePath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\AutoCAD"
#define ResourcePath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\Resources"
#define BundlePath "{commonappdata}\Autodesk\ApplicationPlugins\DICTA.bundle"


; =========================
; ARCHIVOS
; =========================

[Files]

; 1. PackageContents.xml (Manifiesto de Autodesk Autoloader para todas las versiones de AutoCAD)
Source: "{#BasePath}\PackageContents.xml"; \
DestDir: "{#BundlePath}"; \
Flags: ignoreversion

; 2. Ensamblado DLL principal de AutoCAD
Source: "{#BasePath}\bin\Release\net48\AutoCAD.dll"; \
DestDir: "{#BundlePath}\Contents"; \
Flags: ignoreversion

; 3. Plantillas de Excel y tablas de normas
Source: "{#ResourcePath}\SGH - Carga de ocupacion.xlsx"; \
DestDir: "{#BundlePath}\Contents\Resources"; \
Flags: ignoreversion

Source: "{#ResourcePath}\Normas_NSR10_NFPA.xlsx"; \
DestDir: "{#BundlePath}\Contents\Resources"; \
Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

; 4. Iconos e imágenes del Ribbon
Source: "{#BasePath}\Images\*"; \
DestDir: "{#BundlePath}\Contents\Images"; \
Flags: ignoreversion recursesubdirs skipifsourcedoesntexist


; =========================
; LIMPIEZA PREVIA
; =========================

[InstallDelete]
Type: filesandordirs; Name: "{#BundlePath}\Contents\*"


; =========================
; POST INSTALL
; =========================

[Run]
Filename: "{commonappdata}\Autodesk\ApplicationPlugins"; \
Flags: postinstall shellexec skipifsilent nowait
