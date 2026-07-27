// =======================================================================1
// PLUGIN REVIT: Exportar Especificaciones de Iluminaria a Excel
// =======================================================================
// Descripción:
//   Comando que se ejecuta al presionar el botón "ESP.ILU" en la cinta
//   de Revit. Recorre todos los TIPOS de luminarias (Lighting Fixtures)
//   del modelo activo, extrae sus parámetros y los escribe en la
//   plantilla Excel de especificaciones técnicas (ESPE_ILU.xlsx), que
//   el plugin busca automáticamente junto a su propio .dll.
//
//   Al finalizar, solo se le pide al usuario dónde guardar el resultado
//   (no se pide seleccionar la plantilla: eso es automático).

// Versión:  3.0 (migrado a Excel Interop)
// =======================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Excel = Microsoft.Office.Interop.Excel;

namespace MiNamespace
{
    /// <summary>
    /// Comando externo de Revit que exporta las especificaciones técnicas
    /// de todas las luminarias del modelo a la plantilla Excel ESPE_ILU.xlsx.
    /// Se registra en el botón "ESP.ILU" del panel de la cinta.
    /// </summary>
    [Transaction(TransactionMode.Manual)] // No modifica el modelo, pero Manual es requerido por la API
    public class MyTAGS_ESP_ILU : IExternalCommand
    {
        // ---------------------------------------------------------------
        // CONFIGURACIÓN DE LA PLANTILLA
        // ---------------------------------------------------------------
        private const string TEMPLATE_FILE_NAME = "ESPE.ILU.xlsx";
        private const string SHEET_NAME = "2. Especificaciones";

        /// <summary>
        /// Fila donde comienzan los datos en "2. Especificaciones".
        /// Filas 1-6: encabezado del proyecto. Filas 7-8: título de
        /// columnas (a dos líneas). Fila 9 en adelante: una luminaria por fila.
        /// </summary>
        private const int DATA_START_ROW = 9;

        // ---------------------------------------------------------------
        // MÉTODO PRINCIPAL: Execute
        // ---------------------------------------------------------------
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            Excel.Application xl = null;
            Excel.Workbook wb = null;
            Excel.Worksheet ws = null;

            try
            {
                // ── PASO 1: Localizar la plantilla automáticamente ──────────
                string templatePath = LocalizarPlantilla();
                if (string.IsNullOrEmpty(templatePath))
                {
                    TaskDialog.Show("ESP.ILU",
                        $"No se encontró la plantilla '{TEMPLATE_FILE_NAME}'.\n\n" +
                        "Debe estar ubicada en la misma carpeta que el archivo .dll del plugin " +
                        "(junto al .addin).");
                    return Result.Failed;
                }

                // ── PASO 2: Preguntar dónde guardar el resultado ────────────
                string outputPath = SeleccionarSalida(templatePath, doc.Title);
                if (string.IsNullOrEmpty(outputPath))
                    return Result.Cancelled;

                // ── PASO 3: Copiar la plantilla al destino ──────────────────
                // Nunca se modifica la plantilla original; se trabaja sobre la copia.
                File.Copy(templatePath, outputPath, overwrite: true);

                // ── PASO 4: Recopilar luminarias del modelo ─────────────────
                List<LuminariaData> luminarias = ObtenerLuminarias(doc);

                if (luminarias.Count == 0)
                {
                    TaskDialog.Show("ESP.ILU",
                        "No se encontraron luminarias (Lighting Fixtures) en el modelo.");
                    return Result.Succeeded;
                }

                // ── PASO 5: Escribir los datos en el Excel (Interop) ────────
                xl = new Excel.Application { Visible = false, DisplayAlerts = false };
                wb = xl.Workbooks.Open(outputPath);

                try
                {
                    ws = (Excel.Worksheet)wb.Sheets[SHEET_NAME];
                }
                catch
                {
                    TaskDialog.Show("ESP.ILU",
                        $"No se encontró la hoja '{SHEET_NAME}' en la plantilla.");
                    wb.Close(false);
                    return Result.Failed;
                }

                // Limpiar datos anteriores (NO borra el formato)
                Excel.Range rangoDatos = ws.Range["A9:W500"];
                rangoDatos.ClearContents();
                System.Runtime.InteropServices.Marshal.ReleaseComObject(rangoDatos);

                ActualizarFechaExportacion(ws);
                ActualizarDatosProyecto(doc, wb, ws);


                // La fila 9 es la plantilla
                Excel.Range filaModelo = (Excel.Range)ws.Rows[DATA_START_ROW];

                int row = DATA_START_ROW;

                foreach (var lum in luminarias)
                {
                    // Copiar el formato únicamente para las filas nuevas
                    if (row > DATA_START_ROW)
                    {
                        Excel.Range filaDestino = (Excel.Range)ws.Rows[row];
                        filaModelo.Copy();
                        filaDestino.PasteSpecial(Excel.XlPasteType.xlPasteFormats);
                        filaDestino.PasteSpecial(Excel.XlPasteType.xlPasteColumnWidths);
                        xl.CutCopyMode = Excel.XlCutCopyMode.xlCopy;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(filaDestino);
                    }

                    // ============================
                    // ESCRIBIR DATOS
                    // ============================
                    ((Excel.Range)ws.Cells[row, 1]).Value2 = lum.CodigoLuminaria;
                    ((Excel.Range)ws.Cells[row, 2]).Value2 = lum.CodigoProveedor;
                    ((Excel.Range)ws.Cells[row, 4]).Value2 = lum.ColorAcabado;
                    ((Excel.Range)ws.Cells[row, 5]).Value2 = lum.Descripcion;
                    ((Excel.Range)ws.Cells[row, 6]).Value2 = lum.Tecnologia;
                    ((Excel.Range)ws.Cells[row, 7]).Value2 = lum.FlujoLuminoso;
                    ((Excel.Range)ws.Cells[row, 8]).Value2 = lum.Potencia;
                    ((Excel.Range)ws.Cells[row, 10]).Value2 = lum.TensionInstalacion;
                    ((Excel.Range)ws.Cells[row, 11]).Value2 = lum.TemperaturaColor;
                    ((Excel.Range)ws.Cells[row, 12]).Value2 = lum.FactorPotencia;
                    ((Excel.Range)ws.Cells[row, 13]).Value2 = lum.ProteccionIP;
                    ((Excel.Range)ws.Cells[row, 14]).Value2 = lum.ProteccionIK;
                    ((Excel.Range)ws.Cells[row, 15]).Value2 = lum.VidaUtil;
                    ((Excel.Range)ws.Cells[row, 16]).Value2 = lum.Control;
                    ((Excel.Range)ws.Cells[row, 17]).Value2 = lum.Dimensiones;
                    ((Excel.Range)ws.Cells[row, 18]).Value2 = lum.Instalacion;
                    ((Excel.Range)ws.Cells[row, 19]).Value2 = lum.Observaciones;
                    ((Excel.Range)ws.Cells[row, 20]).Value2 = lum.Ubicacion;

                    // ============================
                    // EFICACIA (Fórmula incondicional)
                    // ============================
                    Excel.Range cellI = (Excel.Range)ws.Cells[row, 9];
                    // Flujo Luminoso (G) / Potencia (H)
                    cellI.Formula = $"=IF(H{row}=0,\"\",G{row}/H{row})";
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(cellI);

                    // Mantener la altura de la plantilla
                    Excel.Range fila = (Excel.Range)ws.Rows[row];
                    fila.RowHeight = filaModelo.RowHeight;
                    fila.WrapText = true;
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(fila);

                    row++;
                }

                System.Runtime.InteropServices.Marshal.ReleaseComObject(filaModelo);

                wb.Save();

                // ── PASO 6: Notificar éxito ──────────────────────────────────
                TaskDialog.Show("ESP.ILU",
                    $"Exportación completada.\n\n" +
                    $"Luminarias exportadas: {luminarias.Count}\n" +
                    $"Archivo guardado en:\n{outputPath}");

                AbrirArchivoExcel(outputPath);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("ESP.ILU - Error", ex.ToString());
                return Result.Failed;
            }
            finally
            {
                // Cerrar y liberar Excel SIEMPRE, incluso si algo falló,
                // para no dejar procesos EXCEL.EXE colgados en segundo plano.
                if (wb != null)
                {
                    try { wb.Close(false); } catch { }
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
                }
                if (ws != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(ws);
                if (xl != null)
                {
                    try { xl.Quit(); } catch { }
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // ---------------------------------------------------------------
        // MÉTODO: ObtenerLuminarias
        // ---------------------------------------------------------------
        private List<LuminariaData> ObtenerLuminarias(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsElementType();

            var instanciasPorTipo = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .GroupBy(fi => fi.GetTypeId())
                .ToDictionary(g => g.Key, g => g.First());

            var luminarias = new List<LuminariaData>();
            foreach (ElementType type in collector.Cast<ElementType>())
            {
                LuminariaData lum = new LuminariaData();

                instanciasPorTipo.TryGetValue(type.Id, out FamilyInstance instanciaRep);

                //===========================
                // IDENTIFICACIÓN
                //===========================

                // Primero intenta leer Lamp
                lum.CodigoLuminaria =
                    ObtenerParam(type, "Lamp");

                // Si Lamp está vacío, intenta Type Mark
                if (string.IsNullOrWhiteSpace(lum.CodigoLuminaria))
                {
                    lum.CodigoLuminaria =
                        ObtenerParamBuiltIn(
                            type,
                            BuiltInParameter.ALL_MODEL_TYPE_MARK);
                }

                // Si sigue vacío usa el nombre de familia
                if (string.IsNullOrWhiteSpace(lum.CodigoLuminaria))
                {
                    lum.CodigoLuminaria = type.FamilyName;
                }

                //===========================
                // CÓDIGO PROVEEDOR
                //===========================

                string modelo =
                    ObtenerParamBuiltIn(type, BuiltInParameter.ALL_MODEL_MODEL);

                string fabricante =
                    ObtenerParamBuiltIn(type, BuiltInParameter.ALL_MODEL_MANUFACTURER);

                if (!string.IsNullOrWhiteSpace(modelo) &&
                    !string.IsNullOrWhiteSpace(fabricante))
                {
                    lum.CodigoProveedor = modelo + Environment.NewLine + fabricante;
                }
                else if (!string.IsNullOrWhiteSpace(modelo))
                {
                    lum.CodigoProveedor = modelo;
                }
                else
                {
                    lum.CodigoProveedor = fabricante;
                }

                //===========================
                // DESCRIPCIÓNooooooo
                //===========================

                lum.Descripcion = type.FamilyName;
                if (!string.IsNullOrWhiteSpace(lum.Descripcion))
                {
                    // Remover prefijo tipo "LUF-XX", "LUF-56", etc. (incluyendo guiones o espacios posteriores)
                    lum.Descripcion = System.Text.RegularExpressions.Regex.Replace(
                        lum.Descripcion, 
                        @"^LUF-[A-Za-z0-9]+\s*-?\s*", 
                        "", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                }

                // Fallback por si acaso
                if (string.IsNullOrWhiteSpace(lum.Descripcion))
                    lum.Descripcion = type.Name;

                //===========================
                // ACABADO
                //===========================

                lum.ColorAcabado =
                    ObtenerParam(type, "Cuerpo");

                if (string.IsNullOrWhiteSpace(lum.ColorAcabado))
                    lum.ColorAcabado =
                        ObtenerParam(type, "Marco");

                //===========================
                // TECNOLOGÍA
                //===========================

                lum.Tecnologia = ObtenerParam(type, "DC_Tecnología");
                if (string.IsNullOrWhiteSpace(lum.Tecnologia))
                    lum.Tecnologia = ObtenerParam(type, "DC_Tecnologia");
                if (string.IsNullOrWhiteSpace(lum.Tecnologia))
                    lum.Tecnologia = "LED";

                //===========================
                // DATOS ELÉCTRICOS
                //===========================

                // Flujo Luminoso (con múltiples fallbacks y parámetro nativo)
                lum.FlujoLuminoso = ObtenerParamDouble(type, "DC_Flujo Luminico");
                if (lum.FlujoLuminoso == 0)
                    lum.FlujoLuminoso = ObtenerParamDouble(type, "DC_Flujo Lumínico");
                if (lum.FlujoLuminoso == 0)
                    lum.FlujoLuminoso = ObtenerParamDouble(type, "Initial Intensity");
                if (lum.FlujoLuminoso == 0)
                    lum.FlujoLuminoso = ObtenerParamDouble(type, "Luminous Flux");
                if (lum.FlujoLuminoso == 0)
                    lum.FlujoLuminoso = ObtenerParamDouble(type, "Flujo Luminoso");
                if (lum.FlujoLuminoso == 0)
                    lum.FlujoLuminoso = ObtenerParamDoubleBuiltIn(type, BuiltInParameter.FBX_LIGHT_LIMUNOUS_FLUX);

                // Potencia (corregido fallback erróneo a Initial Intensity, buscando vatios reales)
                lum.Potencia = ObtenerParamDouble(type, "DC_Potencia");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "DC_Potencia Lumínica");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "DC_Potencia Luminica");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "Wattage");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "Initial Wattage");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "Watts");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDouble(type, "Potencia");
                if (lum.Potencia == 0)
                    lum.Potencia = ObtenerParamDoubleBuiltIn(type, BuiltInParameter.FBX_LIGHT_WATTAGE);

                lum.TensionInstalacion =
                    ObtenerParam(type, "DC_Tensión de instalación");

                lum.TemperaturaColor = ObtenerParam(type, "Initial Color Temperature");

                if (string.IsNullOrWhiteSpace(lum.TemperaturaColor))
                    lum.TemperaturaColor = ObtenerParam(type, "Initial Color");

                if (string.IsNullOrWhiteSpace(lum.TemperaturaColor))
                    lum.TemperaturaColor = ObtenerParam(type, "DC_Temperatura de color");

                //===========================
                // FACTOR DE POTENCIA
                //===========================

                lum.FactorPotencia = ObtenerParam(type, "DC_Factor de Potencia");

                if (string.IsNullOrWhiteSpace(lum.FactorPotencia))
                    lum.FactorPotencia = ObtenerParam(type, "DC_FactorPotencia");

                if (string.IsNullOrWhiteSpace(lum.FactorPotencia))
                    lum.FactorPotencia = ObtenerParam(type, "DC_FACTOR DE POTENCIA");

                lum.ProteccionIP =
                    ObtenerParam(type, "DC_IP");

                lum.ProteccionIK =
                    ObtenerParam(type, "DC_IK");

                lum.VidaUtil =
                    ObtenerParam(type, "DC_Vida Útil");

                //===========================
                // INFORMACIÓN TÉCNICA
                //===========================

                lum.Control =
                    ObtenerParam(type, "DC_Control");

                lum.Dimensiones =
                    ObtenerParam(type, "DC_Dimensiones");

                lum.Instalacion =
                    ObtenerParam(type, "DC_Tipo de Montaje");

                //===========================
                // OBSERVACIONES
                //===========================

                lum.Observaciones =
                    ObtenerParamBuiltIn(
                        type,
                        BuiltInParameter.ALL_MODEL_DESCRIPTION);

                //===========================
                // UBICACIÓN 
                //===========================

                lum.Ubicacion = "";

                if (instanciaRep != null)
                {
                    lum.Ubicacion = ObtenerParam(instanciaRep, "Schedule Level");

                    if (string.IsNullOrWhiteSpace(lum.Ubicacion))
                        lum.Ubicacion = ObtenerParamBuiltIn(instanciaRep, BuiltInParameter.LEVEL_PARAM);

                    if (string.IsNullOrWhiteSpace(lum.Ubicacion))
                        lum.Ubicacion = ObtenerParamBuiltIn(instanciaRep, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                }

                //===========================

                luminarias.Add(lum);
            }

            return luminarias
                .OrderBy(x => x.CodigoLuminaria)
                .ToList();
        }

        // ---------------------------------------------------------------
        // HELPER: ObtenerParam (texto, por nombre)
        // ---------------------------------------------------------------
        private string ObtenerParam(Element elem, string paramName)
        {
            if (elem == null || string.IsNullOrWhiteSpace(paramName))
                return "";

            Parameter p = elem.LookupParameter(paramName);

            if (p == null || !p.HasValue)
                return "";

            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? "";

                case StorageType.Double:

                    // Devuelve exactamente lo que muestra Revit
                    string valorDouble = p.AsValueString();

                    if (!string.IsNullOrWhiteSpace(valorDouble))
                        return valorDouble;

                    return p.AsDouble().ToString();

                case StorageType.Integer:

                    string valorInt = p.AsValueString();

                    if (!string.IsNullOrWhiteSpace(valorInt))
                        return valorInt;

                    return p.AsInteger().ToString();

                case StorageType.ElementId:

                    ElementId id = p.AsElementId();

                    if (id != ElementId.InvalidElementId)
                    {
                        Element e = elem.Document.GetElement(id);

                        if (e != null)
                            return e.Name;
                    }

                    return "";

                default:
                    return p.AsValueString() ?? "";
            }
        }

        // ---------------------------------------------------------------
        // HELPER: ObtenerParamBuiltIn (texto, por BuiltInParameter)
        // ---------------------------------------------------------------
        private string ObtenerParamBuiltIn(Element elem, BuiltInParameter bip)
        {
            if (elem == null)
                return "";

            Parameter p = elem.get_Parameter(bip);

            if (p == null || !p.HasValue)
                return "";

            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? "";

                case StorageType.Double:
                    return p.AsValueString() ?? "";

                case StorageType.Integer:
                    return p.AsValueString() ?? "";

                case StorageType.ElementId:

                    ElementId id = p.AsElementId();

                    if (id != ElementId.InvalidElementId)
                    {
                        Element e = elem.Document.GetElement(id);

                        if (e != null)
                            return e.Name;
                    }

                    return "";

                default:
                    return p.AsValueString() ?? "";
            }
        }

        // ---------------------------------------------------------------
        // HELPER: ObtenerParamDouble (número)
        // ---------------------------------------------------------------
        private double ObtenerParamDouble(Element elem, string paramName)
        {
            Parameter p = elem.LookupParameter(paramName);

            if (p == null || !p.HasValue)
                return 0;

            // Primero intentamos leer el valor mostrado por Revit
            string texto = p.AsValueString();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                texto = texto
                    .Replace("W", "")
                    .Replace("lm", "")
                    .Replace("V", "")
                    .Replace("K", "")
                    .Replace("h", "")
                    .Replace("s", "")
                    .Trim();

                // Cambiar coma por punto para evitar problemas de cultura
                texto = texto.Replace(",", ".");

                if (double.TryParse(
                    texto,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double valor))
                {
                    return valor;
                }
            }

            // Si es un parámetro de texto
            if (p.StorageType == StorageType.String)
            {
                texto = p.AsString();

                if (!string.IsNullOrWhiteSpace(texto))
                {
                    texto = texto
                        .Replace("W", "")
                        .Replace("lm", "")
                        .Replace("V", "")
                        .Replace("K", "")
                        .Replace("h", "")
                        .Replace("s", "")
                        .Trim();

                    texto = texto.Replace(",", ".");

                    if (double.TryParse(
                        texto,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double valor))
                    {
                        return valor;
                    }
                }
            }

            return 0;
        }

        // ---------------------------------------------------------------
        // HELPER: ObtenerParamDoubleBuiltIn (número por BuiltInParameter)
        // ---------------------------------------------------------------
        private double ObtenerParamDoubleBuiltIn(Element elem, BuiltInParameter bip)
        {
            if (elem == null)
                return 0;

            Parameter p = elem.get_Parameter(bip);

            if (p == null || !p.HasValue)
                return 0;

            // Primero intentamos leer el valor mostrado por Revit
            string texto = p.AsValueString();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                texto = texto
                    .Replace("W", "")
                    .Replace("lm", "")
                    .Replace("V", "")
                    .Replace("K", "")
                    .Replace("h", "")
                    .Replace("s", "")
                    .Trim()
                    .Replace(",", ".");

                if (double.TryParse(
                    texto,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double valor))
                {
                    return valor;
                }
            }

            if (p.StorageType == StorageType.Double)
                return p.AsDouble();
            if (p.StorageType == StorageType.Integer)
                return p.AsInteger();

            return 0;
        }

        // ---------------------------------------------------------------
        // MÉTODO: LocalizarPlantilla
        // ---------------------------------------------------------------
        private string LocalizarPlantilla()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDir)) return null;

            // Buscar en:
            // 1. Subcarpeta 'Resources' al lado de la DLL (comportamiento estándar en producción)
            // 2. Directamente en la misma carpeta que la DLL
            // 3. Ruta relativa al directorio de desarrollo en la solución
            string[] directoriosCandidatos = new string[]
            {
                Path.Combine(pluginDir, "Resources"),
                pluginDir,
                Path.GetFullPath(Path.Combine(pluginDir, @"..\..\..\..\Resources"))
            };

            foreach (string dir in directoriosCandidatos)
            {
                try
                {
                    string candidato = Path.Combine(dir, TEMPLATE_FILE_NAME);
                    if (File.Exists(candidato))
                    {
                        return candidato;
                    }
                }
                catch
                {
                    // Ignorar errores de ruta inválida al resolver paths de desarrollo
                }
            }

            return null;
        }

        // ---------------------------------------------------------------
        // DIÁLOGO: SeleccionarSalida
        // ---------------------------------------------------------------
        private string SeleccionarSalida(string templatePath, string nombreProyecto)
        {
            using (var dlg = new SaveFileDialog())
            {
                string proyectoLimpio = string.Join("_", nombreProyecto.Split(Path.GetInvalidFileNameChars()));
                dlg.Title = "Guardar especificaciones de iluminaria como...";
                dlg.Filter = "Excel (*.xlsx)|*.xlsx";
                dlg.FileName = $"ESP-ILU-{proyectoLimpio}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
                dlg.InitialDirectory = Path.GetDirectoryName(templatePath);
                return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
            }
        }

        // ---------------------------------------------------------------
        // MÉTODO: AbrirArchivoExcel
        // ---------------------------------------------------------------
        private void AbrirArchivoExcel(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true, // Necesario para que abra con la app predeterminada (Excel)
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ESP.ILU",
                    "El archivo se generó correctamente, pero no se pudo abrir automáticamente.\n\n" +
                    $"Detalle: {ex.Message}");
            }
        }


        // ---------------------------------------------------------------
        // MÉTODO: ActualizarDatosProyecto
        // ---------------------------------------------------------------
        private void ActualizarDatosProyecto(Document doc, Excel.Workbook wb, Excel.Worksheet wsEspecificaciones)
        {
            try
            {
                if (doc.ProjectInformation == null) return;

                // 1. Obtener parámetros de Project Information
                string nombreProyecto = ObtenerParam(doc.ProjectInformation, "DC - Nombre proyecto");
                if (string.IsNullOrWhiteSpace(nombreProyecto)) nombreProyecto = ObtenerParam(doc.ProjectInformation, "DC - Nombre proyecto");
                if (string.IsNullOrWhiteSpace(nombreProyecto)) nombreProyecto = ObtenerParam(doc.ProjectInformation, "Nombre proyecto");
                if (string.IsNullOrWhiteSpace(nombreProyecto)) nombreProyecto = ObtenerParamBuiltIn(doc.ProjectInformation, BuiltInParameter.PROJECT_NAME);
                
                // Si aún es nulo, buscar ignorando mayúsculas/minúsculas
                if (string.IsNullOrWhiteSpace(nombreProyecto))
                {
                    foreach (Parameter p in doc.ProjectInformation.Parameters)
                    {
                        string pName = p.Definition.Name.ToLower();
                        if (pName.Contains("nombre proyecto") || pName.Contains("nombre del proyecto"))
                        {
                            if (p.HasValue && !string.IsNullOrWhiteSpace(p.AsString()))
                            {
                                nombreProyecto = p.AsString();
                                break;
                            }
                        }
                    }
                }
                
                string disciplina = "ILUMINACION";
                
                string etapa = ObtenerParam(doc.ProjectInformation, "DC_Etapa de diseño");
                if (string.IsNullOrWhiteSpace(etapa)) etapa = ObtenerParam(doc.ProjectInformation, "DC_Etapa de siseño");

                string disenador = ObtenerParam(doc.ProjectInformation, "DC_Diseñador");
                string director = ObtenerParam(doc.ProjectInformation, "DC_Director del proyecto");
                string coordinador = ObtenerParam(doc.ProjectInformation, "DC_Coordinador del proyecto");
                
                string version = ObtenerParam(doc.ProjectInformation, "DC_Version de modelo");
                if (string.IsNullOrWhiteSpace(version)) version = ObtenerParam(doc.ProjectInformation, "DC_Versión de modelo");
                if (string.IsNullOrWhiteSpace(version)) version = ObtenerParam(doc.ProjectInformation, "Versión");

                // 2. Actualizar en "2. Especificaciones" (wsEspecificaciones)
                Excel.Range rangoEncabezado2 = wsEspecificaciones.Range["A1:Z10"];
                EscribirDatoAdyacente(wsEspecificaciones, rangoEncabezado2, "PROYECTO", nombreProyecto);
                EscribirDatoAdyacente(wsEspecificaciones, rangoEncabezado2, "DISCIPLINA", disciplina);
                EscribirDatoAdyacente(wsEspecificaciones, rangoEncabezado2, "ETAPA", etapa);
                
                // Si existe Versión en la hoja 2
                if (!string.IsNullOrWhiteSpace(version))
                {
                    Excel.Range celdaVersion2 = rangoEncabezado2.Find("Versi", LookIn: Excel.XlFindLookIn.xlValues, LookAt: Excel.XlLookAt.xlPart);
                    if (celdaVersion2 != null)
                    {
                        celdaVersion2.Value2 = "Versión: " + version;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(celdaVersion2);
                    }
                }
                System.Runtime.InteropServices.Marshal.ReleaseComObject(rangoEncabezado2);

                // 3. Actualizar en "1. Presentación"
                Excel.Worksheet wsPresentacion = null;
                try
                {
                    wsPresentacion = (Excel.Worksheet)wb.Sheets["1. Presentación"];
                }
                catch { }

                if (wsPresentacion != null)
                {
                    // Encabezado superior para Proyecto y Versión
                    Excel.Range rangoTop = wsPresentacion.Range["A1:Z15"];
                    
                    if (!string.IsNullOrWhiteSpace(nombreProyecto))
                    {
                        Excel.Range celdaA8 = wsPresentacion.Range["A8"];
                        celdaA8.Value2 = nombreProyecto;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(celdaA8);
                    }

                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        Excel.Range celdaVersionTop = rangoTop.Find("Versi", LookIn: Excel.XlFindLookIn.xlValues, LookAt: Excel.XlLookAt.xlPart);
                        if (celdaVersionTop != null)
                        {
                            celdaVersionTop.Value2 = "Versión: " + version;
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(celdaVersionTop);
                        }
                    }

                    // Cuadro de Firmas
                    Excel.Range rangoFirmas = wsPresentacion.Range["A20:Z50"];
                    EscribirDatoAdyacente(wsPresentacion, rangoFirmas, "Director del proyecto", director);
                    EscribirDatoAdyacente(wsPresentacion, rangoFirmas, "Coordinador del proyecto", coordinador);
                    EscribirDatoAdyacente(wsPresentacion, rangoFirmas, "Elaborado por", disenador);

                    System.Runtime.InteropServices.Marshal.ReleaseComObject(rangoFirmas);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(rangoTop);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(wsPresentacion);
                }
            }
            catch
            {
                // Ignorar errores menores al actualizar encabezado
            }
        }

        private void EscribirDatoAdyacente(Excel.Worksheet ws, Excel.Range rangoBusqueda, string textoBuscado, string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return;
            
            Excel.Range celdaLabel = rangoBusqueda.Find(
                textoBuscado, LookIn: Excel.XlFindLookIn.xlValues, LookAt: Excel.XlLookAt.xlPart);
                
            if (celdaLabel != null)
            {
                int colDestino = celdaLabel.MergeArea.Column + celdaLabel.MergeArea.Columns.Count;
                Excel.Range destino = (Excel.Range)ws.Cells[celdaLabel.Row, colDestino];
                destino.Value2 = valor;
                System.Runtime.InteropServices.Marshal.ReleaseComObject(celdaLabel);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(destino);
            }
        }

        // ---------------------------------------------------------------
        // MÉTODO: ActualizarFechaExportacion
        // ---------------------------------------------------------------
        private void ActualizarFechaExportacion(Excel.Worksheet ws)
        {
            try
            {
                // Busca la celda que contiene "Fecha:" (con dos puntos) en el bloque
                // de control de documento (Cód. / Fecha: / Versión), NO el bloque
                // de encabezado del proyecto (PROYECTO / DISCIPLINA / ETAPA / FECHA)
                Excel.Range rangoEncabezado = ws.Range["A1:Z5"];

                Excel.Range celdaFecha = rangoEncabezado.Find(
                    "Fecha:",
                    LookIn: Excel.XlFindLookIn.xlValues,
                    LookAt: Excel.XlLookAt.xlPart); // xlPart: coincidencia parcial

                if (celdaFecha != null)
                {
                    string nuevaFecha = "Fecha: " + DateTime.Now.ToString("dd/MM/yyyy");
                    celdaFecha.Value2 = nuevaFecha;
                }

                if (celdaFecha != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(celdaFecha);

                System.Runtime.InteropServices.Marshal.ReleaseComObject(rangoEncabezado);
            }
            catch
            {
                // Si no se encuentra la celda, se omite (no es un error crítico)
            }
        }
    }
        // =======================================================================
        // CLASE: LuminariaData
        // =======================================================================
        public class LuminariaData
    {
        public string CodigoLuminaria { get; set; }
        public string CodigoProveedor { get; set; }
        public string ColorAcabado { get; set; }
        public string Descripcion { get; set; }
        public string Tecnologia { get; set; }
        public double FlujoLuminoso { get; set; }
        public double Potencia { get; set; }
        public string TensionInstalacion { get; set; }
        public string TemperaturaColor { get; set; }
        public string THD { get; set; }
        public string FactorPotencia { get; set; }
        public string ProteccionIP { get; set; }
        public string ProteccionIK { get; set; }
        public string VidaUtil { get; set; }
        public string Control { get; set; }
        public string Dimensiones { get; set; }
        public string Instalacion { get; set; }
        public string Observaciones { get; set; }
        public string Ubicacion { get; set; }
    }    
}

// =======================================================================
// NOTAS
// =======================================================================
// 1) Referencia necesaria en el .csproj (COM):
//      Agregar referencia a "Microsoft Excel XX.0 Object Library"
//      (o el paquete NuGet "Microsoft.Office.Interop.Excel"), igual que
//      ya tienes en MyCommandPreDim. Ya NO necesitas EPPlus para este
//      comando: puedes quitar el PackageReference de EPPlus si no lo usas
//      en otro lado del proyecto.
//
// 2) Requiere Excel instalado en la máquina donde corre Revit (Interop
//    depende de la instalación local de Office, no es "headless" como
//    EPPlus). Como tu PreDim ya usa este mismo patrón y funciona, esto
//    no debería ser un problema nuevo en tu entorno.
//
// 3) Multi-targeting 2020→2026 (framework de compilación):
//      Revit 2020-2024 → net48 (.NET Framework 4.8)
//      Revit 2025-2026 → net8.0-windows (.NET 8)
//    Con Interop.Excel esto sigue aplicando igual que con EPPlus; el
//    código de este archivo no cambia entre frameworks.
//
// 4) Si el error "Command Failure for External Command" persiste con
//    Interop, haz clic en "Show details" en el diálogo de Revit y
//    revisa el texto completo — ahí normalmente aparece el mensaje real
//    (ClassNotRegisteredException si Excel no está instalado o el GUID
//    de la librería COM no coincide con la versión de Office instalada).
// =======================================================================