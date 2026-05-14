using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace MiNamespace.ValidadorParametros
{
    // Lee el archivo ParametrosRequeridos.xlsx usando ClosedXML (no requiere Excel instalado).
    // Estructura esperada del Excel (hoja 1):
    //   Col A: Disciplina | Col B: Categoria | Col C: Nombre_Parametro | Col D: Requerido (SI/NO) | Col E: Descripcion
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
                    if (row.RowNumber() == 1) continue; // saltar encabezado

                    string disciplina = row.Cell(1).GetString().Trim();
                    string categoria  = row.Cell(2).GetString().Trim();
                    string nombre     = row.Cell(3).GetString().Trim();
                    string reqStr     = row.Cell(4).GetString().Trim();
                    string descripcion = row.Cell(5).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(disciplina) || string.IsNullOrWhiteSpace(nombre))
                        continue;

                    bool requerido = reqStr.Equals("SI", StringComparison.OrdinalIgnoreCase)
                                  || reqStr.Equals("YES", StringComparison.OrdinalIgnoreCase)
                                  || reqStr == "1";

                    result.Add(new DisciplineParameter
                    {
                        Disciplina     = disciplina,
                        Categoria      = categoria,
                        NombreParametro = nombre,
                        Requerido      = requerido,
                        Descripcion    = descripcion
                    });
                }
            }

            return result;
        }

        // Crea un archivo de plantilla con datos de ejemplo para que el usuario lo complete.
        public static void CrearPlantilla(string destPath)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Parametros");

                // Encabezados
                ws.Cell(1, 1).Value = "Disciplina";
                ws.Cell(1, 2).Value = "Categoria";
                ws.Cell(1, 3).Value = "Nombre_Parametro";
                ws.Cell(1, 4).Value = "Requerido";
                ws.Cell(1, 5).Value = "Descripcion";

                var headerRange = ws.Range(1, 1, 1, 5);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A237E");
                headerRange.Style.Font.FontColor = XLColor.White;

                // Filas de ejemplo
                var ejemplos = new[]
                {
                    new[]{ "HVAC",       "Ducts",             "Marca",            "SI",  "Marca del fabricante" },
                    new[]{ "HVAC",       "Ducts",             "Modelo",           "SI",  "Modelo del ducto" },
                    new[]{ "HVAC",       "Duct Fittings",     "Marca",            "SI",  "Marca del accesorio" },
                    new[]{ "HIDRAULICO", "Pipes",             "Material",         "SI",  "Material de la tubería" },
                    new[]{ "HIDRAULICO", "Pipes",             "Marca",            "SI",  "Marca del fabricante" },
                    new[]{ "HIDRAULICO", "Pipe Fittings",     "Material",         "SI",  "Material del accesorio" },
                    new[]{ "ELECTRICO",  "Electrical Equipment", "Marca",         "SI",  "Marca del equipo" },
                    new[]{ "ELECTRICO",  "Electrical Equipment", "Modelo",        "SI",  "Modelo del equipo" },
                    new[]{ "ARQ",        "Walls",             "Tipo de Acabado",  "NO",  "Acabado superficial del muro" },
                    new[]{ "ARQ",        "Doors",             "Marca",            "NO",  "Fabricante de la puerta" },
                };

                for (int i = 0; i < ejemplos.Length; i++)
                {
                    for (int c = 0; c < 5; c++)
                        ws.Cell(i + 2, c + 1).Value = ejemplos[i][c];
                }

                ws.Columns().AdjustToContents();
                workbook.SaveAs(destPath);
            }
        }
    }
}
