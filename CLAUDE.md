# RevitPlugin DICTA v2.3

Plugin de Revit multi-versión (2020-2025) para etiquetado automático, dimensionamiento, predimensionado y generación de descripciones de elementos MEP y ARQ.

## Stack
- C# .NET Framework 4.7/4.8 + .NET 8.0 (multi-target)
- Revit API (2020-2025)
- WPF + WinForms UI
- COM Interop (Excel, Word, MSProject)
- JSON para configuración y aprendizaje

## Estructura
```
RevitPlugin/          → Proyecto principal (csproj multi-target)
  UI/                 → WPF windows
  Learning/           → Motor de sugerencias de mapeo de parámetros
  Images/             → Iconos ribbon
  Json/Templates/     → Configs JSON (hvac_config, mapping, unknowns)
  Compat/             → Helpers de compatibilidad entre versiones
SharedCode/           → Código compartido (linked files)
  MyApp.cs            → Entry point: crea ribbon "DICTA" con ~14 botones
  MyTAGS_ELE.cs       → Etiquetado eléctrico
  MyTAGS_ILU.cs       → Iluminación
  MyTAGS_Tomas.cs     → Tomas
  MyTAGS_Alumbrado.cs → Alumbrado
  MyTAGS_SIPRA.cs     → Contra incendios
  MyTAGS_HVAC*.cs     → HVAC (3 variantes)
  MyTAGS_Coor.cs      → Coordinación
  MyCommandPreDim.cs  → Predimensionado (~1500 líneas)
  MyArana.cs          → Herramienta "Araña"
  TagMainMenu.cs      → Menú WinForms de selección de tags
Resources/            → Descripciones.xlsx, RutaCritica.xlsx, RETIE.ttf
```

## Comandos principales
- `GenerateDescriptions.cs` → Genera Excel con descripciones por disciplina (HVAC, Hydraulic, Electrical, Architecture)
- `MyCommandPreDim.cs` → Predimensionado desde diccionario CSV/XLSX
- `MyTAGS_ARQ.cs` → Tags arquitectónicos (puertas, muros con filtro de espesor, iluminación, accesorios)
- `MyTAGS_CielosRasos.cs` → Tags de cielo raso + dimensionamiento automático
- `ValidadorParametrosCommand.cs` → Valida parámetros BIM por disciplina (v2.3 redesign: sidebar compliance, KPI cards, scope Tipo/Instancia)
- `CotasArq/` → Sub-proyecto independiente de cotas exteriores

## Patrones clave
- `[Transaction(TransactionMode.Manual)]` en todos los comandos
- Namespace: `MiNamespace`
- Compilación condicional: `REVIT2021_OR_EARLIER`, `REVIT2022_OR_LATER`, `REVIT_LEGACY_ELEMENTID`
- PostBuild deploya DLL + .addin + assets a cada versión de Revit

## Validador de Parámetros (✅ COMPLETADO v2.3)

### Estructura del plugin
Ubicación: `RevitPlugin/ValidadorParametros/`
- `ValidadorParametrosCommand.cs` → IExternalCommand entry point (Transaction.Manual)
- `ParameterValidator.cs` → Lógica core: valida scope Tipo/Instancia, severidad crítico/advertencia, detecta AlcanceIncorrecto
- `ExcelParameterLoader.cs` → Lee `ParametrosRequeridos.xlsx` con ClosedXML (sin Office instalado)
- `ValidationIssue.cs` → Modelo de datos con TipoProblema (ParametroFaltante, ValorVacio, NombreIncorrecto, Duplicado, AlcanceIncorrecto) y Severidad
- `DisciplineParameter.cs` → Regla de parámetro requerido con Alcance y Obligatorio
- `DisciplineComplianceModel.cs` → Modelos para sidebar (DisciplineComplianceModel, CategoryComplianceModel, ValidationSummary)
- `UI/UiValidadorParametros.xaml(.cs)` → WPF profesional (1440x760) con:
  - Sidebar: árbol de disciplinas + barra de compliance por categoría
  - KPI cards: Familias, Faltantes, Duplicados, Compliance %
  - DataGrid: Severidad, Parámetro, Alcance, Categoría, Familia, Problema, Valor actual, Acción
  - Filtros: solo críticos, faltantes, Tipo/Instancia, búsqueda libre
  - Footer Auto Fix con conteo de problemas corregibles
  - Converters: SeveridadToColorConverter, SeveridadToTextConverter, ComplianceToBarColorConverter

### Archivo de configuración: ParametrosRequeridos.xlsx
**Ubicación:** DLL folder (ej: `bin/Release/net48/ParametrosRequeridos.xlsx`)

**Estructura (6 columnas):**
| Col | Campo | Tipo | Notas |
|-----|-------|------|-------|
| A | **Disciplina** | string | HVAC, Plumbing, Electrical, ARQ, etc. Obligatorio. |
| B | **Categoria** | string | Categoría Revit (Mechanical Equipment, Doors, Pipes, etc.) o vacío (aplica a todas) |
| C | **Parámetro** | string | Nombre exacto del parámetro. Obligatorio. Case-sensitive matching. |
| D | **Alcance** | string | `Tipo` o `Instancia`. Default: `Instancia` si vacío. |
| E | **Obligatorio** | string | `Sí`, `SÍ`, `YES` o `1` = crítico; cualquier otro valor = advertencia |
| F | **Descripcion** | string | Descripción informativa del parámetro. Opcional. |

**Reglas de lectura:**
- Fila 1: Headers (ignorada)
- Filas vacías o sin Disciplina/Parámetro: ignoradas
- Alcance vacío → default "Instancia"
- Obligatorio vacío → default false (advertencia, no crítico)

**Ejemplo:**
```
Disciplina | Categoria              | Parámetro      | Alcance    | Obligatorio | Descripcion
HVAC       | Mechanical Equipment   | PTO_CodigoCosto| Tipo       | Sí          | Código de costo
HVAC       | Mechanical Equipment   | PTO_Marca      | Instancia  | Sí          | Marca del equipo
Plumbing   | Pipes                  | PTO_Material   | Tipo       | No          | Material tubería
ARQ        | Doors                  | PTO_Codigo     | Tipo       | Sí          | Código presupuesto
```

### Generación de plantilla
```csharp
ExcelParameterLoader.CrearPlantilla(@"C:\ParametrosRequeridos.xlsx");
```
Genera Excel con:
- Headers con fondo azul (#1A237E) y texto blanco, bold
- 11 ejemplos pre-cargados (HVAC, Plumbing, Electrical, ARQ)
- Dropdowns para Alcance (Tipo/Instancia) y Obligatorio (Sí/No) en filas 2-12+

### Patrones clave
- ClosedXML para lectura Excel (sin COM Interop)
- `ICollectionView` para filtrado dinámico de issues (no rebuild de ItemsSource)
- `new HashSet<T>(collection, comparer)` en lugar de `.ToHashSet()` (compatibilidad .NET 4.7/4.8)
- Scope validation: `Element elemType = doc.GetElement(elem.GetTypeId());` para distinguir Tipo vs Instancia
- `ValidationSummary` retorna `List<ValidationIssue>` + `TotalElementos`, `TotalFamilias`

## GenerateDescriptions (en curso)
- RevitPlugin\GenerateDescriptions.cs (principal)
- RevitPlugin\Json\Templates\hvac_config.json
- RevitPlugin\Json\Templates\hidraulico_config.json
- RevitPlugin\Json\Templates\rci_config.json
- RevitPlugin\Json\JsonFileManager.cs
- RevitPlugin\Learning\* (motor sugerencias mapeo)
- RevitPlugin\UI\UiGenerateDescriptions.xaml(.cs)
- Cambios recientes:
  - Fallback FIG "" → "(FIG)" en los 3 JSONs (para highlight rojo via HighlightNoData)
  - Limpieza de guiones en materialName y componentName

## Build
- Usar msbuild.exe de VS2022 (dotnet build no soporta COM references)
- Multi-target: net47, net48, net8.0-windows con `#if REVIT_LEGACY_ELEMENTID`
