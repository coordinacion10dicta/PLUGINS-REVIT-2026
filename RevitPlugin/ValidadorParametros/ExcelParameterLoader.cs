using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace MiNamespace.ValidadorParametros
{
    // Lee ParametrosRequeridos.xlsx con ClosedXML (sin Excel instalado).
    // Estructura hoja 1:
    //   A: Disciplina | B: Categoria | C: Parámetro | D: Alcance (Tipo/Instancia) | E: Obligatorio (Sí/No) | F: Descripcion
    public static class ExcelParameterLoader
    {
        public static List<DisciplineParameter> Load(string excelPath)
        {
            if (!File.Exists(excelPath))
                throw new FileNotFoundException($"No se encontró el archivo de parámetros:\n{excelPath}");

            var result = new List<DisciplineParameter>();

            using (var workbook = new XLWorkbook(excelPath))
            {
                var ws = workbook.Worksheet(1);
                var usedRange = ws.RangeUsed();
                if (usedRange == null) return result;

                foreach (var row in usedRange.RowsUsed())
                {
                    if (row.RowNumber() == 1) continue;

                    string disciplina  = row.Cell(1).GetString().Trim();
                    string categoria   = row.Cell(2).GetString().Trim();
                    string nombre      = row.Cell(3).GetString().Trim();
                    string alcance     = row.Cell(4).GetString().Trim();
                    string obligStr    = row.Cell(5).GetString().Trim();
                    string descripcion = row.Cell(6).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(disciplina) || string.IsNullOrWhiteSpace(nombre))
                        continue;

                    // Default Alcance to "Instancia" when not specified
                    if (string.IsNullOrWhiteSpace(alcance))
                        alcance = "Instancia";

                    bool obligatorio = obligStr.Equals("SI",  StringComparison.OrdinalIgnoreCase)
                                    || obligStr.Equals("SÍ",  StringComparison.OrdinalIgnoreCase)
                                    || obligStr.Equals("YES", StringComparison.OrdinalIgnoreCase)
                                    || obligStr == "1";

                    result.Add(new DisciplineParameter
                    {
                        Disciplina      = disciplina,
                        Categoria       = categoria,
                        NombreParametro = nombre,
                        Alcance         = alcance,
                        Obligatorio     = obligatorio,
                        Descripcion     = descripcion
                    });
                }
            }

            return result;
        }

        public static void CrearPlantilla(string destPath)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Parametros");

                // Headers
                string[] headers = { "Disciplina", "Categoria", "Parámetro", "Alcance", "Obligatorio", "Descripcion" };
                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
                headerRange.Style.Font.FontColor = XLColor.White;

                var ejemplos = new[]
                {
                    new[]{ "HVAC",       "Mechanical Equipment", "PTO_CodigoCosto",  "Tipo",      "Sí",  "Código de costo para presupuesto" },
                    new[]{ "HVAC",       "Mechanical Equipment", "PTO_Marca",        "Instancia", "Sí",  "Marca del equipo" },
                    new[]{ "HVAC",       "Air Terminals",        "PTO_Sector",       "Instancia", "Sí",  "Sector o zona" },
                    new[]{ "HVAC",       "Ducts",                "PTO_Sistema",      "Tipo",      "Sí",  "Sistema mecánico" },
                    new[]{ "Plumbing",   "Pipe Accessories",     "PTO_Sector",       "Instancia", "Sí",  "Sector hidráulico" },
                    new[]{ "Plumbing",   "Pipes",                "PTO_Material",     "Tipo",      "Sí",  "Material de la tubería" },
                    new[]{ "Plumbing",   "Plumbing Fixtures",    "PTO_CodigoCosto",  "Tipo",      "Sí",  "Código de costo" },
                    new[]{ "Electrical", "Electrical Equipment", "PTO_CodigoCosto",  "Tipo",      "Sí",  "Código de costo eléctrico" },
                    new[]{ "Electrical", "Electrical Equipment", "PTO_Circuito",     "Instancia", "Sí",  "Número de circuito" },
                    new[]{ "ARQ",        "Doors",                "PTO_CodigoCosto",  "Tipo",      "No",  "Código de presupuesto" },
                    new[]{ "ARQ",        "Walls",                "PTO_Acabado",      "Tipo",      "No",  "Tipo de acabado" },
                };

                for (int i = 0; i < ejemplos.Length; i++)
                    for (int c = 0; c < ejemplos[i].Length; c++)
                        ws.Cell(i + 2, c + 1).Value = ejemplos[i][c];

                // Dropdowns for Alcance and Obligatorio
                var alcanceRange = ws.Range(2, 4, ejemplos.Length + 10, 4);
                alcanceRange.SetDataValidation().List("\"Tipo,Instancia\"");

                var obligRange = ws.Range(2, 5, ejemplos.Length + 10, 5);
                obligRange.SetDataValidation().List("\"Sí,No\"");

                ws.Columns().AdjustToContents();
                workbook.SaveAs(destPath);
            }
        }
    }
}
