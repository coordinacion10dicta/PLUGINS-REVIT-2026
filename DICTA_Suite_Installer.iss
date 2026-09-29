[Setup]
AppId={{9E1A3C4D-5B6E-7F8A-9B0C-1D2E3F4A5B6C}}
AppName=Plugins DICTA (AutoCAD y Revit)
AppVersion=1.0
DefaultDirName={commonappdata}\Autodesk
OutputDir=C:\Users\dicta\Downloads
OutputBaseFilename=DICTA_Plugins_Installer
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes

[Components]
Name: "autocad"; Description: "Plugin para AutoCAD (2020 - 2026+)"; Types: full custom; Flags: fixed
Name: "revit"; Description: "Plugin para Revit (2020 - 2025)"; Types: full custom; Flags: fixed


; =========================
; VARIABLES
; =========================

#define AutoCADPath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\AutoCAD"
#define RevitBasePath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\RevitPlugin\bin"
#define RevitAddinPath "C:\Users\dicta\Desktop\Proyecto_AC\Addin\ADINS USAURIOS"
#define ResourcePath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\Resources"
#define RevitImagesPath "C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\RevitPlugin\Images"

#define AutoCADBundle "{commonappdata}\Autodesk\ApplicationPlugins\DICTA.bundle"


; =========================
; ARCHIVOS DE INSTALACIÓN
; =========================

[Files]

; =========================================================
; AUTOCAD (DICTA.bundle) - Todas las versiones de AutoCAD
; =========================================================

Source: "{#AutoCADPath}\PackageContents.xml"; \
DestDir: "{#AutoCADBundle}"; \
Components: autocad; \
Flags: ignoreversion

Source: "{#AutoCADPath}\bin\Release\net48\AutoCAD.dll"; \
DestDir: "{#AutoCADBundle}\Contents"; \
Components: autocad; \
Flags: ignoreversion

Source: "{#ResourcePath}\SGH - Carga de ocupacion.xlsx"; \
DestDir: "{#AutoCADBundle}\Contents\Resources"; \
Components: autocad; \
Flags: ignoreversion

Source: "{#ResourcePath}\Normas_NSR10_NFPA.xlsx"; \
DestDir: "{#AutoCADBundle}\Contents\Resources"; \
Components: autocad; \
Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

Source: "{#AutoCADPath}\Images\*"; \
DestDir: "{#AutoCADBundle}\Contents\Images"; \
Components: autocad; \
Flags: ignoreversion recursesubdirs skipifsourcedoesntexist


; =========================================================
; REVIT 2020 - NET47
; =========================================================

Source: "{#RevitBasePath}\Release\net47\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2020\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2020\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2020\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2020.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2020"; \
Components: revit; \
Flags: ignoreversion


; =========================================================
; REVIT 2021 - NET47
; =========================================================

Source: "{#RevitBasePath}\Release\net47\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2021\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2021\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2021\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2021.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2021"; \
Components: revit; \
Flags: ignoreversion


; =========================================================
; REVIT 2022 - NET48
; =========================================================

Source: "{#RevitBasePath}\Release\net48\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2022.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2022"; \
Components: revit; \
Flags: ignoreversion


; =========================================================
; REVIT 2023 - NET48
; =========================================================

Source: "{#RevitBasePath}\Release\net48\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2023.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023"; \
Components: revit; \
Flags: ignoreversion


; =========================================================
; REVIT 2024 - NET48
; =========================================================

Source: "{#RevitBasePath}\Release\net48\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2024.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; \
Components: revit; \
Flags: ignoreversion


; =========================================================
; REVIT 2025 - NET8
; =========================================================

Source: "{#RevitBasePath}\Release\net8.0-windows\RevitPlugin.dll"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#ResourcePath}\ESPE.ILU.xlsx"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\DICTA"; \
Components: revit; \
Flags: ignoreversion

Source: "{#RevitImagesPath}\*"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\DICTA\Images"; \
Components: revit; \
Flags: ignoreversion recursesubdirs

Source: "{#RevitAddinPath}\RevitPlugin2025.addin"; \
DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; \
Components: revit; \
Flags: ignoreversion


; =========================
; LIMPIEZA
; =========================

[InstallDelete]
Type: filesandordirs; Name: "{#AutoCADBundle}\Contents\*"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2020\RevitPlugin*.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2021\RevitPlugin*.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2022\RevitPlugin*.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\RevitPlugin*.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\RevitPlugin*.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\RevitPlugin*.addin"


; =========================
; POST INSTALL
; =========================

[Run]
Filename: "{commonappdata}\Autodesk"; \
Flags: postinstall shellexec skipifsilent nowait
