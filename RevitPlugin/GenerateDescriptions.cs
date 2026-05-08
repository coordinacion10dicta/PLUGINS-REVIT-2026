// =========================================================
// IMPORTACIONES DE NAMESPACES
// =========================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;

using Excel = Microsoft.Office.Interop.Excel;

using MiNamespace.Json;
using MiNamespace.Learning;
using MiNamespace.UI;
using Newtonsoft.Json;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class GenerateDescriptions : IExternalCommand
    {
        private HvacConfig _config = null;
        private LearningSystem _learningSystem = null;
        private bool _learningEnabled = false;
        private string _configFileName = "hvac_config.json";
        private readonly Dictionary<ElementId, string> _materialCache = new Dictionary<ElementId, string>();
        private readonly Dictionary<ElementId, string> _sistemaCache = new Dictionary<ElementId, string>();
        private static readonly string[] _noDataTargets = {
            "(NO MATERIAL)", "(NO SISTEMA)", "(NO DIÁMETRO)",
            "(NO AISLAMIENTO)", "(NO MARCA)", "(NO MODELO)",
            "(SISTEMA NO DEFINIDO)", "(ERROR AL CARGAR PLANTILLA)",
            "(MATERIAL)", "(MM)", "(IN)", "(MARCA)", "(EQUIPO)", "(FIG)"
        };
        private const string DescriptionsTemplateFileName = "Descripciones.xlsx";
        private const string DescriptionsTemplateResourceName = "RevitPlugin.Resources.Descripciones.xlsx";
        private readonly Dictionary<string, List<string>> _disciplineKeywords = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "HIDRAULICO", new List<string> { "Desagüe", "Gas", "RCI", "Suministro", "Pluvial", "Sanitario", "Lluvia", "Drenaje", "Hidraulico" } },
            { "RCI", new List<string> { "RCI", "Fuego", "Incendio", "Gabinete" } },
            { "HVAC", new List<string> { "Aire", "HVAC", "Inyección", "Extracción", "Ventilación" } }
        };

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No hay un documento activo.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            Excel.Application xlApp = null;
            Excel.Workbook xlWorkbook = null;
            Excel.Worksheet xlWorksheet = null;

            try
            {
                JsonFileManager.EnsureRuntimeEnvironment();

                UiGenerateDescriptions ui = new UiGenerateDescriptions();
                bool? uiResult = ui.ShowDialog();

                if (ui.OpenLearningViewRequested)
                {
                    LearningView learningView = new LearningView();
                    learningView.Show();
                    return Result.Cancelled;
                }

                if (uiResult != true)
                    return Result.Cancelled;

                string disciplina = ui.DisciplinaSeleccionada;
                _configFileName = RemoveAccents(disciplina).ToLower() + "_config.json";
                _learningSystem = new LearningSystem();
                _learningEnabled = disciplina.Equals("HVAC", StringComparison.OrdinalIgnoreCase);

                UiCategorias uiCategorias = new UiCategorias(doc, disciplina);
                if (uiCategorias.ShowDialog() != true)
                    return Result.Cancelled;

                List<BuiltInCategory> categorias = uiCategorias.GetSeleccionadas();

                List<Element> elementos = new List<Element>();

                foreach (var cat in categorias)
                {
                    elementos.AddRange(
                        new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToElements());
                }

                // Estructura: Dictionary<SistemaClasificado, Dictionary<NombreNivel, Dictionary<KeyUnica, Descripcion>>>
                var itemsPorSistema = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
                var levelElevations = new Dictionary<string, double>();
                Dictionary<string, string> itemsPendientes = new Dictionary<string, string>();
                List<ElementId> elementosSinSistema = new List<ElementId>();

                foreach (var e in elementos)
                {
                    try
                    {
                        string sistema = GetSistemaHvac(e)?.Trim();
                        string material = GetMaterialHvac(e)?.Trim();

                        Level lvlObj = null;
                        string nivel = GetLevelInfo(e, out lvlObj);
                        if (lvlObj != null && !levelElevations.ContainsKey(nivel))
                            levelElevations[nivel] = lvlObj.Elevation;

                        // DETERMINAR PERTENENCIA A DISCIPLINA antes del costoso BuildDescripcion
                        bool perteneceADisciplina = false;

                        if (disciplina.Equals("HIDRAULICO", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(material) &&
                            material.IndexOf("PVC", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            perteneceADisciplina = true;
                        }

                        if (!perteneceADisciplina && !string.IsNullOrWhiteSpace(sistema) && _disciplineKeywords.ContainsKey(disciplina))
                        {
                            var keywords = _disciplineKeywords[disciplina];
                            perteneceADisciplina = keywords.Any(k => sistema.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                        }

                        // Skip temprano: si pertenece a otra disciplina conocida, no construir descripción
                        if (!perteneceADisciplina && !string.IsNullOrWhiteSpace(sistema))
                        {
                            bool perteneceAOtraDisciplina = false;
                            foreach (var kvp in _disciplineKeywords)
                            {
                                if (kvp.Key.Equals(disciplina, StringComparison.OrdinalIgnoreCase)) continue;
                                if (kvp.Value.Any(k => sistema.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    perteneceAOtraDisciplina = true;
                                    break;
                                }
                            }
                            if (perteneceAOtraDisciplina)
                                continue;
                        }

                        // Solo ahora construir la descripción (operación costosa)
                        string desc = BuildDescripcion(e, disciplina);

                        if (string.IsNullOrWhiteSpace(desc) || desc == "Descripción genérica")
                            continue;

                        if (perteneceADisciplina)
                        {
                            string diametro = GetDiametroHvac(e)?.Trim();
                            string matUpper = material?.ToUpper() ?? "SIN_MATERIAL";
                            if (string.IsNullOrWhiteSpace(diametro)) diametro = "SIN_DIAMETRO";

                            string unidad = (e is Pipe || e is Duct) ? "m" : "pza";
                            string sistemaClasificado = ClasificarSistemaGeneral(sistema);

                            if (!itemsPorSistema.ContainsKey(sistemaClasificado))
                                itemsPorSistema[sistemaClasificado] = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                            if (!itemsPorSistema[sistemaClasificado].ContainsKey(nivel))
                                itemsPorSistema[sistemaClasificado][nivel] = new Dictionary<string, string>();

                            string descConUnidad = desc + "|" + unidad;
                            if (!itemsPorSistema[sistemaClasificado][nivel].ContainsKey(descConUnidad))
                                itemsPorSistema[sistemaClasificado][nivel][descConUnidad] = descConUnidad;
                        }
                        else
                        {
                            // CASO C: ES UN VERDADERO PENDIENTE (Sin sistema o sistema desconocido)
                            elementosSinSistema.Add(e.Id);

                            string prefix = string.IsNullOrWhiteSpace(sistema) ? "[SIN SISTEMA] " : $"[SISTEMA DESCONOCIDO: {sistema}] ";
                            string pKey = $"{e.Category?.Name}|{e.Name}|{sistema}";

                            if (!itemsPendientes.ContainsKey(pKey))
                                itemsPendientes[pKey] = prefix + desc;
                        }
                    }
                    catch
                    {
                        // Saltar elemento si falla su procesamiento individual
                    }
                }


                string templatePath = ResolveDescriptionsTemplatePath();
                EnsureOutputDirectoryExists(path: templatePath);

                //SALIDA
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "RevitPlugin",
                    "Descripciones_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                EnsureOutputDirectoryExists(path);

                xlApp = new Excel.Application();
                xlApp.Visible = false;
                xlApp.DisplayAlerts = false;

                // copiar plantilla a archivo final
                File.Copy(templatePath, path, true);

                // abrir copia
                xlWorkbook = xlApp.Workbooks.Open(path);
                xlWorksheet = (Excel.Worksheet)xlWorkbook.Worksheets[1];
                xlWorksheet.Name = disciplina.ToUpper();

                // Optimización de rendimiento (después de abrir libro)
                xlApp.ScreenUpdating = false;
                xlApp.Calculation = Excel.XlCalculation.xlCalculationManual;
                xlApp.EnableEvents = false;

                int row = 5;
                int itemNum = 1;

                // ESCRIBIR TÍTULOS DE COLUMNA
                xlWorksheet.Cells[row, 1] = "ITEM";
                xlWorksheet.Cells[row, 2] = "DESCRIPCIÓN";
                xlWorksheet.Cells[row, 3] = "UNIDAD";
                Excel.Range headerRange = xlWorksheet.Range[xlWorksheet.Cells[row, 1], xlWorksheet.Cells[row, 3]];
                headerRange.Font.Bold = true;
                row++;

                // ORDENAR SISTEMAS
                var sistemasOrdenados = itemsPorSistema.Keys.OrderBy(s => s).ToList();

                foreach (string sistemaKey in sistemasOrdenados)
                {
                    // Encabezado de Sistema
                    xlWorksheet.Cells[row, 2] = "SISTEMA: " + sistemaKey;
                    Excel.Range sysRange = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                    sysRange.Font.Bold = true;
                    sysRange.Font.Size = 14;
                    sysRange.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightSteelBlue);
                    row += 2;

                    var itemsPorNivel = itemsPorSistema[sistemaKey];

                    // ESCRIBIR POR NIVELES (Ordenados por Elevación)
                    var nivelesOrdenados = itemsPorNivel.Keys
                        .Where(n => n != "SIN NIVEL")
                        .OrderBy(n => levelElevations.ContainsKey(n) ? levelElevations[n] : 0)
                        .ToList();
                    if (itemsPorNivel.ContainsKey("SIN NIVEL")) nivelesOrdenados.Add("SIN NIVEL");

                    foreach (string nivel in nivelesOrdenados)
                    {
                        // Encabezado de Nivel
                        xlWorksheet.Cells[row, 2] = "NIVEL: " + nivel.ToUpper();
                        Excel.Range nivelRange = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                        nivelRange.Font.Bold = true;
                        nivelRange.Font.Size = 12;

                        if (nivel == "SIN NIVEL")
                        {
                            nivelRange.Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.Red);
                            nivelRange.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.MistyRose);
                        }
                        else
                        {
                            nivelRange.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightGray);
                        }

                        row++;

                        // SEPARAR POR CATEGORÍA
                        var itemsEnNivel = itemsPorNivel[nivel].Values.ToList();
                        var tuberias = itemsEnNivel.Where(i => i.EndsWith("|m")).ToList();
                        var otros = itemsEnNivel.Where(i => !i.EndsWith("|m")).ToList();

                        // --- SUB-SECCIÓN TUBERIAS ---
                        if (tuberias.Count > 0)
                        {
                            xlWorksheet.Cells[row, 2] = "TUBERIAS";
                            Excel.Range catRange = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                            catRange.Font.Bold = true;
                            catRange.Font.Underline = true;
                            row++;

                            foreach (string itemData in tuberias)
                            {
                                WriteItemRow(xlWorksheet, ref row, ref itemNum, itemData);
                            }

                            // FILA DE TOTAL TUBERIAS
                            xlWorksheet.Cells[row, 2] = "TOTAL TUBERIAS " + nivel.ToUpper();
                            Excel.Range totalRange = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                            totalRange.Font.Bold = true;
                            totalRange.Borders[Excel.XlBordersIndex.xlEdgeTop].LineStyle = Excel.XlLineStyle.xlContinuous;
                            row += 2;
                        }

                        // --- SUB-SECCIÓN CONEXIONES ---
                        if (otros.Count > 0)
                        {
                            xlWorksheet.Cells[row, 2] = "CONEXIONES";
                            Excel.Range catRange = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                            catRange.Font.Bold = true;
                            catRange.Font.Underline = true;
                            row++;

                            foreach (string itemData in otros)
                            {
                                WriteItemRow(xlWorksheet, ref row, ref itemNum, itemData);
                            }

                            // FILA DE TOTAL CONEXIONES
                            xlWorksheet.Cells[row, 2] = "TOTAL CONEXIONES " + nivel.ToUpper();
                            Excel.Range totalRange2 = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                            totalRange2.Font.Bold = true;
                            totalRange2.Borders[Excel.XlBordersIndex.xlEdgeTop].LineStyle = Excel.XlLineStyle.xlContinuous;
                            row += 2;
                        }
                    }
                }

                // SECCIÓN DE PENDIENTES (Si existen)
                if (itemsPendientes.Count > 0)
                {
                    row += 2;
                    xlWorksheet.Cells[row, 2] = "ELEMENTOS SIN SISTEMA DEFINIDO (REVISAR EN MODELO)";
                    Excel.Range pendingHeader = xlWorksheet.Range[xlWorksheet.Cells[row, 2], xlWorksheet.Cells[row, 2]];
                    pendingHeader.Font.Bold = true;
                    pendingHeader.Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.Red);
                    row += 1;

                    foreach (var kv in itemsPendientes)
                    {
                        xlWorksheet.Cells[row, 1] = "P"; // 'P' de Pendiente
                        var cell = (Excel.Range)xlWorksheet.Cells[row, 2];
                        cell.Value = "[PENDIENTE SISTEMA] " + kv.Value;
                        cell.Font.Italic = true;
                        row++;
                    }
                }

                xlWorksheet.Columns[1].HorizontalAlignment = Excel.XlHAlign.xlHAlignLeft;
                xlWorksheet.Columns[1].AutoFit();
                xlWorksheet.Columns[2].WrapText = true;
                xlWorksheet.Columns[2].ColumnWidth = 120;
                xlWorksheet.Columns[3].HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                xlWorksheet.Columns[3].AutoFit();

                // Restaurar configuración de Excel antes de guardar
                xlApp.ScreenUpdating = true;
                xlApp.Calculation = Excel.XlCalculation.xlCalculationAutomatic;
                xlApp.EnableEvents = true;

                xlWorkbook.SaveAs(path);

                xlWorkbook.Close(true);
                xlApp.Quit();

                Marshal.ReleaseComObject(xlWorksheet);
                Marshal.ReleaseComObject(xlWorkbook);
                Marshal.ReleaseComObject(xlApp);

                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }

                int pendingCount = _learningSystem.GetPendingCount();
                int totalProcesados = 0;
                foreach (var s in itemsPorSistema.Values)
                    totalProcesados += s.Values.Sum(d => d.Count);

                string resultMessage = $"Elementos procesados: {totalProcesados}";

                /*
                if (elementosSinSistema.Count > 0)
                {
                    uidoc.Selection.SetElementIds(elementosSinSistema);
                    resultMessage += $"\n\n¡AVISO!: Se encontraron {elementosSinSistema.Count} elementos sin sistema. Se han listado al final del Excel y seleccionado en Revit para tu revisión.";
                }
                */

                TaskDialog.Show("Resultado", resultMessage);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                ReleaseComObjectSafe(xlWorksheet);
                ReleaseComObjectSafe(xlWorkbook);
                if (xlApp != null) { try { xlApp.Quit(); } catch { } }
                ReleaseComObjectSafe(xlApp);

                message = ex.ToString();
                TaskDialog.Show("Error", ex.ToString());
                return Result.Failed;
            }
        }

        private void HighlightNoData(Excel.Range cell, string desc)
        {
            string descUpper = desc.ToUpper();
            bool hasAny = false;
            foreach (var target in _noDataTargets)
            {
                if (descUpper.Contains(target)) { hasAny = true; break; }
            }
            if (!hasAny) return;

            foreach (var target in _noDataTargets)
            {
                int index = descUpper.IndexOf(target);
                if (index >= 0)
                {
                    var chars = cell.Characters[index + 1, target.Length];
                    chars.Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.Red);
                }
            }
        }

        private string GetMaterialDucto(Element e)
        {
            Element type = e.Document.GetElement(e.GetTypeId());

            var mappedResolution = ResolveMappedParameter(e, "Material");
            string mappedValue = ReadParameterValue(mappedResolution?.Parameter);
            if (!string.IsNullOrWhiteSpace(mappedValue))
                return mappedValue;

            if (type != null)
            {
                // 1. Intentar parámetro real
                var p = type.LookupParameter("Material");

                if (p != null && p.HasValue)
                {
                    string val = p.AsValueString() ?? p.AsString();

                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                // 2. Fallback ? Type.Name
                string typeName = type.Name;

                if (!string.IsNullOrWhiteSpace(typeName))
                    return typeName;
            }

            TrackUnknownParameter(e, "Material", "Material");
            return "";
        }

        private string GetNombreDucto(Element e)
        {
            Element type = e.Document.GetElement(e.GetTypeId());

            if (type is ElementType et)
            {
                string familyName = et.FamilyName;

                if (!string.IsNullOrWhiteSpace(familyName))
                {
                    var map = _config?.ducto?.family_map;

                    if (map != null)
                    {
                        foreach (var kv in map)
                        {
                            if (familyName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                                return kv.Value;
                        }
                    }

                    return familyName;
                }
            }

            return "";
        }
        // =========================================================
        // CONFIG JSON
        // =========================================================

        public class HvacConfig
        {
            public HvacTuberiaConfig tuberia { get; set; }
            public HvacDuctoConfig ducto { get; set; }
            public HvacMechanicalConfig mechanical { get; set; }
            public HvacAccesorioConfig accesorio { get; set; }
            public HvacConexionConfig conexion { get; set; }
        }
        //--Tuberias
        public class HvacTuberiaConfig
        {
            public string template { get; set; }
            public Dictionary<string, string> material_map { get; set; }
            public string default_insulation { get; set; }
            public string default_system { get; set; }
            public Dictionary<string, string> fallbacks { get; set; }
        }
        //Ducto
        public class HvacDuctoConfig
        {
            public string template { get; set; }
            public Dictionary<string, string> material_map { get; set; }

            public Dictionary<string, string> family_map { get; set; }

            public string default_system { get; set; }
            public Dictionary<string, string> fallbacks { get; set; }
        }
        //mechanical equipamnet 
        public class HvacMechanicalConfig
        {
            public string template { get; set; }
            public Dictionary<string, string> fallbacks { get; set; }
        }
        //Accesorio
        public class HvacAccesorioConfig
        {
            public string template { get; set; }
            public Dictionary<string, string> fallbacks { get; set; }
        }
        //Conexion
        public class HvacConexionConfig
        {
            public string template { get; set; }
            public Dictionary<string, string> fallbacks { get; set; }
        }
        //METODO JSON 
        private void EnsureConfigLoaded()
        {
            if (_config != null)
                return;

            JsonFileManager.EnsureRuntimeEnvironment();
            _config = JsonFileManager.ReadJson<HvacConfig>(_configFileName);
            if (_config == null)
                _config = GetDefaultHvacConfig();
        }


        private string ResolveDescriptionsTemplatePath()
        {
            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dllFolder = Path.GetDirectoryName(dllPath);

            string[] candidatePaths =
            {
                Path.Combine(dllFolder, "Resources", DescriptionsTemplateFileName),
                Path.Combine(dllFolder, DescriptionsTemplateFileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", DescriptionsTemplateFileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DescriptionsTemplateFileName)
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            string extractedTemplatePath = Path.Combine(dllFolder, "Resources", DescriptionsTemplateFileName);
            ExtractEmbeddedDescriptionsTemplate(extractedTemplatePath);
            return extractedTemplatePath;
        }

        private void ExtractEmbeddedDescriptionsTemplate(string templatePath)
        {
            EnsureOutputDirectoryExists(templatePath);

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(DescriptionsTemplateResourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("No se encontró el recurso embebido de la plantilla.", DescriptionsTemplateResourceName);

                using (FileStream fileStream = new FileStream(templatePath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }

        private void EnsureOutputDirectoryExists(string path)
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private HvacConfig GetDefaultHvacConfig()
        {
            return new HvacConfig
            {
                tuberia = new HvacTuberiaConfig
                {
                    template = "Elemento {FAMILY} - {SYSTEM} (Error al cargar plantilla)",
                    default_system = "GENERAL",
                    material_map = new Dictionary<string, string>()
                },
                ducto = new HvacDuctoConfig
                {
                    template = "Ducto {FAMILY} - {SYSTEM} (Error al cargar plantilla)",
                    default_system = "GENERAL",
                    material_map = new Dictionary<string, string>()
                },
                mechanical = new HvacMechanicalConfig
                {
                    template = "{DESCRIPTION} marca {BRAND}, modelo {MODEL}. Incluye: {INCLUDES}",
                    fallbacks = new Dictionary<string, string>
                {
                    { "DESCRIPTION", "(NO DESCRIPCIÓN)" },
                    { "BRAND", "(NO MARCA)" },
                    { "MODEL", "(NO MODELO)" },
                    { "INCLUDES", "suministro, instalación, mano de obra" }
                }
                },
                accesorio = new HvacAccesorioConfig
                {
                    template = "{COMPONENT} {MATERIAL} {BRAND} {MODEL} de {DIAMETER_MM} {DIAMETER_IN} {CONNECTION}, incluye: {INCLUDES}",
                    fallbacks = new Dictionary<string, string>
                    {
                        { "COMPONENT", "(ACCESORIO)" },
                        { "INCLUDES", "materiales, acarreos, instalación y mano de obra" }
                    }
                },
                conexion = new HvacConexionConfig
                {
                    template = "{COMPONENT} de {MATERIAL} {CLASS} {ANGLE} {DIAMETERS} {FIG}, marca {BRAND}, Incluye: {INCLUDES}",
                    fallbacks = new Dictionary<string, string>
                    {
                        { "COMPONENT", "(CONEXIÓN)" },
                        { "DIAMETERS", "(DIÁMETRO)" },
                        { "INCLUDES", "suministro e instalación, mano de obra, pruebas, etiquetas y todo lo necesario para su ejecución" }
                    }
                }
            };
        }

        // =========================================================
        // DISPATCH DE DESCRIPCIÓN POR DISCIPLINA
        // =========================================================
        private string BuildDescripcion(Element e, string disciplina)
        {
            if (EsCurva(e))
            {
                EnsureConfigLoaded();
                HvacTuberiaConfig cfg = null;

                if (e is Pipe) cfg = _config.tuberia;
                else if (e is Duct)
                {
                    // Lógica ducto simplificada
                    var dCfg = _config.ducto;
                    var dValues = new Dictionary<string, string>
                    {
                        { "FAMILY", GetNombreDucto(e) },
                        { "SYSTEM", GetSistemaHvac(e) },
                        { "MATERIAL", GetMaterialDucto(e) },
                        { "SIZE", GetParam(e, "Size") }
                    };
                    return ApplyTemplate(dCfg.template, dValues, dCfg.fallbacks);
                }

                if (cfg == null) return "Elemento genérico";

                // 1. Obtener Diámetro Base y convertir a MM e IN
                var resolution = ResolveParameter(e, "Diameter", "Diameter");
                var p = resolution?.Parameter;
                string d_mm = "";
                string d_in = "";

                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    double feet = p.AsDouble();
                    d_mm = Math.Round(feet * 304.8) + "mm";
                    // Para pulgadas, intentamos un redondeo simple o el valor nominal
                    d_in = Math.Round(feet * 12 * 8) / 8.0 + "\""; // Redondeo a 1/8"
                    if (d_in.EndsWith(".0\"")) d_in = d_in.Replace(".0\"", "\"");
                }
                else if (p != null && p.HasValue)
                {
                    d_in = p.AsValueString() ?? "";
                }

                // 2. Extraer parámetros adicionales
                string material = GetMaterialHvac(e);
                string materialName = MapMaterial(material);

                // REFUERZO: Si después de todo el proceso solo sale "acero", forzamos el nombre del tipo
                if (materialName.Equals("acero", StringComparison.OrdinalIgnoreCase))
                {
                    Element type = e.Document.GetElement(e.GetTypeId());
                    if (type != null) materialName = type.Name;
                }

                // Regla para evitar redundancia (ej: Tubo de tubería de...)
                materialName = materialName.Replace("tubería de ", "").Replace("Tubería de ", "").Replace("Tubo de ", "").Replace("tubo de ", "").Replace(" - ", " ").Replace("-", " ").Replace("–", " ").Replace("—", " ").Trim();

                string marca = GetBrandMechanical(e);
                string clase = GetParam(e, "Schedule");
                if (string.IsNullOrWhiteSpace(clase)) clase = GetParam(e, "Clase");

                // 3. Lógica para FIG (Obligatorio para PPR / Polipropileno)
                var resRef = ResolveParameter(e, "Referencia comercial", "Referencia", "Modelo");
                string figValue = ReadParameterValue(resRef?.Parameter);

                bool esPolipropileno = material.IndexOf("PPR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       material.IndexOf("Polipropileno", StringComparison.OrdinalIgnoreCase) >= 0;

                if (esPolipropileno)
                {
                    if (string.IsNullOrWhiteSpace(figValue))
                        figValue = "(FIG)"; // Alerta para Excel
                    else
                        figValue = $"Fig. {figValue}";
                }
                else if (string.IsNullOrWhiteSpace(figValue))
                {
                    figValue = ""; // No obligatorio para otros
                }

                // 4. Lógica especial para INCLUDES
                string includes = (cfg.fallbacks != null && cfg.fallbacks.ContainsKey("INCLUDES")) ? cfg.fallbacks["INCLUDES"] : "instalación y mano de obra";
                if (material.IndexOf("PPR", StringComparison.OrdinalIgnoreCase) >= 0)
                    includes = "suministro e instalación";
                else if (material.IndexOf("ACERO", StringComparison.OrdinalIgnoreCase) >= 0 || material.IndexOf("FIERRO", StringComparison.OrdinalIgnoreCase) >= 0)
                    includes = "materiales, acarreos, cortes, soldadura, mano de obra, pruebas, equipo y herramienta";
                else if (material.IndexOf("PEAD", StringComparison.OrdinalIgnoreCase) >= 0)
                    includes = "suministro, instalación, uniones por medio de termofisión, mano de obra, equipo y herramienta";

                var values = new Dictionary<string, string>
                {
                    { "MATERIAL", materialName },
                    { "CLASS", clase },
                    { "DIAMETER_MM", d_mm },
                    { "DIAMETER_IN", d_in },
                    { "FIG", figValue },
                    { "BRAND", marca },
                    { "INCLUDES", includes },
                    { "SYSTEM", GetSistemaHvac(e) },
                    { "DIAMETER", GetDiametroHvac(e) }, // Compatibilidad con plantillas viejas
                    { "INSULATION", GetParam(e, "Insulation") } // Compatibilidad con plantillas viejas
                };
                return ApplyTemplate(cfg.template, values, cfg.fallbacks);
            }

            if (EsConexion(e))
            {
                EnsureConfigLoaded();
                var cfg = _config.conexion;
                if (cfg == null) return "Conexión genérica";

                string material = GetMaterialHvac(e);
                string materialName = MapMaterial(material);

                // Validar si es un material real o solo un fallback inválido de Revit (ej. "Standard" o "2 MEDIDORES")
                if (materialName.Equals(material, StringComparison.OrdinalIgnoreCase))
                {
                    string mUpper = materialName.ToUpper();
                    bool esMaterialValido = mUpper.Contains("PVC") || mUpper.Contains("PPR") || mUpper.Contains("CPVC") ||
                                             mUpper.Contains("COBRE") || mUpper.Contains("PEAD") || mUpper.Contains("ACERO") ||
                                             mUpper.Contains("BRONCE") || mUpper.Contains("HIERRO") || mUpper.Contains("FIERRO") ||
                                             mUpper.Contains("POLIPROPILENO") || mUpper.Contains("GALVANIZADO");
                    if (!esMaterialValido)
                    {
                        materialName = "";
                    }
                }

                materialName = materialName.Replace("tubería de ", "").Replace("Tubería de ", "").Replace("Tubo de ", "").Replace("tubo de ", "").Replace(" - ", " ").Replace("-", " ").Replace("–", " ").Replace("—", " ").Trim();

                string componentName = GetNombreComponente(e);

                // Recortar colilla de material redundante en el nombre del componente si existe (ej. "buje ... - pvc" -> "buje ...", "trap - pvc - sch" -> "trap - sch")
                if (!string.IsNullOrWhiteSpace(materialName) && !string.IsNullOrWhiteSpace(componentName))
                {
                    string escapedMat = System.Text.RegularExpressions.Regex.Escape(materialName);
                    
                    // 1. Caso en medio: " - pvc - " -> " - "
                    string patternMiddle = $@"\s*[-–—/]\s*{escapedMat}\s*[-–—/]\s*";
                    componentName = System.Text.RegularExpressions.Regex.Replace(componentName, patternMiddle, " - ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // 2. Caso al final: " - pvc" -> ""
                    string patternEnd = $@"\s*[-–—/]?\s*{escapedMat}$";
                    componentName = System.Text.RegularExpressions.Regex.Replace(componentName, patternEnd, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // También probar con el material original sin mapear
                    if (!string.IsNullOrWhiteSpace(material))
                    {
                        string escapedRawMat = System.Text.RegularExpressions.Regex.Escape(material);
                        
                        string patternMiddleRaw = $@"\s*[-–—/]\s*{escapedRawMat}\s*[-–—/]\s*";
                        componentName = System.Text.RegularExpressions.Regex.Replace(componentName, patternMiddleRaw, " - ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        string patternEndRaw = $@"\s*[-–—/]?\s*{escapedRawMat}$";
                        componentName = System.Text.RegularExpressions.Regex.Replace(componentName, patternEndRaw, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    componentName = componentName.Trim().TrimEnd('-', '–', '—', '/').Trim();
                    componentName = componentName.Replace(" - ", " ").Replace("-", " ").Replace("–", " ").Replace("—", " ").Trim();
                }

                var values = new Dictionary<string, string>
                {
                    { "COMPONENT", componentName },
                    { "MATERIAL", materialName },
                    { "CLASS", GetParam(e, "Schedule") ?? GetParam(e, "Clase") },
                    { "ANGLE", GetAnguloConexion(e) },
                    { "DIAMETERS", GetDiametrosConexion(e) },
                    { "FIG", ReadParameterValue(ResolveParameter(e, "Referencia comercial", "Referencia", "Modelo")?.Parameter) },
                    { "BRAND", GetBrandMechanical(e) },
                    { "INCLUDES", "" }
                };
                return ApplyTemplate(cfg.template, values, cfg.fallbacks);
            }

            /* 
            if (EsAccesorio(e))
            ...
            */

            return "Descripción genérica";
        }

        private bool EsConexion(Element e)
        {
            if (e.Category == null) return false;
            return (BuiltInCategory)e.Category.Id.IntegerValue == BuiltInCategory.OST_PipeFitting;
        }

        private string GetAnguloConexion(Element e)
        {
            var p = e.LookupParameter("Angle") ??
                    e.LookupParameter("Ángulo") ??
                    e.LookupParameter("Angle of Fitting") ??
                    e.LookupParameter("Ángulo de flexión");

            if (p != null && p.HasValue)
            {
                if (p.StorageType == StorageType.Double)
                {
                    double rad = p.AsDouble();
                    // Evitamos valores absurdos de radianes
                    if (rad > 0.001)
                    {
                        double deg = rad * (180.0 / Math.PI);
                        return Math.Round(deg) + "°";
                    }
                }
                else
                {
                    string textVal = p.AsValueString() ?? p.AsString();
                    if (!string.IsNullOrWhiteSpace(textVal)) return textVal;
                }
            }
            return "";
        }

        private string GetDiametrosConexion(Element e)
        {
            string size = GetParam(e, "Size") ?? "";
            // Limpieza de nombres raros si es necesario
            return size.Replace("-", "x");
        }

        private bool EsCurva(Element e)
        {
            return e is Pipe || e is Duct;
        }

        private bool EsAccesorio(Element e)
        {
            if (e.Category == null) return false;
            BuiltInCategory bic = (BuiltInCategory)e.Category.Id.IntegerValue;
            return bic == BuiltInCategory.OST_PipeFitting ||
                   bic == BuiltInCategory.OST_PipeAccessory ||
                   bic == BuiltInCategory.OST_DuctFitting ||
                   bic == BuiltInCategory.OST_DuctAccessory ||
                   bic == BuiltInCategory.OST_PlumbingFixtures ||
                   bic == BuiltInCategory.OST_Sprinklers;
        }



        private string GetNombreComponente(Element e)
        {
            string rawName = "";
            Element type = e.Document.GetElement(e.GetTypeId());
            if (type != null)
            {
                var pDesc = type.LookupParameter("Description") ?? type.LookupParameter("Descripción");
                if (pDesc != null && pDesc.HasValue)
                {
                    string val = pDesc.AsValueString() ?? pDesc.AsString();
                    if (!string.IsNullOrWhiteSpace(val) && val.Length < 60) rawName = val;
                }
                if (string.IsNullOrWhiteSpace(rawName) && type is ElementType et)
                    rawName = et.FamilyName;
            }

            if (string.IsNullOrWhiteSpace(rawName))
                rawName = e.Category?.Name ?? "Accesorio";

            rawName = rawName.Trim();
            // Eliminar números iniciales seguidos de espacio (ej: "2 medidores" -> "medidores", "90 codo" -> "codo")
            rawName = System.Text.RegularExpressions.Regex.Replace(rawName, @"^\d+\s+", "");
            rawName = rawName.Trim();

            if (rawName.Length > 1)
                return char.ToUpper(rawName[0]) + rawName.Substring(1).ToLower();

            return rawName.ToUpper();
        }

        private string GetTipoConexion(Element e)
        {
            var p = e.LookupParameter("Connection Type") ?? e.LookupParameter("Conexión") ?? e.LookupParameter("Tipo de conexión");
            if (p != null && p.HasValue) return p.AsValueString() ?? p.AsString();

            string typeName = e.Name.ToLower();
            if (typeName.Contains("rosca")) return "roscable";
            if (typeName.Contains("soldar") || typeName.Contains("soldable")) return "soldable";
            if (typeName.Contains("brida")) return "bridado";
            if (typeName.Contains("ranura")) return "ranurado";

            return "";
        }

        // =========================================================
        // DESCRIPCIÓN HVAC 
        // =========================================================



        // =========================================================
        // TEMPLATE ENGINE
        // =========================================================

        private string ApplyTemplate(
            string template,
            Dictionary<string, string> values,
            Dictionary<string, string> fallbacks)
        {
            // Remover conector " de " si el material está vacío o se resolvió como vacío
            if (values.ContainsKey("MATERIAL"))
            {
                string matVal = values["MATERIAL"];
                if (string.IsNullOrWhiteSpace(matVal) && fallbacks != null && fallbacks.ContainsKey("MATERIAL"))
                    matVal = fallbacks["MATERIAL"];

                if (string.IsNullOrWhiteSpace(matVal))
                {
                    template = template.Replace(" de {MATERIAL}", "").Replace(" de {material}", "");
                }
            }

            foreach (var kv in values)
            {
                string key = kv.Key.ToUpper();
                string value = kv.Value;

                if (string.IsNullOrWhiteSpace(value))
                {
                    if (fallbacks != null && fallbacks.ContainsKey(key))
                        value = fallbacks[key];
                    else
                        value = $"(NO HAY {key})";
                }

                template = template.Replace("{" + key + "}", value);
            }

            // Limpieza final de espacios dobles y comas huérfanas por tags vacíos
            return template.Replace("  ", " ").Replace(" ,", ",").Trim();
        }

        private string GetNombreTuberia(Element e)
        {
            Element type = e.Document.GetElement(e.GetTypeId());

            if (type != null)
            {
                string familyName = type.Name;

                if (!string.IsNullOrWhiteSpace(familyName))
                    return familyName;
            }

            return "";
        }

        //==================================
        //METODOS HVAC 
        //==================================
        //---DETECTORES
        //METODO TUBERIA
        private bool EsTuberia(Element e)
        {
            if (e.Category == null) return false;

            BuiltInCategory bic = (BuiltInCategory)e.Category.Id.IntegerValue;

            return bic == BuiltInCategory.OST_PipeCurves
                || bic == BuiltInCategory.OST_PipeFitting
                || bic == BuiltInCategory.OST_PipeAccessory
                || bic == BuiltInCategory.OST_PlumbingFixtures
                || bic == BuiltInCategory.OST_Sprinklers;
        }
        //METODO DUCTO - DETECTOR
        private bool EsDucto(Element e)
        {
            if (e.Category == null) return false;

            BuiltInCategory bic = (BuiltInCategory)e.Category.Id.IntegerValue;

            return bic == BuiltInCategory.OST_DuctCurves
                || bic == BuiltInCategory.OST_DuctFitting
                || bic == BuiltInCategory.OST_DuctAccessory;
        }
        //
        private string GetSistemaHvac(Element e)
        {
            if (_sistemaCache.TryGetValue(e.Id, out string cached))
                return cached;

            string result;
            try
            {
                MEPSystem system = null;
                if (e is Pipe pipe) system = pipe.MEPSystem;
                else if (e is Duct duct) system = duct.MEPSystem;
                else if (e is FamilyInstance fi)
                {
                    var manager = fi.MEPModel?.ConnectorManager;
                    if (manager != null)
                    {
                        foreach (Connector conn in manager.Connectors)
                        {
                            if (conn.MEPSystem != null) { system = conn.MEPSystem; break; }
                        }
                    }
                }

                if (system != null)
                {
                    Element type = e.Document.GetElement(system.GetTypeId());
                    if (type != null) { result = type.Name; _sistemaCache[e.Id] = result; return result; }
                }

                var res = ResolveParameter(e, "System Type", "System Name", "System Classification");
                result = ReadParameterValue(res?.Parameter) ?? "SISTEMA";
            }
            catch { result = "SISTEMA"; }

            _sistemaCache[e.Id] = result;
            return result;
        }
        //METODO OBTENER SISTEMA 
        private string GetMaterialHvac(Element e)
        {
            // CACHÉ: Si ya calculamos el material para este tipo, lo devolvemos directo
            ElementId typeId = e.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId && _materialCache.TryGetValue(typeId, out string cached))
                return cached;

            string result = "";

            // HERENCIA INTELIGENTE: Si es una conexión o accesorio, heredamos de la tubería conectada
            if (e is FamilyInstance fi && fi.Category != null)
            {
                int catId = fi.Category.Id.IntegerValue;
                if (catId == (int)BuiltInCategory.OST_PipeFitting || catId == (int)BuiltInCategory.OST_PipeAccessory)
                {
                    try
                    {
                        var manager = fi.MEPModel?.ConnectorManager;
                        if (manager != null)
                        {
                            bool found = false;
                            int connLimit = 0;
                            foreach (Connector conn in manager.Connectors)
                            {
                                if (found || connLimit++ >= 3) break;
                                foreach (Connector refConn in conn.AllRefs)
                                {
                                    if (refConn.Owner is Pipe connectedPipe)
                                    {
                                        string pipeMat = GetMaterialHvacDirect(connectedPipe);
                                        if (!string.IsNullOrWhiteSpace(pipeMat) && !pipeMat.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                                        {
                                            result = MapMaterial(pipeMat);
                                            found = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // fallback al método directo si falla la herencia por conectores
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(result))
                result = MapMaterial(GetMaterialHvacDirect(e));

            // Guardar en caché
            if (typeId != null && typeId != ElementId.InvalidElementId)
                _materialCache[typeId] = result;

            return result;
        }

        private string GetMaterialHvacDirect(Element e)
        {
            Element type = e.Document.GetElement(e.GetTypeId());

            var mappedResolution = ResolveMappedParameter(e, "Material");
            string mappedValue = ReadParameterValue(mappedResolution?.Parameter);
            if (!string.IsNullOrWhiteSpace(mappedValue))
                return mappedValue;

            if (type != null)
            {
                // 1. Intentar parámetro real
                var p = type.LookupParameter("Material");

                if (p != null && p.HasValue)
                {
                    string val = p.AsValueString() ?? p.AsString();

                    // REGLA ESPECIAL: Si es acero, usamos el nombre del tipo
                    if (!string.IsNullOrWhiteSpace(val) && val.IndexOf("Acero", StringComparison.OrdinalIgnoreCase) >= 0)
                        return type.Name;

                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                // 2. Fallback ? Type.Name
                return type.Name;
            }

            TrackUnknownParameter(e, "Material", "Material");
            return "";
        }
        //METODO OBTENER MATERIAL - DETECTOR
        private string MapMaterial(string input)
        {
            var map = _config?.tuberia?.material_map;

            if (map != null)
            {
                foreach (var kv in map)
                {
                    if (input.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Value;
                }
            }

            return input;
        }
        //METODO DIAMETRO
        private string GetDiametroHvac(Element e)
        {
            var resolution = ResolveParameter(e, "Diameter", "Diameter");
            var p = resolution?.Parameter;
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return Math.Round(p.AsDouble() * 12) + "\"";
            if (p != null && p.HasValue)
                return p.AsValueString() ?? "";

            return "";
        }
        //GET DESCRIPCIÓN MECHANICAL EQUIPAMENT 
        private string GetDescripcionMechanical(Element e)
        {
            Element type = e.Document.GetElement(e.GetTypeId());

            var resolution = ResolveMappedParameter(e, "Description");
            string resolvedValue = ReadParameterValue(resolution?.Parameter);
            if (!string.IsNullOrWhiteSpace(resolvedValue))
                return resolvedValue;

            if (type != null)
            {
                // intenta description (si existe)
                var p = type.LookupParameter("Description");

                if (p != null && p.HasValue)
                {
                    string val = p.AsValueString() ?? p.AsString();

                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                // tare nombre de familia 
                return type.Name;
            }

            TrackUnknownParameter(e, "Description", "Description");
            return "";
        }
        //Marca Mechanical equipament
        private string GetBrandMechanical(Element e)
        {
            var resolution = ResolveParameter(e, "Brand", "Manufacturer", "Brand");
            return ReadParameterValue(resolution?.Parameter) ?? "";
        }
        //Modelo Mecanical equipament 
        private string GetModelMechanical(Element e)
        {
            var resolution = ResolveParameter(e, "Model", "Model");
            return ReadParameterValue(resolution?.Parameter) ?? "";
        }
        //DETECTOR METODO MECHANICAL EQUIPAMENT 
        private bool EsMechanicalEquipment(Element e)
        {
            if (e.Category == null) return false;

            BuiltInCategory bic = (BuiltInCategory)e.Category.Id.IntegerValue;

            return bic == BuiltInCategory.OST_MechanicalEquipment;
        }
        //OBTENER PARAMETRO
        private string GetParam(Element e, string name)
        {
            if (string.Equals(name, "Insulation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Size", StringComparison.OrdinalIgnoreCase))
            {
                var resolution = ResolveParameter(e, name, name);
                return ReadParameterValue(resolution?.Parameter) ?? "";
            }

            var p = e.LookupParameter(name);
            return p?.AsString() ?? p?.AsValueString() ?? "";
        }

        private ParameterResolution ResolveParameter(Element element, string semanticField, params string[] defaultParameterNames)
        {
            if (_learningEnabled &&
                _learningSystem != null &&
                _learningSystem.TryResolveParameter(element, semanticField, out var resolution, defaultParameterNames))
            {
                return resolution;
            }

            if (TryFindParameterWithoutTracking(element, out var fallbackResolution, defaultParameterNames))
                return fallbackResolution;

            return null;
        }

        private ParameterResolution ResolveMappedParameter(Element element, string semanticField)
        {
            if (_learningEnabled &&
                _learningSystem != null &&
                _learningSystem.TryResolveMappedParameter(element, semanticField, out var resolution))
            {
                return resolution;
            }

            return null;
        }

        private void TrackUnknownParameter(Element element, string semanticField, params string[] defaultParameterNames)
        {
            if (_learningEnabled)
                _learningSystem?.TryResolveParameter(element, semanticField, out _, defaultParameterNames);
        }

        private string GetLevelInfo(Element e, out Level levelObj)
        {
            levelObj = null;
            Document doc = e.Document;
            ElementId levelId = e.LevelId;

            if (levelId == ElementId.InvalidElementId)
            {
                Parameter p = e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                if (p != null && p.HasValue) levelId = p.AsElementId();
            }

            if (levelId != ElementId.InvalidElementId)
            {
                levelObj = doc.GetElement(levelId) as Level;
                if (levelObj != null) return levelObj.Name;
            }

            return "SIN NIVEL";
        }

        private void WriteItemRow(Excel.Worksheet xlWorksheet, ref int row, ref int itemNum, string itemData)
        {
            string[] parts = itemData.Split('|');
            string desc = parts[0];
            string unidad = parts.Length > 1 ? parts[1] : "pza";

            xlWorksheet.Cells[row, 1] = itemNum++;
            var cellDesc = (Excel.Range)xlWorksheet.Cells[row, 2];
            cellDesc.Value = desc;
            xlWorksheet.Cells[row, 3] = unidad;

            HighlightNoData(cellDesc, desc);
            row++;
        }

        private static string ReadParameterValue(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
                return null;

            return parameter.AsString() ?? parameter.AsValueString();
        }

        private static bool TryFindParameterWithoutTracking(
            Element element,
            out ParameterResolution resolution,
            params string[] defaultParameterNames)
        {
            resolution = null;
            if (element == null)
                return false;

            foreach (string defaultParameterName in defaultParameterNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                Parameter parameter = element.LookupParameter(defaultParameterName);
                if (parameter == null)
                {
                    var type = element.Document?.GetElement(element.GetTypeId());
                    parameter = type?.LookupParameter(defaultParameterName);
                }

                if (parameter == null)
                    continue;

                resolution = new ParameterResolution
                {
                    Parameter = parameter,
                    ParameterName = defaultParameterName,
                    ComesFromLearningMap = false
                };
                return true;
            }

            return false;
        }
        private string RemoveAccents(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return text
                .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                .Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U")
                .Replace("ñ", "n").Replace("Ñ", "N");
        }
        private string ClasificarSistemaGeneral(string sistemaRaw)
        {
            if (string.IsNullOrWhiteSpace(sistemaRaw)) return "SIN SISTEMA";
            string s = sistemaRaw.ToUpper();

            if (s.Contains("SAN") || s.Contains("DRENAJE") || s.Contains("DESAGÜE") || s.Contains("DESAGUE") || s.Contains("REVENTILACION") || s.Contains("REVENTILACIÓN"))
                return "SANITARIO";
            if (s.Contains("APROVECHAMIENTO") || s.Contains("TRATADA"))
                return "APROVECHAMIENTO PLUVIAL (AGUA TRATADA)";
            if (s.Contains("PLUVIAL") || s.Contains("LLUVIA"))
                return "PLUVIAL";
            if (s.Contains("PCI") || s.Contains("RCI") || s.Contains("FUEGO") || s.Contains("INCENDIO"))
                return "PCI";
            if (s.Contains("GAS"))
                return "GAS";
            if (s.Contains("HIDRÁULIC") || s.Contains("HIDRAULIC") || s.Contains("AGUA") || s.Contains("SUMINISTRO") || s.Contains("DOMESTICA"))
                return "HIDRAULICO";

            return sistemaRaw; // Fallback al nombre original si no coincide
        }
        private static void ReleaseComObjectSafe(object obj)
        {
            if (obj == null) return;
            try { Marshal.ReleaseComObject(obj); } catch { }
        }

    } // Fin de GenerateDescriptions
}