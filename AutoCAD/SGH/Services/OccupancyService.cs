using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Excel = Microsoft.Office.Interop.Excel;

namespace AutoCAD.SGH.Services
{
    public class OccupancyGroupInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public double? FactorM2PerPerson { get; set; }
        public bool IsManual => !FactorM2PerPerson.HasValue || FactorM2PerPerson.Value <= 0;

        public string DisplayText => FactorM2PerPerson.HasValue
            ? $"{Code} - {Name} ({FactorM2PerPerson.Value.ToString("0.#", CultureInfo.InvariantCulture)} m²/oc.)"
            : $"{Code} - {Name} (Manual)";

        public override string ToString() => DisplayText;
    }

    public class NormSet
    {
        public string Name { get; set; } = "NSR-10";
        public List<OccupancyGroupInfo> Groups { get; set; } = new List<OccupancyGroupInfo>();
        public Dictionary<string, (double? CorredoresMm, double? EscalerasMm)> EgressWidthFactors { get; set; }
            = new Dictionary<string, (double?, double?)>(StringComparer.OrdinalIgnoreCase);
    }

    public class NormItemCombined
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string FactorNsrDisplay { get; set; }
        public string FactorNfpaDisplay { get; set; }
        public string PasillosDisplay { get; set; }
        public string EscalerasDisplay { get; set; }
    }

    public static class OccupancyService
    {
        public const string NORMS_FILE_NAME = "Normas_NSR10_NFPA.xlsx";

        public static NormSet NormsNSR10 { get; private set; } = new NormSet { Name = "NSR-10" };
        public static NormSet NormsNFPA { get; private set; } = new NormSet { Name = "NFPA" };

        public static bool IsMultiNormMode { get; set; } = true;
        public static string PrimaryNormName { get; set; } = "NSR-10";
        public static bool HasNsrTable { get; set; } = true;
        public static bool HasNfpaTable { get; set; } = true;

        // Propiedad de compatibilidad para diálogos existentes
        public static List<OccupancyGroupInfo> Groups => (PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase) && !IsMultiNormMode) ? NormsNFPA.Groups : NormsNSR10.Groups;

        public static string LoadedNormsFilePath { get; private set; } = string.Empty;

        static OccupancyService()
        {
            try
            {
                InitializeDefaults();
                TryLoadSavedOrDefaultNorms();
            }
            catch { }
        }

        private static void InitializeDefaults()
        {
            NormsNSR10 = CreateDefaultNSR10();
            NormsNFPA = CreateDefaultNFPA();
            HasNsrTable = true;
            HasNfpaTable = true;
        }        private static NormSet CreateDefaultNSR10()
        {
            var set = new NormSet { Name = "NSR-10" };
            set.Groups = new List<OccupancyGroupInfo>
            {
                new OccupancyGroupInfo { Code = "A", Name = "ALMACENAMIENTO", FactorM2PerPerson = 28.0 },
                new OccupancyGroupInfo { Code = "C", Name = "COMERCIAL", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "C-1", Name = "Servicios", FactorM2PerPerson = 10.0 },
                new OccupancyGroupInfo { Code = "C-2", Name = "Bienes y Productos - Piso a Nivel de la Calle e Inferiores", FactorM2PerPerson = 3.0 },
                new OccupancyGroupInfo { Code = "C-2", Name = "Bienes y Productos - Otros pisos", FactorM2PerPerson = 6.0 },
                new OccupancyGroupInfo { Code = "E", Name = "ESPECIAL", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "F", Name = "FABRIL E INDUSTRIAL", FactorM2PerPerson = 9.0 },
                new OccupancyGroupInfo { Code = "I", Name = "INSTITUCIONAL", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "I-1", Name = "Reclusión", FactorM2PerPerson = 11.0 },
                new OccupancyGroupInfo { Code = "I-2", Name = "Salud o Incapacidad", FactorM2PerPerson = 7.0 },
                new OccupancyGroupInfo { Code = "I-2.1", Name = "Dormitorios", FactorM2PerPerson = 11.0 },
                new OccupancyGroupInfo { Code = "I-2.2", Name = "Cuidados Ambulatorios", FactorM2PerPerson = 9.0 },
                new OccupancyGroupInfo { Code = "I-2.3", Name = "Áreas de tratamiento con pacientes internos", FactorM2PerPerson = 22.0 },
                new OccupancyGroupInfo { Code = "I-3", Name = "Educación", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "I-3.1", Name = "Salones de Clase", FactorM2PerPerson = 1.8 },
                new OccupancyGroupInfo { Code = "I-3.2", Name = "Laboratorios, talleres y áreas vocacionales", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "I-4", Name = "Seguridad Pública", FactorM2PerPerson = 2.8 },
                new OccupancyGroupInfo { Code = "I-5", Name = "Servicio Público", FactorM2PerPerson = 0.3 },
                new OccupancyGroupInfo { Code = "L", Name = "LUGARES DE REUNIÓN", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "L", Name = "Uso concentrado - Sin asientos", FactorM2PerPerson = 0.5 },
                new OccupancyGroupInfo { Code = "L", Name = "Uso concentrado - Asientos no fijos", FactorM2PerPerson = 0.7 },
                new OccupancyGroupInfo { Code = "L", Name = "Uso menos concentrado", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "L", Name = "Uso con asientos fijos", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "L", Name = "Asientos tipo grada", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "L", Name = "Casinos", FactorM2PerPerson = 1.0 },
                new OccupancyGroupInfo { Code = "L", Name = "Salas de lectura", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "L", Name = "Zonas de estantería de libros", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "L", Name = "Piscinas - Lámina de agua", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "L", Name = "Piscinas - Deck", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "L", Name = "Escenarios", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "L", Name = "Salas de ejercicios con equipos", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "L", Name = "Salas de ejercicios sin equipos", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "L", Name = "Zonas de reclamo de equipaje", FactorM2PerPerson = 1.8 },
                new OccupancyGroupInfo { Code = "L", Name = "Zonas de manejo de equipaje", FactorM2PerPerson = 28.0 },
                new OccupancyGroupInfo { Code = "L", Name = "Zonas de espera", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "L", Name = "Juzgados (Sin asientos fijos)", FactorM2PerPerson = 3.7 },
                new OccupancyGroupInfo { Code = "L", Name = "Cocinas", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "M", Name = "MIXTO Y OTROS", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "P", Name = "ALTA PELIGROSIDAD", FactorM2PerPerson = 9.0 },
                new OccupancyGroupInfo { Code = "R", Name = "RESIDENCIAL", FactorM2PerPerson = 18.0 },
                new OccupancyGroupInfo { Code = "T", Name = "TEMPORAL Y MISCELÁNEO", FactorM2PerPerson = null }
            };

            set.EgressWidthFactors = new Dictionary<string, (double?, double?)>(StringComparer.OrdinalIgnoreCase)
            {
                { "A",     (5.0, 8.0) },
                { "C",     (5.0, 10.0) },
                { "C-1",   (5.0, 10.0) },
                { "C-2",   (5.0, 10.0) },
                { "E",     (null, null) },
                { "F",     (6.0, 10.0) },
                { "I",     (6.0, 10.0) },
                { "I-1",   (6.0, 10.0) },
                { "I-2",   (13.0, 15.0) },
                { "I-2.1", (13.0, 15.0) },
                { "I-2.2", (13.0, 15.0) },
                { "I-2.3", (13.0, 15.0) },
                { "I-3",   (13.0, 15.0) },
                { "I-3.1", (13.0, 15.0) },
                { "I-3.2", (13.0, 15.0) },
                { "I-4",   (13.0, 15.0) },
                { "I-5",   (13.0, 15.0) },
                { "L",     (5.0, 10.0) },
                { "M",     (null, null) },
                { "P",     (10.0, 18.0) },
                { "R",     (5.0, 10.0) },
                { "T",     (null, null) }
            };

            return set;
        }

        private static NormSet CreateDefaultNFPA()
        {
            var set = new NormSet { Name = "NFPA" };
            set.Groups = new List<OccupancyGroupInfo>
            {
                new OccupancyGroupInfo { Code = "ALMACENAMIENTO", Name = "Almacenamiento", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "ALMACENAMIENTO", Name = "Uso mercantil", FactorM2PerPerson = 27.9 },
                new OccupancyGroupInfo { Code = "ALMACENAMIENTO", Name = "No mercantil", FactorM2PerPerson = 46.5 },
                new OccupancyGroupInfo { Code = "Mercantil", Name = "Area de ventas a nivel de calle", FactorM2PerPerson = 2.8 },
                new OccupancyGroupInfo { Code = "Mercantil", Name = "Area de ventas en 2 o mas pisos a nivel de calle", FactorM2PerPerson = 3.7 },
                new OccupancyGroupInfo { Code = "Mercantil", Name = "Area de ventas en 1 piso debajo de nivel de calle", FactorM2PerPerson = 2.8 },
                new OccupancyGroupInfo { Code = "Mercantil", Name = "Area de ventas en pisos situados por encima del piso a nivel de calle", FactorM2PerPerson = 5.6 },
                new OccupancyGroupInfo { Code = "Mercantil", Name = "Pisos o sectores de almacenamiento y envio - No abierto al público", FactorM2PerPerson = 27.9 },
                new OccupancyGroupInfo { Code = "Negocios", Name = "Uso concentrado", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "Negocios", Name = "Niveles de obs de torres de control de tráfico de aeropuertos", FactorM2PerPerson = 3.7 },
                new OccupancyGroupInfo { Code = "Negocios", Name = "Salas de colaboración <41,8m2", FactorM2PerPerson = 2.8 },
                new OccupancyGroupInfo { Code = "Negocios", Name = "Salas de colaboración >41,8m2", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "Negocios", Name = "Otros negocios", FactorM2PerPerson = 14.0 },
                new OccupancyGroupInfo { Code = "Guarderías", Name = "Guarderías", FactorM2PerPerson = 3.3 },
                new OccupancyGroupInfo { Code = "Detención y correccional", Name = "Detención y correccional", FactorM2PerPerson = 11.1 },
                new OccupancyGroupInfo { Code = "Educacional", Name = "Aulas", FactorM2PerPerson = 1.9 },
                new OccupancyGroupInfo { Code = "Educacional", Name = "Talleres, laboratorios salas vocacionales", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "Salud", Name = "Tratamiento de pacientes internados", FactorM2PerPerson = 22.3 },
                new OccupancyGroupInfo { Code = "Salud", Name = "Habitaciones para dormir", FactorM2PerPerson = 11.1 },
                new OccupancyGroupInfo { Code = "Salud", Name = "Pacientes ambulatorios", FactorM2PerPerson = 14.0 },
                new OccupancyGroupInfo { Code = "Industrial", Name = "Industria general y peligro elevado", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "Residencial", Name = "Hoteles y dormitorios", FactorM2PerPerson = 18.6 },
                new OccupancyGroupInfo { Code = "Residencial", Name = "Edificios de departamentos", FactorM2PerPerson = 18.6 },
                new OccupancyGroupInfo { Code = "Residencial", Name = "Asilos y centros de acogida", FactorM2PerPerson = 18.6 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Uso concentrado - Sin asientos", FactorM2PerPerson = 0.7 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Uso menos concentrado", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Asientos tipo banco", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Uso con asientos fijos", FactorM2PerPerson = null },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Espacios de espera <=930m2", FactorM2PerPerson = 0.46 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Espacios de espera >930m2", FactorM2PerPerson = 0.65 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Cocinas", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Estanterias en bibliotecas", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Salas de lectura", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Piscinas", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Áreas alrededor de piscinas", FactorM2PerPerson = 2.8 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Salas de ejercicios con equipos", FactorM2PerPerson = 4.6 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Salas de ejercicios sin equipos", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Escenarios", FactorM2PerPerson = 1.4 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Pasarelas, galerías y andamios para iluminación y acceso", FactorM2PerPerson = 9.3 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Casinos y áreas de juego similares", FactorM2PerPerson = 1.0 },
                new OccupancyGroupInfo { Code = "Reunión pública", Name = "Pistas de patinaje", FactorM2PerPerson = 4.6 }
            };

            set.EgressWidthFactors = new Dictionary<string, (double?, double?)>(StringComparer.OrdinalIgnoreCase)
            {
                { "ALMACENAMIENTO",           (10.0, 8.0) },
                { "Mercantil",                (10.0, 10.0) },
                { "Negocios",                 (10.0, 10.0) },
                { "Guarderías",               (10.0, 10.0) },
                { "Detención y correccional", (10.0, 10.0) },
                { "Educacional",              (10.0, 15.0) },
                { "Salud",                    (10.0, 15.0) },
                { "Industrial",               (10.0, 10.0) },
                { "Residencial",              (10.0, 10.0) },
                { "Reunión pública",          (10.0, 10.0) }
            };

            return set;
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "DICTA_AutoCAD_SGH_NormsPath.txt");
        }

        public static void TryLoadSavedOrDefaultNorms()
        {
            try
            {
                string cfgFile = GetConfigPath();
                if (File.Exists(cfgFile))
                {
                    string savedPath = File.ReadAllText(cfgFile).Trim();
                    if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                    {
                        if (LoadNormsFromExcel(savedPath))
                        {
                            return;
                        }
                    }
                }
            }
            catch { }

            try
            {
                string defaultPath = LocalizarPlantillaNormas();
                if (!string.IsNullOrEmpty(defaultPath) && File.Exists(defaultPath))
                {
                    LoadNormsFromExcel(defaultPath);
                }
            }
            catch { }
        }

        public static string LocalizarPlantillaNormas()
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
                candidates.Add(Path.Combine(dllDir, "Resources", NORMS_FILE_NAME));
                candidates.Add(Path.Combine(dllDir, NORMS_FILE_NAME));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents\Resources", NORMS_FILE_NAME));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\..\Resources", NORMS_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\Resources", NORMS_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\Resources", NORMS_FILE_NAME)));
            }

            candidates.Add(Path.Combine(@"C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\Resources", NORMS_FILE_NAME));

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

        public static bool LoadNormsFromExcel(string excelPath)
        {
            if (string.IsNullOrEmpty(excelPath) || !File.Exists(excelPath))
                return false;

            // 1. Intento de lectura ultrarrápida en memoria vía OpenXml (ZipArchive + XDocument)
            if (TryLoadNormsViaOpenXml(excelPath, out var parsedSets) && parsedSets.Count > 0)
            {
                ApplyParsedNormSets(parsedSets, excelPath);
                return true;
            }

            // 2. Fallback COM solo si no se pudo leer por OpenXml (por ejemplo, archivos .xls binarios antiguos)
            Excel.Application xl = null;
            Excel.Workbook wb = null;

            try
            {
                xl = new Excel.Application { Visible = false, DisplayAlerts = false };
                wb = xl.Workbooks.Open(excelPath, ReadOnly: true);

                var comParsedSets = new List<NormSet>();

                foreach (Excel.Worksheet ws in wb.Worksheets)
                {
                    try
                    {
                        var headerPositions = new List<(int Row, int Col, string Title, string ColHeader)>();

                        for (int r = 1; r <= 150; r++)
                        {
                            for (int c = 1; c <= 15; c++)
                            {
                                string val = (ws.Cells[r, c] as Excel.Range)?.Value2?.ToString()?.Trim();
                                if (!string.IsNullOrEmpty(val))
                                {
                                    string valLower = val.ToLowerInvariant();
                                    if (valLower.Equals("nomenclatura") ||
                                        valLower.Equals("uso") ||
                                        valLower.Equals("código") ||
                                        valLower.Equals("codigo") ||
                                        valLower.Equals("cod") ||
                                        valLower.Equals("grupo") ||
                                        valLower.Equals("grupos") ||
                                        valLower.Equals("clasificación") ||
                                        valLower.Equals("clasificacion") ||
                                        valLower.Equals("categoría") ||
                                        valLower.Equals("categoria") ||
                                        valLower.Equals("norma") ||
                                        valLower.StartsWith("código") ||
                                        valLower.StartsWith("codigo"))
                                    {
                                        string title = "";
                                        if (r > 1)
                                        {
                                            title = (ws.Cells[r - 1, c] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                                            if (string.IsNullOrEmpty(title) && c > 1)
                                            {
                                                title = (ws.Cells[r - 1, c - 1] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                                            }
                                        }

                                        headerPositions.Add((r, c, title, val));
                                    }
                                }
                            }
                        }

                        if (headerPositions.Count == 0 && comParsedSets.Count == 0)
                        {
                            string checkData = (ws.Cells[2, 1] as Excel.Range)?.Value2?.ToString()?.Trim() ??
                                              (ws.Cells[3, 1] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(checkData))
                            {
                                headerPositions.Add((2, 1, ws.Name ?? "Normas", "Nomenclatura"));
                            }
                        }

                        for (int i = 0; i < headerPositions.Count; i++)
                        {
                            var pos = headerPositions[i];
                            int headerRow = pos.Row;
                            int codeCol = pos.Col;
                            string title = pos.Title;
                            string colHeader = pos.ColHeader;

                            int nameCol = codeCol + 1;
                            int factorCol = codeCol + 2;

                            int pasillosCol = -1;
                            int escalerasCol = -1;
                            string col3Head = (ws.Cells[headerRow, codeCol + 3] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                            string col4Head = (ws.Cells[headerRow, codeCol + 4] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                            if (col3Head.IndexOf("Pasillo", StringComparison.OrdinalIgnoreCase) >= 0 || col3Head.IndexOf("Corredor", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                pasillosCol = codeCol + 3;
                            }
                            if (col4Head.IndexOf("Escalera", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                escalerasCol = codeCol + 4;
                            }

                            var tempRows = new List<(string Code, string Name, double? Factor, double? Pasillos, double? Escaleras)>();
                            int row = headerRow + 1;
                            int emptyCount = 0;
                            string lastCodeOrUso = "";

                            while (row <= headerRow + 100 && emptyCount < 4)
                            {
                                string code = (ws.Cells[row, codeCol] as Excel.Range)?.Value2?.ToString()?.Trim();
                                string name = (ws.Cells[row, nameCol] as Excel.Range)?.Value2?.ToString()?.Trim() ?? "";
                                var valFactorRaw = (ws.Cells[row, factorCol] as Excel.Range)?.Value2;

                                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name) && valFactorRaw == null)
                                {
                                    emptyCount++;
                                    row++;
                                    continue;
                                }

                                if (!string.IsNullOrEmpty(code) &&
                                    (code.StartsWith("Tabla", StringComparison.OrdinalIgnoreCase) ||
                                     code.StartsWith("TABLA", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Nomenclatura", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("USO", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Código", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Codigo", StringComparison.OrdinalIgnoreCase)))
                                {
                                    break;
                                }

                                emptyCount = 0;

                                if (!string.IsNullOrEmpty(code))
                                {
                                    lastCodeOrUso = code;
                                }
                                else
                                {
                                    code = lastCodeOrUso;
                                }

                                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(code))
                                {
                                    name = code;
                                }

                                double? factor = ParseDoubleNullable(valFactorRaw);
                                double? pasillos = pasillosCol > 0 ? ParseDoubleNullable((ws.Cells[row, pasillosCol] as Excel.Range)?.Value2) : null;
                                double? escaleras = escalerasCol > 0 ? ParseDoubleNullable((ws.Cells[row, escalerasCol] as Excel.Range)?.Value2) : null;

                                tempRows.Add((code, name, factor, pasillos, escaleras));
                                row++;
                            }

                            if (tempRows.Count > 0)
                            {
                                var samples = tempRows.Select(x => (x.Code, x.Name)).ToList();
                                string normName = DetectNormTypeFromCom(ws, headerRow, codeCol, colHeader, samples);

                                var set = new NormSet { Name = normName };
                                foreach (var item in tempRows)
                                {
                                    double? pasillos = item.Pasillos;
                                    double? escaleras = item.Escaleras;

                                    if (!pasillos.HasValue || !escaleras.HasValue)
                                    {
                                        var defaultSet = normName == "NFPA" ? CreateDefaultNFPA() : CreateDefaultNSR10();
                                        if (defaultSet.EgressWidthFactors.TryGetValue(item.Code, out var defEw))
                                        {
                                            if (!pasillos.HasValue) pasillos = defEw.CorredoresMm;
                                            if (!escaleras.HasValue) escaleras = defEw.EscalerasMm;
                                        }
                                    }

                                    set.Groups.Add(new OccupancyGroupInfo
                                    {
                                        Code = item.Code,
                                        Name = item.Name,
                                        FactorM2PerPerson = item.Factor
                                    });

                                    set.EgressWidthFactors[item.Code] = (pasillos, escaleras);
                                }

                                comParsedSets.Add(set);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        if (ws != null) Marshal.ReleaseComObject(ws);
                    }
                }

                if (comParsedSets.Count > 0)
                {
                    ApplyParsedNormSets(comParsedSets, excelPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SGH LoadNormsFromExcel COM Error]: {ex.Message}");
                return false;
            }
            finally
            {
                if (wb != null) { try { wb.Close(false); } catch { } Marshal.ReleaseComObject(wb); }
                if (xl != null) { try { xl.Quit(); } catch { } Marshal.ReleaseComObject(xl); }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static bool TryLoadNormsViaOpenXml(string excelPath, out List<NormSet> parsedSets)
        {
            parsedSets = new List<NormSet>();
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(excelPath))
                {
                    XNamespace sNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                    XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

                    // 1. Cargar cadenas compartidas (SharedStrings)
                    var sharedStrings = new List<string>();
                    var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                    if (ssEntry != null)
                    {
                        using (var s = ssEntry.Open())
                        {
                            var xss = System.Xml.Linq.XDocument.Load(s);
                            foreach (var si in xss.Descendants(sNs + "si"))
                            {
                                string str = string.Concat(si.Descendants(sNs + "t").Select(t => t.Value));
                                sharedStrings.Add(str);
                            }
                        }
                    }

                    // 2. Mapear hojas
                    var relMap = new Dictionary<string, string>();
                    var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
                    if (relsEntry != null)
                    {
                        using (var s = relsEntry.Open())
                        {
                            var xRels = System.Xml.Linq.XDocument.Load(s);
                            foreach (var rel in xRels.Descendants(pkgRelNs + "Relationship"))
                            {
                                string id = (string)rel.Attribute("Id");
                                string target = (string)rel.Attribute("Target");
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                                {
                                    if (!target.StartsWith("xl/")) target = "xl/" + target.TrimStart('/');
                                    relMap[id] = target;
                                }
                            }
                        }
                    }

                    var sheetList = new List<(string Name, string Path)>();
                    var wbEntry = zip.GetEntry("xl/workbook.xml");
                    if (wbEntry != null)
                    {
                        using (var s = wbEntry.Open())
                        {
                            var xWb = System.Xml.Linq.XDocument.Load(s);
                            foreach (var sh in xWb.Descendants(sNs + "sheet"))
                            {
                                string name = (string)sh.Attribute("name");
                                string rId = (string)sh.Attribute(rNs + "id");
                                if (!string.IsNullOrEmpty(rId) && relMap.TryGetValue(rId, out string p))
                                {
                                    sheetList.Add((name, p));
                                }
                            }
                        }
                    }

                    if (sheetList.Count == 0)
                    {
                        foreach (var e in zip.Entries)
                        {
                            if (e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"))
                            {
                                sheetList.Add((Path.GetFileNameWithoutExtension(e.FullName), e.FullName));
                            }
                        }
                    }

                    // 3. Procesar cada hoja de cálculo
                    foreach (var shInfo in sheetList)
                    {
                        var shEntry = zip.GetEntry(shInfo.Path);
                        if (shEntry == null) continue;

                        var grid = new Dictionary<(int Row, int Col), string>();
                        using (var s = shEntry.Open())
                        {
                            var xSheet = System.Xml.Linq.XDocument.Load(s);
                            foreach (var c in xSheet.Descendants(sNs + "c"))
                            {
                                string rAttr = (string)c.Attribute("r");
                                if (string.IsNullOrEmpty(rAttr)) continue;

                                var (rIdx, cIdx) = ParseCellReference(rAttr);
                                string cellVal = null;
                                string tAttr = (string)c.Attribute("t");

                                if (tAttr == "s")
                                {
                                    string vStr = c.Element(sNs + "v")?.Value;
                                    if (int.TryParse(vStr, out int sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                                    {
                                        cellVal = sharedStrings[sIdx];
                                    }
                                }
                                else if (tAttr == "inlineStr")
                                {
                                    cellVal = c.Element(sNs + "is")?.Element(sNs + "t")?.Value;
                                }
                                else
                                {
                                    cellVal = c.Element(sNs + "v")?.Value;
                                }

                                if (!string.IsNullOrEmpty(cellVal))
                                {
                                    grid[(rIdx, cIdx)] = cellVal.Trim();
                                }
                            }
                        }

                        // Localizar cabeceras en la cuadrícula
                        var headerPositions = new List<(int Row, int Col, string Title, string ColHeader)>();
                        for (int r = 1; r <= 150; r++)
                        {
                            for (int c = 1; c <= 15; c++)
                            {
                                if (grid.TryGetValue((r, c), out string val) && !string.IsNullOrEmpty(val))
                                {
                                    string valLower = val.ToLowerInvariant();
                                    if (valLower.Equals("nomenclatura") ||
                                        valLower.Equals("uso") ||
                                        valLower.Equals("código") ||
                                        valLower.Equals("codigo") ||
                                        valLower.Equals("cod") ||
                                        valLower.Equals("grupo") ||
                                        valLower.Equals("grupos") ||
                                        valLower.Equals("clasificación") ||
                                        valLower.Equals("clasificacion") ||
                                        valLower.Equals("categoría") ||
                                        valLower.Equals("categoria") ||
                                        valLower.Equals("norma") ||
                                        valLower.StartsWith("código") ||
                                        valLower.StartsWith("codigo"))
                                    {
                                        string title = "";
                                        if (r > 1 && grid.TryGetValue((r - 1, c), out string t1)) title = t1;
                                        if (string.IsNullOrEmpty(title) && r > 1 && c > 1 && grid.TryGetValue((r - 1, c - 1), out string t2)) title = t2;

                                        headerPositions.Add((r, c, title, val));
                                    }
                                }
                            }
                        }

                        if (headerPositions.Count == 0 && parsedSets.Count == 0)
                        {
                            if (grid.TryGetValue((2, 1), out string _) || grid.TryGetValue((3, 1), out string _))
                            {
                                headerPositions.Add((2, 1, shInfo.Name ?? "Normas", "Nomenclatura"));
                            }
                        }

                        for (int i = 0; i < headerPositions.Count; i++)
                        {
                            var pos = headerPositions[i];
                            int headerRow = pos.Row;
                            int codeCol = pos.Col;
                            string title = pos.Title;
                            string colHeader = pos.ColHeader;

                            int nameCol = codeCol + 1;
                            int factorCol = codeCol + 2;

                            int pasillosCol = -1;
                            int escalerasCol = -1;

                            if (grid.TryGetValue((headerRow, codeCol + 3), out string h3) && (h3.IndexOf("Pasillo", StringComparison.OrdinalIgnoreCase) >= 0 || h3.IndexOf("Corredor", StringComparison.OrdinalIgnoreCase) >= 0))
                                pasillosCol = codeCol + 3;
                            if (grid.TryGetValue((headerRow, codeCol + 4), out string h4) && h4.IndexOf("Escalera", StringComparison.OrdinalIgnoreCase) >= 0)
                                escalerasCol = codeCol + 4;

                            var tempRows = new List<(string Code, string Name, double? Factor, double? Pasillos, double? Escaleras)>();
                            int row = headerRow + 1;
                            int emptyCount = 0;
                            string lastCodeOrUso = "";

                            while (row <= headerRow + 100 && emptyCount < 4)
                            {
                                grid.TryGetValue((row, codeCol), out string code);
                                grid.TryGetValue((row, nameCol), out string name);
                                grid.TryGetValue((row, factorCol), out string valFactorRaw);

                                code = code?.Trim();
                                name = name?.Trim() ?? "";

                                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(valFactorRaw))
                                {
                                    emptyCount++;
                                    row++;
                                    continue;
                                }

                                if (!string.IsNullOrEmpty(code) &&
                                    (code.StartsWith("Tabla", StringComparison.OrdinalIgnoreCase) ||
                                     code.StartsWith("TABLA", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Nomenclatura", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("USO", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Código", StringComparison.OrdinalIgnoreCase) ||
                                     code.Equals("Codigo", StringComparison.OrdinalIgnoreCase)))
                                {
                                    break;
                                }

                                emptyCount = 0;

                                if (!string.IsNullOrEmpty(code)) lastCodeOrUso = code;
                                else code = lastCodeOrUso;

                                if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(code)) name = code;

                                double? factor = ParseDoubleNullable(valFactorRaw);
                                double? pasillos = pasillosCol > 0 && grid.TryGetValue((row, pasillosCol), out string pStr) ? ParseDoubleNullable(pStr) : null;
                                double? escaleras = escalerasCol > 0 && grid.TryGetValue((row, escalerasCol), out string eStr) ? ParseDoubleNullable(eStr) : null;

                                tempRows.Add((code, name, factor, pasillos, escaleras));
                                row++;
                            }

                            if (tempRows.Count > 0)
                            {
                                var samples = tempRows.Select(x => (x.Code, x.Name)).ToList();
                                string normName = DetectNormTypeFromGrid(grid, shInfo.Name, headerRow, codeCol, colHeader, samples);

                                var set = new NormSet { Name = normName };
                                foreach (var item in tempRows)
                                {
                                    double? pasillos = item.Pasillos;
                                    double? escaleras = item.Escaleras;

                                    if (!pasillos.HasValue || !escaleras.HasValue)
                                    {
                                        var defaultSet = normName == "NFPA" ? CreateDefaultNFPA() : CreateDefaultNSR10();
                                        if (defaultSet.EgressWidthFactors.TryGetValue(item.Code, out var defEw))
                                        {
                                            if (!pasillos.HasValue) pasillos = defEw.CorredoresMm;
                                            if (!escaleras.HasValue) escaleras = defEw.EscalerasMm;
                                        }
                                    }

                                    set.Groups.Add(new OccupancyGroupInfo
                                    {
                                        Code = item.Code,
                                        Name = item.Name,
                                        FactorM2PerPerson = item.Factor
                                    });

                                    set.EgressWidthFactors[item.Code] = (pasillos, escaleras);
                                }

                                parsedSets.Add(set);
                            }
                        }
                    }
                }

                return parsedSets.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenXml Reader Exception]: {ex.Message}");
                return false;
            }
        }

        private static (int Row, int Col) ParseCellReference(string cellRef)
        {
            int col = 0;
            int row = 0;
            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            {
                col = col * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
                i++;
            }
            while (i < cellRef.Length && char.IsDigit(cellRef[i]))
            {
                row = row * 10 + (cellRef[i] - '0');
                i++;
            }
            return (row, col);
        }

        private static void ApplyParsedNormSets(List<NormSet> parsedSets, string excelPath)
        {
            if (parsedSets == null || parsedSets.Count == 0) return;

            var nsrSet = parsedSets.FirstOrDefault(s => s.Name.Equals("NSR-10", StringComparison.OrdinalIgnoreCase));
            var nfpaSet = parsedSets.FirstOrDefault(s => s.Name.Equals("NFPA", StringComparison.OrdinalIgnoreCase));

            if (parsedSets.Count >= 2)
            {
                if (nsrSet == null && nfpaSet != null)
                {
                    nsrSet = parsedSets.FirstOrDefault(s => s != nfpaSet);
                    if (nsrSet != null) nsrSet.Name = "NSR-10";
                }
                else if (nfpaSet == null && nsrSet != null)
                {
                    nfpaSet = parsedSets.FirstOrDefault(s => s != nsrSet);
                    if (nfpaSet != null) nfpaSet.Name = "NFPA";
                }
                else if (nsrSet == null && nfpaSet == null)
                {
                    nsrSet = parsedSets[0];
                    nsrSet.Name = "NSR-10";
                    nfpaSet = parsedSets[1];
                    nfpaSet.Name = "NFPA";
                }

                HasNsrTable = nsrSet != null;
                HasNfpaTable = nfpaSet != null;

                NormsNSR10 = nsrSet ?? CreateDefaultNSR10();
                NormsNFPA = nfpaSet ?? CreateDefaultNFPA();
                PrimaryNormName = "NSR-10";
                IsMultiNormMode = true;
            }
            else
            {
                var singleSet = parsedSets[0];
                if (singleSet.Name.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
                {
                    HasNsrTable = false;
                    HasNfpaTable = true;
                    NormsNFPA = singleSet;
                    PrimaryNormName = "NFPA";
                    IsMultiNormMode = false;
                }
                else
                {
                    HasNsrTable = true;
                    HasNfpaTable = false;
                    NormsNSR10 = singleSet;
                    PrimaryNormName = "NSR-10";
                    IsMultiNormMode = false;
                }
            }

            LoadedNormsFilePath = excelPath;

            try
            {
                File.WriteAllText(GetConfigPath(), excelPath);
            }
            catch { }
        }

        private static string DetectNormTypeFromGrid(Dictionary<(int Row, int Col), string> grid, string sheetName, int headerRow, int codeCol, string colHeader, List<(string Code, string Name)> sampleRows)
        {
            if (!string.IsNullOrEmpty(sheetName))
            {
                if (sheetName.IndexOf("NFPA", StringComparison.OrdinalIgnoreCase) >= 0) return "NFPA";
                if (sheetName.IndexOf("NSR", StringComparison.OrdinalIgnoreCase) >= 0) return "NSR-10";
            }

            if (!string.IsNullOrEmpty(colHeader))
            {
                if (colHeader.Equals("USO", StringComparison.OrdinalIgnoreCase) || colHeader.IndexOf("NFPA", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "NFPA";
                if (colHeader.Equals("Nomenclatura", StringComparison.OrdinalIgnoreCase) || colHeader.IndexOf("NSR", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "NSR-10";
            }

            for (int r = Math.Max(1, headerRow - 4); r < headerRow; r++)
            {
                for (int c = Math.Max(1, codeCol - 2); c <= codeCol + 4; c++)
                {
                    if (grid.TryGetValue((r, c), out string cellText) && !string.IsNullOrEmpty(cellText))
                    {
                        if (cellText.IndexOf("NFPA", StringComparison.OrdinalIgnoreCase) >= 0 || cellText.IndexOf("7.3.1", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "NFPA";
                        if (cellText.IndexOf("NSR", StringComparison.OrdinalIgnoreCase) >= 0 || cellText.IndexOf("K.3.3", StringComparison.OrdinalIgnoreCase) >= 0 || cellText.IndexOf("k.3.3", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "NSR-10";
                    }
                }
            }

            if (sampleRows != null && sampleRows.Count > 0)
            {
                int nfpaCount = 0;
                int nsrCount = 0;
                foreach (var sample in sampleRows)
                {
                    string cStr = sample.Code ?? "";
                    string nStr = sample.Name ?? "";

                    if (cStr.Equals("Mercantil", StringComparison.OrdinalIgnoreCase) ||
                        cStr.Equals("Negocios", StringComparison.OrdinalIgnoreCase) ||
                        cStr.Equals("Guarderías", StringComparison.OrdinalIgnoreCase) ||
                        cStr.Equals("Educacional", StringComparison.OrdinalIgnoreCase) ||
                        cStr.Equals("Salud", StringComparison.OrdinalIgnoreCase) ||
                        cStr.Equals("Detención y correccional", StringComparison.OrdinalIgnoreCase) ||
                        nStr.IndexOf("mercantil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        nStr.IndexOf("nivel de calle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        nfpaCount += 3;
                    }

                    if (cStr.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                        cStr.StartsWith("C-", StringComparison.OrdinalIgnoreCase) ||
                        cStr.StartsWith("I-", StringComparison.OrdinalIgnoreCase) ||
                        cStr.StartsWith("L-", StringComparison.OrdinalIgnoreCase))
                    {
                        nsrCount += 3;
                    }
                }

                if (nfpaCount > nsrCount) return "NFPA";
                if (nsrCount > nfpaCount) return "NSR-10";
            }

            return colHeader.Equals("USO", StringComparison.OrdinalIgnoreCase) ? "NFPA" : "NSR-10";
        }

        private static string DetectNormTypeFromCom(Excel.Worksheet ws, int headerRow, int codeCol, string colHeader, List<(string Code, string Name)> sampleRows)
        {
            if (ws != null)
            {
                try
                {
                    string wsName = ws.Name ?? "";
                    if (wsName.IndexOf("NFPA", StringComparison.OrdinalIgnoreCase) >= 0) return "NFPA";
                    if (wsName.IndexOf("NSR", StringComparison.OrdinalIgnoreCase) >= 0) return "NSR-10";
                }
                catch { }
            }

            return DetectNormTypeFromGrid(new Dictionary<(int Row, int Col), string>(), ws?.Name, headerRow, codeCol, colHeader, sampleRows);
        }

        /// <summary>
        /// Obtiene la lista consolidada de grupos únicos de ocupación disponibles para la norma seleccionada (NSR-10 o NFPA).
        /// </summary>
        public static List<string> GetDistinctGroupNamesForNorm(string normName)
        {
            var result = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (string.Equals(normName, "NFPA", StringComparison.OrdinalIgnoreCase))
                {
                    var nfpaGroups = NormsNFPA?.Groups != null && NormsNFPA.Groups.Count > 0
                        ? NormsNFPA.Groups
                        : CreateDefaultNFPA().Groups;

                    foreach (var g in nfpaGroups ?? Enumerable.Empty<OccupancyGroupInfo>())
                    {
                        if (g == null) continue;
                        string name = (g.Name ?? g.Code ?? "").Trim();
                        if (!string.IsNullOrEmpty(name) && set.Add(name))
                        {
                            result.Add(name);
                        }
                    }

                    if (result.Count == 0)
                    {
                        result = new List<string> { "Almacenamiento", "Mercantil", "Negocios", "Guarderías", "Detención y correccional", "Educacional", "Salud", "Industrial", "Residencial", "Reunión pública" };
                    }
                }
                else
                {
                    var nsrGroups = NormsNSR10?.Groups != null && NormsNSR10.Groups.Count > 0
                        ? NormsNSR10.Groups
                        : CreateDefaultNSR10().Groups;

                    // Extraer los grupos principales de NSR-10
                    foreach (var g in nsrGroups ?? Enumerable.Empty<OccupancyGroupInfo>())
                    {
                        if (g == null) continue;
                        string code = (g.Code ?? "").Trim();
                        string name = (g.Name ?? "").Trim();
                        string groupName = null;

                        if (!string.IsNullOrEmpty(code) && code.Length == 1 && char.IsLetter(code[0]))
                        {
                            groupName = !string.IsNullOrEmpty(name) ? $"{name} ({code})" : code;
                        }
                        else if (!string.IsNullOrEmpty(code) && (code.StartsWith("C-") || code.StartsWith("I-") || code.StartsWith("R-") || code.StartsWith("L-")))
                        {
                            groupName = !string.IsNullOrEmpty(name) ? $"{name} ({code})" : code;
                        }
                        else if (!string.IsNullOrEmpty(name))
                        {
                            groupName = name;
                        }

                        if (!string.IsNullOrEmpty(groupName) && set.Add(groupName))
                        {
                            result.Add(groupName);
                        }
                    }

                    if (result.Count == 0 || !result.Any(r => r.IndexOf("Residencial", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf("R-1", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        result = new List<string>
                        {
                            "Almacenamiento (A)",
                            "Comercial (C)",
                            "Especial (E)",
                            "Fabril e Industrial (F)",
                            "Institucional (I)",
                            "Lugares de Reunión (L)",
                            "Mixto y Otros (M)",
                            "Alta Peligrosidad (P)",
                            "Residencial (R)",
                            "Residencial (R-1)",
                            "Residencial (R-2)",
                            "Temporal y Misceláneo (T)"
                        };
                    }
                }
            }
            catch
            {
                result = new List<string>
                {
                    "Almacenamiento (A)",
                    "Comercial (C)",
                    "Especial (E)",
                    "Fabril e Industrial (F)",
                    "Institucional (I)",
                    "Lugares de Reunión (L)",
                    "Mixto y Otros (M)",
                    "Alta Peligrosidad (P)",
                    "Residencial (R)",
                    "Temporal y Misceláneo (T)"
                };
            }

            return result;
        }



        private static double? ParseDoubleNullable(object valObj)
        {
            if (valObj == null) return null;

            if (valObj is double dVal) return dVal > 0 ? dVal : (double?)null;
            if (valObj is float fVal) return fVal > 0 ? (double)fVal : (double?)null;
            if (valObj is int iVal) return iVal > 0 ? (double)iVal : (double?)null;

            string str = valObj.ToString().Trim();
            if (string.IsNullOrEmpty(str) ||
                str.IndexOf("Manual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                str.IndexOf("según", StringComparison.OrdinalIgnoreCase) >= 0 ||
                str.IndexOf("segun", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(str, @"([0-9]+([.,][0-9]+)?)");
            if (match.Success)
            {
                string numStr = match.Value.Replace(',', '.');
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                {
                    return parsed;
                }
            }

            return null;
        }

        public static OccupancyGroupInfo FindNfpaGroupForNsr(OccupancyGroupInfo nsrGroup)
        {
            if (nsrGroup == null || NormsNFPA == null || NormsNFPA.Groups == null || NormsNFPA.Groups.Count == 0)
                return null;

            string nsrCode = nsrGroup.Code?.Trim() ?? "";
            string nsrName = nsrGroup.Name?.Trim() ?? "";

            var match = NormsNFPA.Groups.FirstOrDefault(x => x.Code.Equals(nsrCode, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            match = NormsNFPA.Groups.FirstOrDefault(x => x.Name.Equals(nsrName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            if (nsrCode.Equals("A", StringComparison.OrdinalIgnoreCase))
                return NormsNFPA.Groups.FirstOrDefault(x => x.Code.IndexOf("ALMACENAMIENTO", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Almacenamiento", StringComparison.OrdinalIgnoreCase) >= 0);

            if (nsrCode.Equals("C-1", StringComparison.OrdinalIgnoreCase))
                return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Uso mercantil", StringComparison.OrdinalIgnoreCase) >= 0);

            if (nsrCode.Equals("C-2", StringComparison.OrdinalIgnoreCase))
            {
                if (nsrName.IndexOf("Calle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("nivel de calle", StringComparison.OrdinalIgnoreCase) >= 0);
                if (nsrName.IndexOf("Otros", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("2 o mas pisos", StringComparison.OrdinalIgnoreCase) >= 0);
                return NormsNFPA.Groups.FirstOrDefault(x => x.Code.Equals("Mercantil", StringComparison.OrdinalIgnoreCase));
            }

            if (nsrCode.Equals("I-1", StringComparison.OrdinalIgnoreCase))
                return NormsNFPA.Groups.FirstOrDefault(x => x.Code.IndexOf("Detención", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Detención", StringComparison.OrdinalIgnoreCase) >= 0);

            if (nsrCode.StartsWith("I-2", StringComparison.OrdinalIgnoreCase))
            {
                if (nsrCode.Equals("I-2.1", StringComparison.OrdinalIgnoreCase) || nsrName.IndexOf("Dormitorios", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Habitaciones", StringComparison.OrdinalIgnoreCase) >= 0);
                if (nsrCode.Equals("I-2.2", StringComparison.OrdinalIgnoreCase) || nsrName.IndexOf("Ambulatorios", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("ambulatorios", StringComparison.OrdinalIgnoreCase) >= 0);
                if (nsrCode.Equals("I-2.3", StringComparison.OrdinalIgnoreCase) || nsrName.IndexOf("internos", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Tratamiento", StringComparison.OrdinalIgnoreCase) >= 0);
                return NormsNFPA.Groups.FirstOrDefault(x => x.Code.Equals("Salud", StringComparison.OrdinalIgnoreCase));
            }

            if (nsrCode.StartsWith("I-3", StringComparison.OrdinalIgnoreCase))
            {
                if (nsrCode.Equals("I-3.1", StringComparison.OrdinalIgnoreCase) || nsrName.IndexOf("Salones", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Aulas", StringComparison.OrdinalIgnoreCase) >= 0);
                if (nsrCode.Equals("I-3.2", StringComparison.OrdinalIgnoreCase) || nsrName.IndexOf("Laboratorios", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Talleres", StringComparison.OrdinalIgnoreCase) >= 0);
                return NormsNFPA.Groups.FirstOrDefault(x => x.Code.Equals("Educacional", StringComparison.OrdinalIgnoreCase));
            }

            if (nsrCode.Equals("I-4", StringComparison.OrdinalIgnoreCase))
                return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("Otros negocios", StringComparison.OrdinalIgnoreCase) >= 0);

            if (nsrCode.Equals("I-5", StringComparison.OrdinalIgnoreCase))
                return NormsNFPA.Groups.FirstOrDefault(x => x.Name.IndexOf("concentrado", StringComparison.OrdinalIgnoreCase) >= 0);

            return null;
        }

        public static List<NormItemCombined> GetNormItemsCombined()
        {
            var items = new List<NormItemCombined>();

            if (!IsMultiNormMode && PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var g in NormsNFPA.Groups)
                {
                    NormsNFPA.EgressWidthFactors.TryGetValue(g.Code, out var ewNfpa);

                    string factorStr = g.FactorM2PerPerson.HasValue ? $"{g.FactorM2PerPerson.Value:0.#} m²/pers" : "Manual";
                    string pasilloStr = ewNfpa.CorredoresMm.HasValue ? $"{ewNfpa.CorredoresMm.Value:0.#}" : "Manual";
                    string escStr = ewNfpa.EscalerasMm.HasValue ? $"{ewNfpa.EscalerasMm.Value:0.#}" : "Manual";

                    items.Add(new NormItemCombined
                    {
                        Code = g.Code,
                        Name = g.Name,
                        FactorNsrDisplay = "— (Solo NFPA)",
                        FactorNfpaDisplay = factorStr,
                        PasillosDisplay = $"{pasilloStr} mm",
                        EscalerasDisplay = $"{escStr} mm"
                    });
                }
            }
            else if (!IsMultiNormMode && PrimaryNormName.Equals("NSR-10", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var g in NormsNSR10.Groups)
                {
                    NormsNSR10.EgressWidthFactors.TryGetValue(g.Code, out var ewNsr);

                    string factorStr = g.FactorM2PerPerson.HasValue ? $"{g.FactorM2PerPerson.Value:0.#} m²/pers" : "Manual";
                    string pasilloStr = ewNsr.CorredoresMm.HasValue ? $"{ewNsr.CorredoresMm.Value:0.#}" : "Manual";
                    string escStr = ewNsr.EscalerasMm.HasValue ? $"{ewNsr.EscalerasMm.Value:0.#}" : "Manual";

                    items.Add(new NormItemCombined
                    {
                        Code = g.Code,
                        Name = g.Name,
                        FactorNsrDisplay = factorStr,
                        FactorNfpaDisplay = "— (Solo NSR-10)",
                        PasillosDisplay = $"{pasilloStr} mm",
                        EscalerasDisplay = $"{escStr} mm"
                    });
                }
            }
            else
            {
                var matchedNfpaGroups = new HashSet<OccupancyGroupInfo>();

                foreach (var g in NormsNSR10.Groups)
                {
                    NormsNSR10.EgressWidthFactors.TryGetValue(g.Code, out var ewNsr);

                    var gNfpa = FindNfpaGroupForNsr(g);
                    if (gNfpa != null) matchedNfpaGroups.Add(gNfpa);

                    double? factorNfpa = gNfpa?.FactorM2PerPerson;

                    double? ewCorredoresNfpa = null;
                    double? ewEscalerasNfpa = null;
                    if (gNfpa != null && NormsNFPA.EgressWidthFactors.TryGetValue(gNfpa.Code, out var ewNfpa))
                    {
                        ewCorredoresNfpa = ewNfpa.CorredoresMm;
                        ewEscalerasNfpa = ewNfpa.EscalerasMm;
                    }

                    string pasilloNsrStr = ewNsr.CorredoresMm.HasValue ? $"{ewNsr.CorredoresMm.Value:0.#}" : "Manual";
                    string pasilloNfpaStr = ewCorredoresNfpa.HasValue ? $"{ewCorredoresNfpa.Value:0.#}" : "Manual";

                    string escNsrStr = ewNsr.EscalerasMm.HasValue ? $"{ewNsr.EscalerasMm.Value:0.#}" : "Manual";
                    string escNfpaStr = ewEscalerasNfpa.HasValue ? $"{ewEscalerasNfpa.Value:0.#}" : "Manual";

                    items.Add(new NormItemCombined
                    {
                        Code = g.Code,
                        Name = g.Name,
                        FactorNsrDisplay = g.FactorM2PerPerson.HasValue ? $"{g.FactorM2PerPerson.Value:0.#} m²/pers" : "Manual",
                        FactorNfpaDisplay = factorNfpa.HasValue ? $"{factorNfpa.Value:0.#} m²/pers" : "Manual",
                        PasillosDisplay = $"{pasilloNsrStr} / {pasilloNfpaStr} mm",
                        EscalerasDisplay = $"{escNsrStr} / {escNfpaStr} mm"
                    });
                }

                if (NormsNFPA != null && NormsNFPA.Groups != null)
                {
                    foreach (var gNfpa in NormsNFPA.Groups)
                    {
                        if (matchedNfpaGroups.Contains(gNfpa)) continue;

                        NormsNFPA.EgressWidthFactors.TryGetValue(gNfpa.Code, out var ewNfpa);

                        string factorNfpaStr = gNfpa.FactorM2PerPerson.HasValue ? $"{gNfpa.FactorM2PerPerson.Value:0.#} m²/pers" : "Manual";
                        string pasilloNfpaStr = ewNfpa.CorredoresMm.HasValue ? $"{ewNfpa.CorredoresMm.Value:0.#}" : "Manual";
                        string escNfpaStr = ewNfpa.EscalerasMm.HasValue ? $"{ewNfpa.EscalerasMm.Value:0.#}" : "Manual";

                        items.Add(new NormItemCombined
                        {
                            Code = gNfpa.Code,
                            Name = gNfpa.Name,
                            FactorNsrDisplay = "Manual",
                            FactorNfpaDisplay = factorNfpaStr,
                            PasillosDisplay = $"Manual / {pasilloNfpaStr} mm",
                            EscalerasDisplay = $"Manual / {escNfpaStr} mm"
                        });
                    }
                }
            }

            return items;
        }

        private static OccupancyGroupInfo FindNsrGroupForNfpa(OccupancyGroupInfo nfpaGroup)
        {
            if (nfpaGroup == null || NormsNSR10 == null || NormsNSR10.Groups == null || NormsNSR10.Groups.Count == 0)
                return null;

            string code = nfpaGroup.Code?.Trim() ?? "";
            string name = nfpaGroup.Name?.Trim() ?? "";

            var match = NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            if (code.Equals("ALMACENAMIENTO", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("A", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Mercantil", StringComparison.OrdinalIgnoreCase))
            {
                if (name.IndexOf("nivel de calle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormsNSR10.Groups.FirstOrDefault(x => x.Name.IndexOf("Calle", StringComparison.OrdinalIgnoreCase) >= 0);
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("C-1", StringComparison.OrdinalIgnoreCase));
            }

            if (code.Equals("Negocios", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("I-4", StringComparison.OrdinalIgnoreCase) || x.Code.Equals("I-5", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Guarderías", StringComparison.OrdinalIgnoreCase) || code.Equals("Educacional", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("I-3.1", StringComparison.OrdinalIgnoreCase) || x.Code.Equals("I-3", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Salud", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("I-2", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Detención y correccional", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("I-1", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Industrial", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("F", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Residencial", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("R", StringComparison.OrdinalIgnoreCase));

            if (code.Equals("Reunión pública", StringComparison.OrdinalIgnoreCase))
                return NormsNSR10.Groups.FirstOrDefault(x => x.Code.Equals("L", StringComparison.OrdinalIgnoreCase));

            return null;
        }

        public static OccupancyGroupInfo MatchGroup_NSR10(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string clean = text.Trim();

            var match = NormsNSR10.Groups.FirstOrDefault(g =>
                g.DisplayText.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            return NormsNSR10.Groups.FirstOrDefault(g =>
                clean.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                g.Name.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static OccupancyGroupInfo MatchGroup_NFPA(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string clean = text.Trim();

            var match = NormsNFPA.Groups.FirstOrDefault(g =>
                g.DisplayText.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            return NormsNFPA.Groups.FirstOrDefault(g =>
                clean.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                g.Name.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static double? GetFactor_NSR10(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return null;
            string clean = groupCode.Trim();

            var matches = NormsNSR10.Groups.Where(g =>
                g.DisplayText.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase)).ToList();

            var validMatch = matches.FirstOrDefault(g => g.FactorM2PerPerson.HasValue && g.FactorM2PerPerson.Value > 0);
            if (validMatch != null) return validMatch.FactorM2PerPerson;

            var match = MatchGroup_NSR10(clean);
            if (match != null && match.FactorM2PerPerson.HasValue) return match.FactorM2PerPerson;

            var nfpaGroup = MatchGroup_NFPA(clean);
            if (nfpaGroup != null)
            {
                var nsrMatch = FindNsrGroupForNfpa(nfpaGroup);
                if (nsrMatch != null && nsrMatch.FactorM2PerPerson.HasValue) return nsrMatch.FactorM2PerPerson;
            }

            return match?.FactorM2PerPerson;
        }

        public static double? GetFactor_NFPA(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return null;
            string clean = groupCode.Trim();

            var matches = NormsNFPA.Groups.Where(g =>
                g.DisplayText.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase)).ToList();

            var validMatch = matches.FirstOrDefault(g => g.FactorM2PerPerson.HasValue && g.FactorM2PerPerson.Value > 0);
            if (validMatch != null) return validMatch.FactorM2PerPerson;

            var match = MatchGroup_NFPA(clean);
            if (match != null && match.FactorM2PerPerson.HasValue) return match.FactorM2PerPerson;

            var nsrMatch = MatchGroup_NSR10(clean);
            if (nsrMatch != null)
            {
                var nfpaMatch = FindNfpaGroupForNsr(nsrMatch);
                if (nfpaMatch != null && nfpaMatch.FactorM2PerPerson.HasValue) return nfpaMatch.FactorM2PerPerson;
            }

            return match?.FactorM2PerPerson;
        }

        public static int? CalculateCO_NSR10(double areaM2, string groupCode)
        {
            var factor = GetFactor_NSR10(groupCode);
            if (!factor.HasValue || factor.Value <= 0) return null;
            int co = (int)Math.Ceiling(areaM2 / factor.Value);
            return Math.Max(1, co);
        }

        public static int? CalculateCO_NFPA(double areaM2, string groupCode)
        {
            var factor = GetFactor_NFPA(groupCode);
            if (!factor.HasValue || factor.Value <= 0) return null;
            int co = (int)Math.Ceiling(areaM2 / factor.Value);
            return Math.Max(1, co);
        }

        public static (double? AnchoCorredoresMm, double? AnchoEscalerasMm) CalculateEgressWidths_Combined(int coNsr, int coNfpa, string groupShortCode)
        {
            if (string.IsNullOrWhiteSpace(groupShortCode)) return (null, null);

            double? pasillosNsr = null;
            double? escalerasNsr = null;
            if (NormsNSR10.EgressWidthFactors.TryGetValue(groupShortCode.Trim(), out var ewNsr))
            {
                if (ewNsr.CorredoresMm.HasValue) pasillosNsr = coNsr * ewNsr.CorredoresMm.Value;
                if (ewNsr.EscalerasMm.HasValue) escalerasNsr = coNsr * ewNsr.EscalerasMm.Value;
            }

            double? pasillosNfpa = null;
            double? escalerasNfpa = null;

            var nsrMatch = NormsNSR10.Groups.FirstOrDefault(g => g.Code.Equals(groupShortCode.Trim(), StringComparison.OrdinalIgnoreCase));
            var nfpaGroup = nsrMatch != null ? FindNfpaGroupForNsr(nsrMatch) : null;
            string nfpaCode = nfpaGroup?.Code ?? groupShortCode.Trim();

            if (NormsNFPA.EgressWidthFactors.TryGetValue(nfpaCode, out var ewNfpa))
            {
                if (ewNfpa.CorredoresMm.HasValue) pasillosNfpa = coNfpa * ewNfpa.CorredoresMm.Value;
                if (ewNfpa.EscalerasMm.HasValue) escalerasNfpa = coNfpa * ewNfpa.EscalerasMm.Value;
            }

            double? finalPasillos = MaxNullable(pasillosNsr, pasillosNfpa);
            double? finalEscaleras = MaxNullable(escalerasNsr, escalerasNfpa);

            return (finalPasillos, finalEscaleras);
        }

        private static double? MaxNullable(double? a, double? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return Math.Max(a.Value, b.Value);
        }

        public static (double? AnchoCorredoresMm, double? AnchoEscalerasMm) CalculateEgressWidths(int co, string groupShortCode)
        {
            return CalculateEgressWidths_Combined(co, co, groupShortCode);
        }

        public static double? FindFactor(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string clean = text.Trim();
            var match = NormsNSR10.Groups.FirstOrDefault(g => 
                g.DisplayText.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " ", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith(g.Code + " -", StringComparison.OrdinalIgnoreCase));

            return match?.FactorM2PerPerson;
        }

        public static string AbbreviateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "A";
            code = code.Trim();
            if (code.Length <= 5) return code;

            string upper = code.ToUpperInvariant();
            if (upper.StartsWith("ALMACENAMIENTO")) return "ALM";
            if (upper.StartsWith("MERCANTIL")) return "MER";
            if (upper.StartsWith("NEGOCIOS")) return "NEG";
            if (upper.StartsWith("GUARDER")) return "GUA";
            if (upper.StartsWith("DETENCION") || upper.StartsWith("DETENCIÓN")) return "DET";
            if (upper.StartsWith("EDUCACIONAL")) return "EDU";
            if (upper.StartsWith("SALUD")) return "SAL";
            if (upper.StartsWith("INDUSTRIAL")) return "IND";
            if (upper.StartsWith("RESIDENCIAL")) return "RES";
            if (upper.StartsWith("REUNION") || upper.StartsWith("REUNIÓN")) return "REU";

            int dash = code.IndexOf('-');
            if (dash > 0 && dash <= 5) return code.Substring(0, dash).Trim();

            return code.Length > 4 ? code.Substring(0, 4).ToUpperInvariant() : code.ToUpperInvariant();
        }

        public static string ExtractShortCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "A";

            string clean = text.Trim();

            var matchNsr = MatchGroup_NSR10(clean);
            if (matchNsr != null && matchNsr.Code.Length <= 5) return matchNsr.Code;

            var matchNfpa = MatchGroup_NFPA(clean);
            if (matchNfpa != null)
            {
                var nsrEquivalent = FindNsrGroupForNfpa(matchNfpa);
                if (nsrEquivalent != null && nsrEquivalent.Code.Length <= 5)
                {
                    return nsrEquivalent.Code;
                }
                return AbbreviateCode(matchNfpa.Code);
            }

            int dashIdx = clean.IndexOf('-');
            if (dashIdx > 0 && dashIdx <= 5) return clean.Substring(0, dashIdx).Trim();

            return AbbreviateCode(clean);
        }

        public static int? CalculateCO(double areaM2, string groupText)
        {
            return CalculateCO_NSR10(areaM2, groupText);
        }

        public static string GetCanonicalPisoKey(string piso)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(piso)) return "N1";

                string clean = piso.Trim();

                string norm = System.Text.RegularExpressions.Regex.Replace(clean, @"^(piso|nivel|nv|level)\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                if (int.TryParse(norm, out int n))
                {
                    return $"N{n}";
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"^[nN][-_\s]*\d+$"))
                {
                    string digits = System.Text.RegularExpressions.Regex.Match(norm, @"\d+").Value;
                    return $"N{digits}";
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(norm, @"^[sS](ub)?[-_\s]*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    string digits = System.Text.RegularExpressions.Regex.Match(norm, @"\d+").Value;
                    return $"S{digits}";
                }

                return norm.ToUpperInvariant();
            }
            catch
            {
                return (piso ?? "N1").Trim().ToUpperInvariant();
            }
        }
    }
}
