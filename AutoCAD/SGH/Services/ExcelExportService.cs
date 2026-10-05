using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.SGH.Models;
using Excel = Microsoft.Office.Interop.Excel;
using AutoCAD.SGH.UI;

namespace AutoCAD.SGH.Services
{
    public static class ExcelExportService
    {
        private const string TEMPLATE_FILE_NAME = "SGH - Carga de ocupacion.xlsx";
        private const string SHEET_NAME = "OCUPACIÓN";
        private const int DATA_START_ROW = 4;

        /// <summary>
        /// Recopila todos los espacios SGH desde las referencias de bloque presentes en el dibujo actual.
        /// </summary>
        public static List<SghSpace> RecopilarEspacios(Database db)
        {
            var spaces = new List<SghSpace>();
            var visitedIds = new HashSet<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                // 1. Buscar en las definiciones de bloques SGH
                foreach (ObjectId btrId in bt)
                {
                    var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                    if (btr == null) continue;

                    if (SpaceService.IsSghBlock(btr.Name))
                    {
                        var blockRefIds = btr.GetBlockReferenceIds(true, true);
                        foreach (ObjectId refId in blockRefIds)
                        {
                            if (refId.IsErased || visitedIds.Contains(refId)) continue;
                            var blkRef = tr.GetObject(refId, OpenMode.ForRead) as BlockReference;
                            if (blkRef == null || blkRef.IsErased) continue;

                            visitedIds.Add(refId);
                            var space = SpaceService.ReadSpaceFromBlock(tr, blkRef);
                            if (space != null)
                            {
                                spaces.Add(space);
                            }
                        }
                    }
                }

                // 2. Revisar también el espacio modelo por si hay bloques dinámicos anónimos (*U...)
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId entId in modelSpace)
                {
                    if (entId.IsErased || visitedIds.Contains(entId)) continue;
                    if (tr.GetObject(entId, OpenMode.ForRead) is BlockReference blkRef)
                    {
                        string blkName = blkRef.Name;
                        if (blkRef.IsDynamicBlock)
                        {
                            var dynBtr = (BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead);
                            blkName = dynBtr.Name;
                        }

                        if (SpaceService.IsSghBlock(blkName))
                        {
                            visitedIds.Add(entId);
                            var space = SpaceService.ReadSpaceFromBlock(tr, blkRef);
                            if (space != null)
                            {
                                spaces.Add(space);
                            }
                        }
                    }
                }

                tr.Commit();
            }

            // Ordenar por Nivel (Piso) usando clave canónica y luego por Número de espacio numéricamente
            int ParseNum(string n) => int.TryParse(n?.Trim(), out int val) ? val : 999999;

            return (spaces ?? new List<SghSpace>())
                .Where(s => s != null)
                .OrderBy(s => OccupancyService.GetCanonicalPisoKey(s?.Piso))
                .ThenBy(s => ParseNum(s?.Numero))
                .ThenBy(s => s?.Numero ?? string.Empty)
                .ToList();
        }

        /// <summary>
        /// Localiza la plantilla Excel en las posibles rutas de distribución y desarrollo.
        /// </summary>
        public static string LocalizarPlantilla()
        {
            string dllDir = string.Empty;
            try
            {
                dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(dllDir))
            {
                // 1. Subcarpeta Resources junto a la DLL
                candidates.Add(Path.Combine(dllDir, "Resources", TEMPLATE_FILE_NAME));
                // 2. Directamente junto a la DLL
                candidates.Add(Path.Combine(dllDir, TEMPLATE_FILE_NAME));
                // 3. Carpeta de despliegue ApplicationPlugins bundle
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents\Resources", TEMPLATE_FILE_NAME));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents", TEMPLATE_FILE_NAME));
                // 4. Rutas relativas de solución en desarrollo
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\..\Resources", TEMPLATE_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\Resources", TEMPLATE_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\Resources", TEMPLATE_FILE_NAME)));
            }

            // 5. Ruta estándar fija de desarrollo del proyecto
            candidates.Add(Path.Combine(@"C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\Resources", TEMPLATE_FILE_NAME));

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Diálogo para que el usuario elija la ruta y nombre del archivo exportado.
        /// </summary>
        public static string SeleccionarSalida(string drawingName)
        {
            using (var dlg = new SaveFileDialog())
            {
                string baseName = "Dibujo";
                if (!string.IsNullOrWhiteSpace(drawingName))
                {
                    string fileNameOnly = Path.GetFileNameWithoutExtension(drawingName);
                    baseName = string.Join("_", fileNameOnly.Split(Path.GetInvalidFileNameChars()));
                }

                dlg.Title = "Guardar Carga de Ocupación SGH como...";
                dlg.Filter = "Libro de Excel (*.xlsx)|*.xlsx";
                dlg.FileName = $"SGH-CargaDeOcupacion-{baseName}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktop))
                {
                    dlg.InitialDirectory = desktop;
                }

                return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
            }
        }

        /// <summary>
        /// Ejecuta el proceso completo de exportación a Excel.
        /// </summary>
        public static bool Exportar(Database db, string drawingName, Editor ed)
        {
            // 1. Recopilar espacios del dibujo
            var spaces = RecopilarEspacios(db);
            if (spaces.Count == 0)
            {
                MessageBox.Show(
                    "No se encontraron bloques de área SGH (DICTA_SGH_TAG) en el dibujo actual.\n\n" +
                    "Use el comando 'SGHAREAS' o el botón 'Crear Área' para etiquetar los espacios antes de exportar.",
                    "SGH - Exportar a Excel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            SghReportModel model = null;
            while (true)
            {
                // 2. Desplegar diálogo de selección y actualización de normas Excel (NSR-10 / NFPA)
                var normsDialog = new NormsSelectionDialog();
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(normsDialog);

                if (!normsDialog.Confirmed)
                {
                    ed.WriteMessage("\n[SGH] Exportación cancelada por el usuario en el paso de selección de normas.");
                    return false;
                }

                // 3. Desplegar diálogo asistente de informe y exportación SGH (Portada, Parámetros y Conclusiones)
                var reportDialog = new SghReportDialog(spaces, drawingName);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(reportDialog);

                if (reportDialog.Confirmed)
                {
                    model = reportDialog.Model;
                    break;
                }

                // Si el usuario hace clic en Cancelar dentro del informe, regresa automáticamente a la pantalla de Selección de Normas
            }

            string outputPath = model.ExcelOutputPath;
            string wordPath = model.WordOutputPath;
            bool excelSuccess = false;
            bool wordSuccess = false;

            // Mostrar rueda de carga azul pequeña mientras se generan y abren los entregables
            SghLoadingWindow loadingWin = null;
            try
            {
                loadingWin = new SghLoadingWindow();
                loadingWin.Show();
                System.Windows.Forms.Application.DoEvents();

                // 4. Si se seleccionó exportar Excel
            if (model.GenerateExcel)
            {
                string templatePath = LocalizarPlantilla();
                if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                {
                    MessageBox.Show(
                        $"No se encontró el archivo de plantilla '{TEMPLATE_FILE_NAME}'.\n\n" +
                        "Verifique que la plantilla se encuentre en la carpeta 'Resources' del plugin.",
                        "SGH - Plantilla no encontrada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }

                dynamic xl = null;
                dynamic wb = null;
                dynamic ws = null;

                try
                {
                    // Copiar plantilla al destino (la plantilla original nunca se altera)
                    string outDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                    File.Copy(templatePath, outputPath, overwrite: true);

                    // Iniciar Excel mediante Late-Binding Dynamic para evitar errores de COM TypeLib QueryInterface
                    Type excelType = Type.GetTypeFromProgID("Excel.Application");
                    if (excelType == null)
                    {
                        MessageBox.Show(
                            "No se encontró Microsoft Excel instalado en esta computadora.",
                            "Error de Exportación SGH",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return false;
                    }

                    xl = Activator.CreateInstance(excelType);
                    xl.Visible = false;
                    xl.DisplayAlerts = false;

                    wb = xl.Workbooks.Open(outputPath);

                try
                {
                    ws = wb.Worksheets[SHEET_NAME];
                }
                catch
                {
                    ws = wb.Worksheets[1];
                }

                bool isMulti = OccupancyService.IsMultiNormMode;

                // 6. Si es Multinorma, insertar una nueva columna para Clasificación NFPA después de Ocupación NSR-10 (Col I)
                try
                {
                    if (isMulti)
                    {
                        dynamic colI = ws.Range["I:I"];
                        colI.Insert((int)Excel.XlInsertShiftDirection.xlShiftToRight);
                        if (colI != null) Marshal.ReleaseComObject((object)colI);

                        ws.Cells[3, 5].Value2 = "Clasificación de uso NSR 10";
                        ws.Cells[3, 6].Value2 = "Área (m2)";
                        ws.Cells[3, 7].Value2 = "Índice de ocupación por NSR 10";
                        ws.Cells[3, 8].Value2 = "Ocupación NSR-10";
                        ws.Cells[3, 9].Value2 = "Clasificación de uso NFPA";
                        ws.Cells[3, 10].Value2 = "Índice de ocupación por NFPA";
                        ws.Cells[3, 11].Value2 = "Ocupación NFPA";
                        ws.Cells[3, 12].Value2 = "Ancho puertas y pasillos";
                        ws.Cells[3, 13].Value2 = "Ancho escaleras";
                        ws.Cells[3, 14].Value2 = "Observaciones";
                    }
                    else if (OccupancyService.PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
                    {
                        ws.Cells[3, 5].Value2 = "Clasificación de uso NFPA";
                        ws.Cells[3, 7].Value2 = "Índice de ocupación por NFPA";
                        ws.Cells[3, 8].Value2 = "Ocupación NFPA";
                        ws.Cells[3, 9].Value2 = "—";
                        ws.Cells[3, 10].Value2 = "—";
                    }
                    else
                    {
                        ws.Cells[3, 5].Value2 = "Clasificación de uso NSR-10";
                        ws.Cells[3, 7].Value2 = "Índice de ocupación por NSR 10";
                        ws.Cells[3, 8].Value2 = "Ocupación NSR-10";
                        ws.Cells[3, 9].Value2 = "—";
                        ws.Cells[3, 10].Value2 = "—";
                    }
                }
                catch { }

                string lastColChar = isMulti ? "N" : "M";

                // Guardar copias de formato de la fila de datos y la fila de total
                dynamic backupDataRow = ws.Range[$"A199:{lastColChar}199"];
                dynamic backupTotalRow = ws.Range[$"A200:{lastColChar}200"];
                ws.Range[$"A4:{lastColChar}4"].Copy(backupDataRow);
                ws.Range[$"A14:{lastColChar}14"].Copy(backupTotalRow);

                // Limpiar cualquier columna sobrante a la derecha
                try
                {
                    string extraStartChar = isMulti ? "O" : "N";
                    dynamic extraCols = ws.Range[$"{extraStartChar}1:Z200"];
                    extraCols.Clear();
                    if (extraCols != null) Marshal.ReleaseComObject((object)extraCols);
                }
                catch { }

                // Descombinar y limpiar todo el rango de datos en la plantilla antes de procesar
                try
                {
                    dynamic fullRange = ws.Range[$"A4:{lastColChar}150"];
                    fullRange.UnMerge();
                    fullRange.ClearContents();
                    fullRange.ClearFormats();
                    if (fullRange != null) Marshal.ReleaseComObject((object)fullRange);
                }
                catch { }

                int totalSpaces = spaces.Count;

                // Agrupar espacios por Piso/Nivel usando clave canónica (n1, N1, Piso 1 se agrupan juntos)
                var floorGroups = spaces
                    .GroupBy(s => OccupancyService.GetCanonicalPisoKey(s.Piso))
                    .ToList();

                int currentRow = DATA_START_ROW;
                var subtotalAreaCells = new List<string>();
                var subtotalCoCells = new List<string>();
                var subtotalJCells = new List<string>();

                int ParseNumLocal(string n) => int.TryParse(n?.Trim(), out int val) ? val : 999999;

                foreach (var group in floorGroups)
                {
                    string pisoName = group.Key;
                    var groupSpaces = group
                        .OrderBy(s => ParseNumLocal(s.Numero))
                        .ThenBy(s => s.Numero ?? string.Empty)
                        .ToList();
                    int startRow = currentRow;

                    for (int i = 0; i < groupSpaces.Count; i++)
                    {
                        var sp = groupSpaces[i];
                        int r = currentRow;

                        backupDataRow.Copy();
                        dynamic targetDataRowRange = ws.Range[$"A{r}:{lastColChar}{r}"];
                        targetDataRowRange.PasteSpecial((int)Excel.XlPasteType.xlPasteFormats);
                        if (targetDataRowRange != null) Marshal.ReleaseComObject((object)targetDataRowRange);

                        string rawNsr = sp.GrupoOcupacionNsr ?? sp.GrupoOcupacion ?? "A";
                        string rawNfpa = !string.IsNullOrWhiteSpace(sp.GrupoOcupacionNfpa) ? sp.GrupoOcupacionNfpa : rawNsr;

                        string descUsoNsr = "";
                        string descUsoNfpa = "";
                        double? factorPrimary = null;
                        int? coPrimary = null;
                        double? factorSecondary = null;
                        int? coSecondary = null;

                        if (isMulti)
                        {
                            var gNsr = OccupancyService.NormsNSR10.Groups.FirstOrDefault(g =>
                                g.DisplayText.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                g.Code.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                g.Name.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                rawNsr.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase) ||
                                rawNsr.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase))
                                ?? OccupancyService.MatchGroup_NSR10(rawNsr);

                            var gNfpa = OccupancyService.NormsNFPA.Groups.FirstOrDefault(g =>
                                g.DisplayText.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                g.Name.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                g.Code.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                (rawNfpa.Length > 3 && rawNfpa.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                rawNfpa.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase) ||
                                rawNfpa.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase))
                                ?? OccupancyService.MatchGroup_NFPA(rawNfpa);

                            if (gNfpa == null && gNsr != null) gNfpa = OccupancyService.FindNfpaGroupForNsr(gNsr);

                            descUsoNsr = gNsr != null ? $"{gNsr.Code} - {gNsr.Name}" : OccupancyService.ExtractShortCode(rawNsr);
                            descUsoNfpa = gNfpa != null ? $"{gNfpa.Code} - {gNfpa.Name}" : OccupancyService.ExtractShortCode(rawNfpa);

                            factorPrimary = gNsr?.FactorM2PerPerson ?? OccupancyService.GetFactor_NSR10(rawNsr);
                            coPrimary = (int.TryParse(sp.CargaOcupacionNsr?.Trim(), out int vCo1) && vCo1 > 0)
                                ? vCo1
                                : (factorPrimary.HasValue && factorPrimary.Value > 0 ? (int?)Math.Max(1, (int)Math.Ceiling(sp.Area / factorPrimary.Value)) : OccupancyService.CalculateCO_NSR10(sp.Area, rawNsr));

                            factorSecondary = gNfpa?.FactorM2PerPerson ?? OccupancyService.GetFactor_NFPA(rawNfpa);
                            coSecondary = (int.TryParse(sp.CargaOcupacionNfpa?.Trim(), out int vCo2) && vCo2 > 0)
                                ? vCo2
                                : (factorSecondary.HasValue && factorSecondary.Value > 0 ? (int?)Math.Max(1, (int)Math.Ceiling(sp.Area / factorSecondary.Value)) : OccupancyService.CalculateCO_NFPA(sp.Area, rawNfpa));
                        }
                        else if (OccupancyService.PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
                        {
                            var gNfpa = OccupancyService.NormsNFPA.Groups.FirstOrDefault(g =>
                                g.DisplayText.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                g.Name.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                g.Code.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                (rawNfpa.Length > 3 && rawNfpa.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0))
                                ?? OccupancyService.MatchGroup_NFPA(rawNfpa);

                            descUsoNfpa = gNfpa != null ? $"{gNfpa.Code} - {gNfpa.Name}" : OccupancyService.ExtractShortCode(rawNfpa);

                            factorPrimary = gNfpa?.FactorM2PerPerson ?? OccupancyService.GetFactor_NFPA(rawNfpa);
                            coPrimary = (int.TryParse(sp.CargaOcupacionNfpa?.Trim(), out int vCo2) && vCo2 > 0)
                                ? vCo2
                                : (factorPrimary.HasValue && factorPrimary.Value > 0 ? (int?)Math.Max(1, (int)Math.Ceiling(sp.Area / factorPrimary.Value)) : OccupancyService.CalculateCO_NFPA(sp.Area, rawNfpa));
                        }
                        else
                        {
                            var gNsr = OccupancyService.NormsNSR10.Groups.FirstOrDefault(g =>
                                g.DisplayText.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                g.Code.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                g.Name.Equals(rawNsr, StringComparison.OrdinalIgnoreCase))
                                ?? OccupancyService.MatchGroup_NSR10(rawNsr);

                            descUsoNsr = gNsr != null ? $"{gNsr.Code} - {gNsr.Name}" : OccupancyService.ExtractShortCode(rawNsr);

                            factorPrimary = gNsr?.FactorM2PerPerson ?? OccupancyService.GetFactor_NSR10(rawNsr);
                            coPrimary = (int.TryParse(sp.CargaOcupacionNsr?.Trim(), out int vCo1) && vCo1 > 0)
                                ? vCo1
                                : (factorPrimary.HasValue && factorPrimary.Value > 0 ? (int?)Math.Max(1, (int)Math.Ceiling(sp.Area / factorPrimary.Value)) : OccupancyService.CalculateCO_NSR10(sp.Area, rawNsr));
                        }

                        // Anchos de pasillos y escaleras
                        double? pasillos = sp.AnchoPasillosMm;
                        double? escaleras = sp.AnchoEscalerasMm;
                        if (!pasillos.HasValue || !escaleras.HasValue)
                        {
                            var widths = OccupancyService.CalculateEgressWidths_Combined(coPrimary ?? 1, coSecondary ?? coPrimary ?? 1, rawNsr);
                            if (!pasillos.HasValue) pasillos = widths.AnchoCorredoresMm;
                            if (!escaleras.HasValue) escaleras = widths.AnchoEscalerasMm;
                        }

                        int exportCoNsr = (int.TryParse(sp.CargaOcupacionNsr?.Trim(), out int mCo1) && mCo1 > 0)
                            ? mCo1
                            : (coPrimary.HasValue && coPrimary.Value > 0 ? coPrimary.Value : 1);

                        int exportCoNfpa = (int.TryParse(sp.CargaOcupacionNfpa?.Trim(), out int mCo2) && mCo2 > 0)
                            ? mCo2
                            : (coSecondary.HasValue && coSecondary.Value > 0 ? coSecondary.Value : exportCoNsr);

                        if (isMulti)
                        {
                            // Col B (2): Piso
                            ws.Cells[r, 2].Value2 = pisoName;
                            // Col C (3): No.
                            ws.Cells[r, 3].Value2 = sp.Numero;
                            // Col D (4): Espacio
                            ws.Cells[r, 4].Value2 = sp.Espacio;
                            // Col E (5): Clasificación NSR-10
                            ws.Cells[r, 5].Value2 = descUsoNsr;
                            // Col F (6): Área (m2)
                            ws.Cells[r, 6].Value2 = Math.Round(sp.Area, 2);
                            // Col G (7): Índice NSR-10
                            ws.Cells[r, 7].Value2 = (factorPrimary.HasValue && factorPrimary.Value > 0) ? (object)factorPrimary.Value : "Manual";
                            // Col H (8): Ocupación NSR-10 (Con fórmula dinámica)
                            ws.Cells[r, 8].Formula = $"=IF(AND(ISNUMBER(G{r}),G{r}>0),ROUNDUP(F{r}/G{r},0),{exportCoNsr})";
                            // Col I (9): Clasificación NFPA
                            ws.Cells[r, 9].Value2 = descUsoNfpa;
                            // Col J (10): Índice NFPA
                            ws.Cells[r, 10].Value2 = (factorSecondary.HasValue && factorSecondary.Value > 0) ? (object)factorSecondary.Value : "Manual";
                            // Col K (11): Ocupación NFPA (Con fórmula dinámica)
                            ws.Cells[r, 11].Formula = $"=IF(AND(ISNUMBER(J{r}),J{r}>0),ROUNDUP(F{r}/J{r},0),{exportCoNfpa})";
                            // Col L (12): Pasillos
                            ws.Cells[r, 12].Value2 = pasillos.HasValue ? $"{pasillos.Value:0.#} mm" : "-";
                            // Col M (13): Escaleras
                            ws.Cells[r, 13].Value2 = escaleras.HasValue ? $"{escaleras.Value:0.#} mm" : "-";
                            // Col N (14): Observaciones
                            ws.Cells[r, 14].Value2 = string.Empty;
                        }
                        else
                        {
                            // Col B (2): Piso
                            ws.Cells[r, 2].Value2 = pisoName;
                            // Col C (3): No.
                            ws.Cells[r, 3].Value2 = sp.Numero;
                            // Col D (4): Espacio
                            ws.Cells[r, 4].Value2 = sp.Espacio;
                            // Col E (5): Clasificación de uso
                            ws.Cells[r, 5].Value2 = OccupancyService.PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase) ? descUsoNfpa : descUsoNsr;
                            // Col F (6): Área (m2)
                            ws.Cells[r, 6].Value2 = Math.Round(sp.Area, 2);
                            // Col G (7): Índice de ocupación
                            ws.Cells[r, 7].Value2 = (factorPrimary.HasValue && factorPrimary.Value > 0) ? (object)factorPrimary.Value : "Manual";
                            // Col H (8): Ocupación
                            ws.Cells[r, 8].Formula = $"=IF(AND(ISNUMBER(G{r}),G{r}>0),ROUNDUP(F{r}/G{r},0),{exportCoNsr})";
                            // Col I y J no aplican en norma única
                            ws.Cells[r, 9].Value2 = "—";
                            ws.Cells[r, 10].Value2 = "—";
                            // Col K (11): Pasillos
                            ws.Cells[r, 11].Value2 = pasillos.HasValue ? $"{pasillos.Value:0.#} mm" : "-";
                            // Col L (12): Escaleras
                            ws.Cells[r, 12].Value2 = escaleras.HasValue ? $"{escaleras.Value:0.#} mm" : "-";
                            // Col M (13): Observaciones
                            ws.Cells[r, 13].Value2 = string.Empty;
                        }

                        currentRow++;
                    }

                    int endRow = currentRow - 1;

                    // Combinar Col B para este piso si tiene más de 1 espacio
                    if (endRow > startRow)
                    {
                        dynamic pisoRange = ws.Range[$"B{startRow}:B{endRow}"];
                        pisoRange.Merge();
                        pisoRange.VerticalAlignment = (int)Excel.XlVAlign.xlVAlignCenter;
                        if (pisoRange != null) Marshal.ReleaseComObject((object)pisoRange);
                    }

                    // Fila de Total por Piso
                    int totalRowIndex = currentRow;
                    backupTotalRow.Copy();
                    dynamic targetTotalRowRange = ws.Range[$"A{totalRowIndex}:{lastColChar}{totalRowIndex}"];
                    targetTotalRowRange.PasteSpecial((int)Excel.XlPasteType.xlPasteFormats);
                    if (targetTotalRowRange != null) Marshal.ReleaseComObject((object)targetTotalRowRange);

                    if (isMulti)
                    {
                        ws.Cells[totalRowIndex, 2].Value2 = "Total";
                        ws.Cells[totalRowIndex, 3].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 4].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 5].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 6].Formula = $"=SUM(F{startRow}:F{endRow})";
                        ws.Cells[totalRowIndex, 7].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 8].Formula = $"=SUM(H{startRow}:H{endRow})";
                        ws.Cells[totalRowIndex, 9].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 10].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 11].Formula = $"=SUM(K{startRow}:K{endRow})";
                        ws.Cells[totalRowIndex, 12].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 13].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 14].Value2 = string.Empty;

                        subtotalAreaCells.Add($"F{totalRowIndex}");
                        subtotalCoCells.Add($"H{totalRowIndex}");
                        subtotalJCells.Add($"K{totalRowIndex}");
                    }
                    else
                    {
                        ws.Cells[totalRowIndex, 2].Value2 = "Total";
                        ws.Cells[totalRowIndex, 3].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 4].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 5].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 6].Formula = $"=SUM(F{startRow}:F{endRow})";
                        ws.Cells[totalRowIndex, 7].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 8].Formula = $"=ROUNDDOWN(SUM(H{startRow}:H{endRow}),0)";
                        ws.Cells[totalRowIndex, 9].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 10].Formula = $"=ROUNDDOWN(SUM(J{startRow}:J{endRow}),0)";
                        ws.Cells[totalRowIndex, 11].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 12].Value2 = string.Empty;
                        ws.Cells[totalRowIndex, 13].Value2 = string.Empty;

                        subtotalAreaCells.Add($"F{totalRowIndex}");
                        subtotalCoCells.Add($"H{totalRowIndex}");
                        subtotalJCells.Add($"J{totalRowIndex}");
                    }

                    currentRow++; // Fila de total del piso

                    // Fila vacía de separación entre tablas de pisos
                    currentRow++;
                }

                int lastRowWritten = currentRow - 1;

                // Si hay más de 1 piso, agregar fila de Total General al final
                if (floorGroups.Count > 1)
                {
                    int grandTotalRowIndex = currentRow;
                    backupTotalRow.Copy();
                    dynamic targetGrandTotalRange = ws.Range[$"A{grandTotalRowIndex}:{lastColChar}{grandTotalRowIndex}"];
                    targetGrandTotalRange.PasteSpecial((int)Excel.XlPasteType.xlPasteFormats);
                    if (targetGrandTotalRange != null) Marshal.ReleaseComObject((object)targetGrandTotalRange);

                    if (isMulti)
                    {
                        ws.Cells[grandTotalRowIndex, 2].Value2 = "Total General";
                        ws.Cells[grandTotalRowIndex, 3].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 4].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 5].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 6].Formula = $"=SUM({string.Join(",", subtotalAreaCells)})";
                        ws.Cells[grandTotalRowIndex, 7].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 8].Formula = $"=SUM({string.Join(",", subtotalCoCells)})";
                        ws.Cells[grandTotalRowIndex, 9].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 10].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 11].Formula = $"=SUM({string.Join(",", subtotalJCells)})";
                        ws.Cells[grandTotalRowIndex, 12].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 13].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 14].Value2 = string.Empty;
                    }
                    else
                    {
                        ws.Cells[grandTotalRowIndex, 2].Value2 = "Total General";
                        ws.Cells[grandTotalRowIndex, 3].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 4].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 5].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 6].Formula = $"=SUM({string.Join(",", subtotalAreaCells)})";
                        ws.Cells[grandTotalRowIndex, 7].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 8].Formula = $"=ROUNDDOWN(SUM({string.Join(",", subtotalCoCells)}),0)";
                        ws.Cells[grandTotalRowIndex, 9].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 10].Formula = $"=ROUNDDOWN(SUM({string.Join(",", subtotalJCells)}),0)";
                        ws.Cells[grandTotalRowIndex, 11].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 12].Value2 = string.Empty;
                        ws.Cells[grandTotalRowIndex, 13].Value2 = string.Empty;
                    }

                    lastRowWritten = grandTotalRowIndex;
                }

                // Limpiar del portapapeles de Excel
                try { xl.CutCopyMode = 0; } catch { }

                // Limpiar filas temporales de backup, columnas a la derecha y filas sobrantes abajo
                try
                {
                    ws.Range[$"A199:{lastColChar}200"].Clear();
                    int lastUsedRow = lastRowWritten + 10;
                    if (lastUsedRow < 150)
                    {
                        dynamic cleanRange = ws.Range[$"A{lastRowWritten + 1}:{lastColChar}{lastUsedRow}"];
                        cleanRange.ClearContents();
                        cleanRange.ClearFormats();
                        if (cleanRange != null) Marshal.ReleaseComObject((object)cleanRange);
                    }
                }
                catch { }

                if (backupDataRow != null) Marshal.ReleaseComObject((object)backupDataRow);
                if (backupTotalRow != null) Marshal.ReleaseComObject((object)backupTotalRow);

                // 11. Eliminar columnas I y J si es norma única (solo NSR o solo NFPA subida)
                if (!OccupancyService.IsMultiNormMode)
                {
                    try
                    {
                        dynamic bannerRange = ws.Range["B1:M1"];
                        bannerRange.UnMerge();
                        if (bannerRange != null) Marshal.ReleaseComObject((object)bannerRange);
                    }
                    catch { }

                    try
                    {
                        dynamic colsToDelete = ws.Range["I:J"];
                        colsToDelete.Delete((int)Excel.XlDeleteShiftDirection.xlShiftToLeft);
                        if (colsToDelete != null) Marshal.ReleaseComObject((object)colsToDelete);
                    }
                    catch { }

                    try
                    {
                        dynamic newBannerRange = ws.Range["B1:K1"];
                        newBannerRange.Merge();
                        if (newBannerRange != null) Marshal.ReleaseComObject((object)newBannerRange);
                    }
                    catch { }
                }

                // 12. Guardar y cerrar
                wb.Save();
                wb.Close(true);

                excelSuccess = true;
                ed.WriteMessage($"\n[SGH] Carga de ocupación exportada exitosamente: {outputPath}\n");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[SGH Error al exportar a Excel]: {ex.Message}\n");
                MessageBox.Show(
                    $"Ocurrió un error al exportar la tabla a Excel:\n\n{ex.Message}\n\nDetalles:\n{ex.StackTrace}",
                    "Error de Exportación SGH",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // Liberar objetos COM de manera segura
                if (ws != null) Marshal.ReleaseComObject((object)ws);
                if (wb != null) Marshal.ReleaseComObject((object)wb);
                if (xl != null)
                {
                    try { xl.Quit(); } catch { }
                    Marshal.ReleaseComObject((object)xl);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // 5. Si se seleccionó generar Informe Word
        if (model.GenerateWord)
        {
            ed.WriteMessage("\n[SGH] Generando Informe Técnico de Seguridad Humana en Word...");
            wordSuccess = WordReportService.GenerarInforme(model, spaces, wordPath, ed);
        }

            // 6. Apertura automática de ambos entregables (Excel y Word) directamente
            if (excelSuccess || wordSuccess)
            {
                ed.WriteMessage("\n[SGH] ¡Entregables generados con éxito!\n");
                if (excelSuccess) ed.WriteMessage($"[SGH] Archivo Excel: {outputPath}\n");
                if (wordSuccess) ed.WriteMessage($"[SGH] Archivo Word: {wordPath}\n");

                // Abrir automáticamente el archivo Excel
                if (excelSuccess && File.Exists(outputPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\n[SGH] No se pudo abrir automáticamente el archivo Excel: {ex.Message}\n");
                    }
                }

                // Abrir automáticamente el archivo Word
                if (wordSuccess && File.Exists(wordPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(wordPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\n[SGH] No se pudo abrir automáticamente el archivo Word: {ex.Message}\n");
                    }
                }
            }

            return excelSuccess || wordSuccess;
        }
        finally
        {
            if (loadingWin != null)
            {
                try { loadingWin.Close(); } catch { }
            }
        }
    }
}
}
