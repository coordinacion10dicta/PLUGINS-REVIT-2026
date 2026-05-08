using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Excel = Microsoft.Office.Interop.Excel;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyCommandPreDim : IExternalCommand
    {
        private readonly List<ElementId> _vitrLines = new List<ElementId>();
        // ► Datos que el usuario escribirá para el encabezado
        private string _proyecto, _disciplina, _etapa, _fecha;

        public Result Execute(ExternalCommandData commandData,
                              ref string message,
                              ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Pregunta inicial: continuar o salir ───────────────────────────────
            var dlg = new TaskDialog("Predimensionamiento");
            dlg.MainInstruction = "Se ha iniciado el plugin de predimensionamiento ";
            dlg.MainContent = "QUIERES?:";
            dlg.CommonButtons = TaskDialogCommonButtons.None;      // sin botones por defecto
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                               "Continuar");                         // ✔ Sigue con el flujo
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                               "Salir del plugin");                  // ❌ Sale sin hacer nada

            TaskDialogResult res = dlg.Show();

            if (res == TaskDialogResult.CommandLink2)                // «Salir del plugin»
                return Result.Cancelled;     // ← aborta todo el comando

            /* ─── 0. ¿Proyecto residencial? ─────────────────────────────────── */
            bool esResidencial = MessageBox.Show(
                "¿Este proyecto es de RESIDENCIAS Y VIVIENDAS?",
                "Tipo de Proyecto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            /* ─── 1. Diccionario CSV/XLSX ───────────────────────────────────── */
            string dicFile;
            using (var ofd = new OpenFileDialog
            {
                Filter = "CSV o Excel|*.csv;*.xlsx",
                Title = "Selecciona el archivo con el diccionario de cargas"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;
                dicFile = ofd.FileName;
            }

            var dic = CargarDiccionarioDesdeArchivo(dicFile);
            if (dic.Count == 0)
            {
                TaskDialog.Show("Error", "No se pudo cargar el diccionario desde el archivo.");
                return Result.Failed;
            }
            // ─── Datos de cabecera ────────────────────────────────
            _proyecto = Microsoft.VisualBasic.Interaction.InputBox("Nombre del proyecto:", "Proyecto");
            _disciplina = Microsoft.VisualBasic.Interaction.InputBox("Disciplina:", "Disciplina", "ELÉCTRICO");
            _etapa = Microsoft.VisualBasic.Interaction.InputBox("Etapa:", "Etapa", "CONCEPTO");

            string f = Microsoft.VisualBasic.Interaction.InputBox(
                             "Fecha (AAAA/MM/DD):", "Fecha",
                             DateTime.Today.ToString("yyyy/MM/dd"));
            _fecha = string.IsNullOrWhiteSpace(f) ? DateTime.Today.ToString("yyyy/MM/dd") : f;


            /* ─── 2. Flujo residencial se maneja aparte ─────────────────────── */
            if (esResidencial)
            {
                EjecutarFlujoResidencial(doc, dic, dicFile);
                return Result.Succeeded;   // el borrado temporal se hace allí
            }

            /* ─── 3. Flujo NO residencial ───────────────────────────────────── */
            var manual = new List<PredimResult>();
            if (MessageBox.Show("¿Desea agregar cargas especiales (HVAC, UPS, etc)?",
                                "Cargas especiales",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                manual = ObtenerEntradasManual();
            }

            var results = ObtenerSpaces(doc, dic);
            results.InsertRange(0, manual);

            if (results.Count == 0)
            {
                TaskDialog.Show("Espacios", "No se encontraron Spaces o no coincidieron los usos.");
                return Result.Succeeded;
            }

            /* ─── 4. Aire acondicionado ─────────────────────────────────────── */
            double acLoadKVA = 0;
            if (MessageBox.Show("¿El proyecto tiene aire acondicionado?",
                                "Aire acondicionado",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string sKva = Microsoft.VisualBasic.Interaction.InputBox(
                                  "Carga total de aire acondicionado (kVA)",
                                  "Aire acondicionado", "0");
                double.TryParse(sKva.Replace(",", "."), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out acLoadKVA);
            }

            // --- 5. Vitrinismo ----------------------------------------------------
            bool tieneVitri = false;                // ←  deja esto
            if (MessageBox.Show(                    // ←  CAMBIA while (…) por if (…)
                    "¿Hay vitrinismo lineal?",
                    "Vitrinismo",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (PreguntarYAgregarVitrinismo(doc, dic, results))
                    tieneVitri = true;
            }

            // ─── 6. Porcentaje de diversificación ─────────────────────────────
            double pctNorm = PedirPorcentajeDiversificado(70);   // 70 % es la sugerencia
            if (pctNorm < 0) return Result.Cancelled;            // usuario canceló la ventana


            /* ─── 7. Resumen y exportación ───────────────────────────────────── */
            MostrarResumen(results);
            ExportarAExcel(results, pctNorm, acLoadKVA, tieneVitri);

            /* ─── 8. Borrar las líneas rojas temporales ─────────────────────── */
            if (_vitrLines.Count > 0)
            {
                using (var t = new Transaction(doc, "Eliminar vitrinismo temporal"))
                {
                    t.Start();
                    doc.Delete(_vitrLines);
                    t.Commit();
                }
                _vitrLines.Clear();
            }

            return Result.Succeeded;
        }


        private double LeerKvaPorUsuarioDeExcel(string excelPath, int estrato, int usuariosTotales)
        {
            double resultado = 0;
            var xl = new Excel.Application();
            var wb = xl.Workbooks.Open(excelPath);
            Excel.Worksheet ws = null;

            try
            {
                /* 1️⃣  Comprobar que exista la hoja 2  */
                if (wb.Sheets.Count < 2)
                {
                    TaskDialog.Show("Archivo inválido",
                        "El Excel debe contener al menos dos hojas:\n" +
                        "  • Hoja 1  →  Diccionario VA/m²\n" +
                        "  • Hoja 2  →  Tablas por estrato");
                    return 0;                     // aborta antes de acceder a Sheets[2]
                }

                // Hoja 2 garantizada
                ws = (Excel.Worksheet)wb.Sheets[2];

                /* 2️⃣  Lógica original (tal cual) */
                int rowStart = 1;

                // Localizar la tabla del estrato
                for (int r = 1; r <= 200; r++)
                {
                    var cell = (ws.Cells[r, 2] as Excel.Range).Text.ToString();
                    if (cell == $"ESTRATO {estrato}")
                    {
                        rowStart = r + 3;           // primera fila de valores
                        break;
                    }
                }

                // Buscar la fila de usuarios totales
                for (int fila = rowStart; fila <= rowStart + 20; fila++)
                {
                    var usuariosCell = (ws.Cells[fila, 2] as Excel.Range).Text.ToString();
                    if (int.TryParse(usuariosCell, out int usuarios) &&
                        usuarios == usuariosTotales)
                    {
                        var kvaCell = (ws.Cells[fila, 3] as Excel.Range).Text.ToString();
                        double.TryParse(kvaCell, out resultado);
                        break;
                    }
                }
            }
            finally
            {
                wb.Close(false); xl.Quit();
                if (ws != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);
            }

            return resultado;   // 0 si algo no coincidió o faltaba la hoja
        }


        // El cuerpo de la tabla de cargas comenzará en la fila 8
        private const int HEADER_ROWS = 7;
        /* ─── Carga unitaria de vitrinismo ──────────────────────────────────────────
       0,65 kVA por metro lineal (valor que venías usando). 
       Si mañana cambia, sólo actualizas este número. */
        private const double KVA_PER_M = 0.65;
        /* --------------------------------------------------------- */
        /// Inserta logo, título, rótulo derecho y cuadro de datos centrales
        private void InsertarEncabezadoExcel(
            Excel.Worksheet ws,
            string cod = "Cód COO-FR-006",
            string version = "2.0")
        {
            ws.Cells.Font.Name = "Arial Narrow";
            ws.Cells.Font.Size = 10;

            /* 1. LOGO (A1:B3) ---------------------------------------------------- */
            ws.Range["A1:B3"].Merge();

            /* 2. TÍTULO (C1:I3) -------------------------------------------------- */
            var titulo = ws.Range["C1:I3"];
            titulo.Merge();
            titulo.Value2 = "MEMORIAS DE CÁLCULO ANÁLISIS INICIAL DE CARGA";
            titulo.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            titulo.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
            titulo.Font.Bold = true;
            titulo.Font.Size = 14;
            titulo.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            titulo.Borders[Excel.XlBordersIndex.xlEdgeBottom].Weight =
                Excel.XlBorderWeight.xlMedium;

            /* 3. Fila vacía debajo del título (fila 4) --------------------------- */
            ws.Rows["4"].Insert(Excel.XlInsertShiftDirection.xlShiftDown);

            /* ─── 4. FECHAS ────────────────────────────────────────────────────── */
            // ► Fecha escrita por el usuario (InputBox)
            DateTime fechaUsuario;
            if (!DateTime.TryParseExact(
                    _fecha,
                    new[] { "yyyy/MM/dd", "dd/MM/yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fechaUsuario))
                fechaUsuario = DateTime.Today;

            // ► Fecha del día en que se genera el Excel
            DateTime fechaSistema = DateTime.Today;

            /* 5. RÓTULO DERECHO (J1:K3) ----------------------------------------- */
            ws.Range["J1:K1"].Merge();
            ws.Range["J2:K2"].Merge();
            ws.Range["J3:K3"].Merge();

            ws.Range["J1"].Value2 = cod;

            var celdaFechaRotulo = ws.Range["J2:K2"];
            celdaFechaRotulo.Value2 = $"Fecha : {fechaSistema:d/M/yyyy}";
            celdaFechaRotulo.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            celdaFechaRotulo.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;

            ws.Range["J3"].Value2 = $"Versión: {version}";

            ws.Range["J1:K3"].Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            ws.Range["J1:K3"].HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            ws.Range["J1:K3"].VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;

            /* ────────────────────────────────────────────────────────────────────
               CUADRO PROYECTO / DISCIPLINA / ETAPA / FECHA  (A5:K6)
               ──────────────────────────────────────────────────────────────────── */
            ws.Range["A5:K6"].Borders.LineStyle = Excel.XlLineStyle.xlContinuous;

            // Fila 5
            ws.Range["A5:B5"].Merge(); ws.Range["A5"].Value2 = "PROYECTO";
            ws.Range["C5:E5"].Merge(); ws.Range["C5"].Value2 = _proyecto.ToUpper();

            ws.Range["F5:G5"].Merge(); ws.Range["F5"].Value2 = "ETAPA";
            ws.Range["H5:K5"].Merge(); ws.Range["H5"].Value2 = _etapa.ToUpper();

            // Fila 6
            ws.Range["A6:B6"].Merge(); ws.Range["A6"].Value2 = "DISCIPLINA";
            ws.Range["C6:E6"].Merge(); ws.Range["C6"].Value2 = _disciplina.ToUpper();

            ws.Range["F6:G6"].Merge(); ws.Range["F6"].Value2 = "FECHA";
            var celdaFechaCuadro = ws.Range["H6:K6"];
            celdaFechaCuadro.Merge();
            celdaFechaCuadro.Value = fechaUsuario;          // almacena DateTime
            celdaFechaCuadro.NumberFormat = "d/m/yyyy";

            // Bordes interiores y formato común
            ws.Range["A5:K6"].Borders[Excel.XlBordersIndex.xlInsideVertical].LineStyle =
            ws.Range["A5:K6"].Borders[Excel.XlBordersIndex.xlInsideHorizontal].LineStyle =
                Excel.XlLineStyle.xlContinuous;

            ws.Range["A5:K6"].HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            ws.Range["A5:K6"].VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
            // Solo poner en negrilla los rótulos (cabeceras)
            ws.Range["A5"].Font.Bold = true;
            ws.Range["F5"].Font.Bold = true;
            ws.Range["A6"].Font.Bold = true;
            ws.Range["F6"].Font.Bold = true;

        }




        private void EjecutarFlujoResidencial(Document doc, Dictionary<string, double> dic, string dicFile)

        {
            // === 1. CARGAS MANUALES GENERALES ===
            var manual = ObtenerEntradasManual();

            // === 2. SPACES AUTOMÁTICOS COMPLETOS ===
            var results = ObtenerSpaces(doc, dic);
            results.InsertRange(0, manual);

            // === 3. AIRE ACONDICIONADO ===
            double acLoadKVA = PreguntarAireAcondicionado();


            // ─── VITRINISMO EN VARIAS VISTAS / NIVELES ───────────────────────────
            bool tieneVitri = false;                // ←  igual que arriba
            if (MessageBox.Show(
                    "¿Hay vitrinismo lineal?",
                    "Vitrinismo",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (PreguntarYAgregarVitrinismo(doc, dic, results))
                    tieneVitri = true;
            }



            // === 5. Porcentaje de diversificación ===
            // ─── 5. Porcentaje de diversificación ─────────────────────────────
            // 5. Porcentaje de diversificación ── dos valores
            double pctZonasComunes = PedirPorcentajeDiversificado(40); // sugerencia 40 %
            if (pctZonasComunes < 0) return;


            // === 6. Separar ZONAS COMUNES y APARTAMENTOS ===
            var apartamentos = results.FindAll(r =>
                r.Uso.Equals("Apto", StringComparison.OrdinalIgnoreCase) ||
                r.Uso.Equals("Apartamento", StringComparison.OrdinalIgnoreCase)
            );

            var zonasComunes = results.FindAll(r =>
                !r.Uso.Equals("Apto", StringComparison.OrdinalIgnoreCase) &&
                !r.Uso.Equals("Apartamento", StringComparison.OrdinalIgnoreCase)
            );

            // === 7. Usuarios totales: calculado automáticamente ===
            int usuariosTotales = apartamentos.Count;

            // 7.5) Preguntar el estrato
            // 7.5) Preguntar el estrato (validado 1-6)
            int estrato = PedirEstrato();
            if (estrato == -1) return;   // el usuario canceló → abortar comando

            // 7.6) Leer valor KVA/USUARIO de la tabla de estrato del archivo Excel
            double kvaPorUsuario = LeerKvaPorUsuarioDeExcel(dicFile, estrato, usuariosTotales);

            // ▼ CALCULAR el transformador automáticamente
            double transformadorKVA = SeleccionarTransformador(
                    dicFile,           // Excel que seleccionó el usuario
                    estrato,           // estrato 1-6
                    kvaPorUsuario,     // kVA / usuario obtenido (puede ser 0)
                    usuariosTotales);  // nº apartamentos


            // === 8. Cargas especiales adicionales para residencias ===
            var especialesApto = ObtenerEntradasManualResidencial();

            // ── limpiar vitrinismo temporal ──
            double pctResidencial = PedirPorcentajeDiversificado(50); // sugerencia 50 %
            if (pctResidencial < 0) return;

            // usuario canceló
            // 9. Exportar ambas hojas -------------------------------------
            ExportarResidencialAExcel(
                zonasComunes,
                apartamentos,
                especialesApto,
                usuariosTotales,
                kvaPorUsuario,
                pctResidencial,      // ← para la hoja 2
                acLoadKVA,
                tieneVitri,
                transformadorKVA,
                pctZonasComunes);


            if (_vitrLines.Count > 0)
            {
                using (var t = new Transaction(doc, "Eliminar vitrinismo temporal"))
                {
                    t.Start();
                    doc.Delete(_vitrLines);
                    t.Commit();
                }
                _vitrLines.Clear();
            }


        }


        private List<PredimResult> ObtenerEntradasManualResidencial()
        {
            var lista = new List<PredimResult>();

            while (true)
            {
                /* 1️⃣  Nombre de la carga especial */
                string nombre = Microsoft.VisualBasic.Interaction.InputBox(
                                    "Nombre de la carga especial (ej: CARGADORES VEHICULARES)",
                                    "Carga Especial Residencial");

                if (string.IsNullOrWhiteSpace(nombre))
                    break;                                   // cancelar / terminar

                /* 2️⃣  Carga total en kVA – SOLO NÚMEROS */
                double kva = 0;
                while (true)
                {
                    string sKva = Microsoft.VisualBasic.Interaction.InputBox(
                                       $"¿Cuál es la carga total para '{nombre}' en kVA? (solo número)",
                                       "Carga (kVA)", "0");

                    /* Usuario canceló esta ventana → volver a pedir nombre */
                    if (string.IsNullOrWhiteSpace(sKva))
                        goto PreguntarOtroNombre;

                    /* ¿Texto numérico y > 0? */
                    if (double.TryParse(sKva.Replace(",", "."),
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out kva) && kva > 0)
                        break;                               // ✅ dato correcto

                    /* Mensaje de error y repite la pregunta */
                    MessageBox.Show(
                        "Por favor digita un valor numérico positivo en kVA.",
                        "Valor inválido",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                /* 3️⃣  Añadir el registro */
                lista.Add(new PredimResult
                {
                    AreaName = nombre,
                    Uso = nombre,
                    AreaM2 = 0,
                    VaPorM2 = 0,
                    CargaKVA = kva,
                    CargaVA = kva * 1000,
                    EsManual = true
                });

            PreguntarOtroNombre:
                /* 4️⃣  ¿Agregar otra carga? */
                if (MessageBox.Show("¿Deseas agregar otra carga especial residencial?",
                                    "Continuar",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question) != DialogResult.Yes)
                    break;
            }

            return lista;
        }



private double PreguntarAireAcondicionado()
{
    // 1. ¿Hay aire acondicionado?
    if (MessageBox.Show("¿El proyecto tiene aire acondicionado?",
                        "Aire acondicionado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question)
        != DialogResult.Yes)
        return 0;   // el usuario contestó “No”

    // 2. Bucle de validación: solo números positivos
    while (true)
    {
        string sKva = Microsoft.VisualBasic.Interaction.InputBox(
                          "Carga total de aire acondicionado (kVA)\n" +
                          "⚠️  Ingresa únicamente un número positivo.",
                          "Aire acondicionado", "0");

        // Cancelar o entrada vacía => sin carga
        if (string.IsNullOrWhiteSpace(sKva))
            return 0;

        // ¿Número válido y > 0?
        if (double.TryParse(sKva.Replace(",", "."),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out double kva) && kva > 0)
            return kva;   // ✓ dato correcto

        // Si no es válido, muestra error y repite
        MessageBox.Show("Por favor digita un valor numérico positivo en kVA.",
                        "Valor inválido",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
    }
}

        private int PedirEstrato()
        {
            while (true)
            {
                string texto = Microsoft.VisualBasic.Interaction.InputBox(
                    "¿Número de estrato residencial? (1 a 6)",
                    "Estrato Residencial", "4");

                if (string.IsNullOrWhiteSpace(texto)) return -1;   // Cancelar

                if (int.TryParse(texto.Trim(), out int estrato) && estrato >= 1 && estrato <= 6)
                    return estrato;

                MessageBox.Show(
                    "Por favor digita un número de estrato válido (1 a 6).",
                    "Valor inválido",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private bool PreguntarYAgregarVitrinismo(
            Document doc, Dictionary<string, double> dic, List<PredimResult> results)
        {
            UIDocument uidoc = new UIDocument(doc);

            while (true)
            {
                /* ───────────── ELECCIÓN DEL MODO ───────────── */
                var resp = MessageBox.Show(
                    "¿El tramo es LINEAL?\n\n" +
                    "    Sí  →  Seleccionaré dos puntos.\n" +
                    "    No  →  Digitaré un perímetro (círculo / óvalo / especial).",
                    "Tipo de vitrinismo",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (resp == DialogResult.Cancel) break;

                double lengthM;

                if (resp == DialogResult.Yes)                // LINEAL
                {
                    /* 1️⃣  Elegir puntos */
                    XYZ p1 = uidoc.Selection.PickPoint(ObjectSnapTypes.None,
                                                       "Punto inicial vitrinismo");
                    XYZ p2 = uidoc.Selection.PickPoint(ObjectSnapTypes.None,
                                                       "Punto final vitrinismo");

                    lengthM = UnitUtils.ConvertFromInternalUnits(
                        p1.DistanceTo(p2),
#if REVIT2022_OR_LATER
                UnitTypeId.Meters
#else
                        DisplayUnitType.DUT_METERS
#endif
                    );

                    /* 1-bis. Dibujar la línea roja temporal (igual que antes) */
                    // … (código que ya tenías para SketchPlane y línea) …
                }
                else                                         // PERÍMETRO MANUAL
                {
                    double? per = PedirPerimetroEspecial();
                    if (per == null) break;   // cancelado

                    lengthM = per.Value;      // no se dibuja nada en pantalla
                }

                /* 2️⃣  Pedir NOMBRE (obligatorio / único) */
                string nombreVit;
                while (true)
                {
                    nombreVit = Microsoft.VisualBasic.Interaction.InputBox(
                        "Nombre del tramo de vitrinismo (obligatorio y único):",
                        "Vitrinismo");

                    if (nombreVit == "")               // cancelado
                    {
                        // si se dibujó línea temporal, bórrala aquí…
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(nombreVit))
                    {
                        MessageBox.Show("Debes digitar un nombre.",
                                        "Nombre obligatorio",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Error);
                        continue;
                    }

                    bool existe = results.Exists(r =>
                        r.Uso.Equals("Vitrinismo", StringComparison.OrdinalIgnoreCase) &&
                        r.AreaName.Equals(nombreVit, StringComparison.OrdinalIgnoreCase));

                    if (existe)
                    {
                        MessageBox.Show("Ya existe un tramo con ese nombre.",
                                        "Nombre duplicado",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Error);
                        continue;
                    }
                    break;
                }

                /* 3️⃣  Registrar */
                double vitriKVA = lengthM * KVA_PER_M;

                results.Add(new PredimResult
                {
                    AreaName = nombreVit,
                    Uso = "Vitrinismo",
                    AreaM2 = lengthM,
                    VaPorM2 = 0,
                    CargaKVA = vitriKVA,
                    CargaVA = vitriKVA * 1000,
                    EsManual = true
                });

                /* 4️⃣  ¿Otro tramo? */
                if (MessageBox.Show("¿Agregar otro tramo de vitrinismo?",
                                    "Continuar",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question)
                    != DialogResult.Yes)
                    break;
            }

            return true;
        }


        private void ExportarResidencialAExcel(
                List<PredimResult> zonasComunes,
                List<PredimResult> apartamentos,
                List<PredimResult> especiales,
                int usuariosTotales,
                double kvaPorUsuario,
                double pctResidencial,    // ← porcentaje que se aplicará a la Hoja 2
                double acLoadKVA,
                bool tieneVitri,
                double transformadorKVA,
                double pctZonasComunes)   // ← porcentaje que se aplicará a la Hoja 1

        {
            /* 1️⃣  Ruta de guardado ------------------------------------------------ */
            string filePath = null;
            using (var sfd = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                Title = "Guardar Excel"
            })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                    filePath = sfd.FileName;
            }
            if (string.IsNullOrEmpty(filePath)) return;

            /* 2️⃣  Crear Excel y libro -------------------------------------------- */
            var xl = new Excel.Application { Visible = false };
            var wb = xl.Workbooks.Add(Type.Missing);

            /* 3️⃣  HOJA 1 – Zonas comunes ----------------------------------------- */
            var ws1 = (Excel.Worksheet)wb.Sheets[1];
            ws1.Name = "CargasEspacios";

            int filaCargaDiv = ExportarAHojaComun(ws1,
                                                     zonasComunes,
                                                     pctZonasComunes,
                                                     acLoadKVA,
                                                     tieneVitri);


            /* 4️⃣  HOJA 2 – Apartamentos ------------------------------------------ */
            var ws2 = (Excel.Worksheet)wb.Sheets.Add(After: ws1);
            ws2.Name = "CargasResidenciales";

            InsertarEncabezadoExcel(ws2);   // aquí la fecha queda bien

            ws2.Cells.NumberFormat = "0.00";               // esto vuelve a poner todo en “0.00”
            ws2.Range["H6:K6"].NumberFormat = "d/m/yyyy";  // ← restablece el formato de la fecha

            /* --  Cabeceras ------------------------------------------------------- */
            var headers = new List<string>
    {
        "ZONA", "ÁREA", "USUARIOS TOTALES", "KVA/USUARIO", "CARGA (kVA)"
    };
            int colInicio = 4; // ← columna D

            int headerRow = HEADER_ROWS + 2;    // (7 + 1) = 8
            int firstData = headerRow + 1;     // 9
            int row = firstData;
            // 🔶 Insertar encabezado "RESIDENCIAS Y VIVIENDAS"
            string ultimaCol = ColumnNumberToLetter(colInicio + headers.Count - 1);  // Asegúrate de tener esta función
            int tituloRow = headerRow - 1;    // Una fila antes del header

            var rngTitulo = ws2.Range[$"{ColumnNumberToLetter(colInicio)}{tituloRow}:{ultimaCol}{tituloRow}"];
            rngTitulo.Merge();
            rngTitulo.Value2 = "RESIDENCIAS Y VIVIENDAS";
            rngTitulo.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            rngTitulo.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
            rngTitulo.Font.Bold = true;
            rngTitulo.Font.Size = 12;
            rngTitulo.Interior.Color = System.Drawing.ColorTranslator.ToOle(
                                            System.Drawing.Color.FromArgb(0xFF, 0xC0, 0x00));  // Color #FFC000
            rngTitulo.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            rngTitulo.Borders.Weight = Excel.XlBorderWeight.xlMedium;

            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws2.Cells[headerRow, colInicio + c];
                cell.Value2 = headers[c];
                cell.Font.Bold = true;
            }



            /* -- 1. Apartamentos -------------------------------------------------- */
                int aptoStartRow = row;
                foreach (var apto in apartamentos)
                {
                    ws2.Cells[row, colInicio + 0] = apto.AreaName;
                    ws2.Cells[row, colInicio + 1] = apto.AreaM2;
                    row++;
                }
                int aptoEndRow = row - 1;




            /* -- 2. Cargas especiales adicionales -------------------------------- */
            foreach (var esp in especiales)
            {
                ws2.Cells[row, colInicio + 0] = esp.AreaName;
                ws2.Cells[row, colInicio + 1] = esp.AreaM2;
                ws2.Cells[row, colInicio + 4] = esp.CargaKVA;
                row++;
            }


            if (apartamentos.Count > 0)
            {
                int filaUltima = aptoEndRow; // ✅ Solo hasta el último apartamento

                // USUARIOS TOTALES (merge)
                var rngUsr = ws2.Range[ws2.Cells[aptoStartRow, colInicio + 2], ws2.Cells[filaUltima, colInicio + 2]];
                rngUsr.ClearContents(); // 🔑 Limpia antes del merge
                rngUsr.Merge();
                rngUsr.Value2 = usuariosTotales;
                rngUsr.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                rngUsr.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
                rngUsr.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                rngUsr.Borders.Weight = Excel.XlBorderWeight.xlThin;

                // KVA/USUARIO (merge)
                var rngKva = ws2.Range[ws2.Cells[aptoStartRow, colInicio + 3], ws2.Cells[filaUltima, colInicio + 3]];
                rngKva.ClearContents(); // 🔑 Limpia antes del merge
                rngKva.Merge();

                string celdaDiv = $"\'CargasEspacios\'!F{filaCargaDiv}";
                string celdaUsr = $"{ColumnNumberToLetter(colInicio + 2)}{aptoStartRow}";
                string sep = ws2.Application.International[Excel.XlApplicationInternational.xlListSeparator].ToString();

                if (sep == ";")
                    rngKva.FormulaLocal = $"={celdaDiv}/{celdaUsr}";
                else
                    rngKva.Formula = $"={celdaDiv}/{celdaUsr}";

                rngKva.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                rngKva.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
                rngKva.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                rngKva.Borders.Weight = Excel.XlBorderWeight.xlThin;

                // CARGA (kVA) (merge)
                var rngCarga = ws2.Range[ws2.Cells[aptoStartRow, colInicio + 4], ws2.Cells[filaUltima, colInicio + 4]];
                rngCarga.ClearContents(); // 🔑 Limpia antes del merge
                rngCarga.Merge();

                if (sep == ";")
                    rngCarga.FormulaLocal = $"={ColumnNumberToLetter(colInicio + 2)}{aptoStartRow}*{ColumnNumberToLetter(colInicio + 3)}{aptoStartRow}";
                else
                    rngCarga.Formula = $"={ColumnNumberToLetter(colInicio + 2)}{aptoStartRow}*{ColumnNumberToLetter(colInicio + 3)}{aptoStartRow}";

                rngCarga.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                rngCarga.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
                rngCarga.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                rngCarga.Borders.Weight = Excel.XlBorderWeight.xlThin;
            }


            /* -- 3. TOTAL y CARGA DIVERSIFICADA ---------------------------------- */
            ws2.Cells[row, colInicio + 0] = "TOTAL CARGA (kVA)";
            ws2.Cells[row, colInicio + 4].Formula = $"=SUM({ColumnNumberToLetter(colInicio + 4)}{firstData}:{ColumnNumberToLetter(colInicio + 4)}{row - 1})";
            int filaTotal = row;

            row++;
            ws2.Cells[row, colInicio + 0] = "CARGA DIVERSIFICADA (kVA)";
            ws2.Cells[row, colInicio + 1] = pctResidencial;
            ws2.Cells[row, colInicio + 1].NumberFormat = "0%";
            ws2.Cells[row, colInicio + 4].Formula = $"={ColumnNumberToLetter(colInicio + 4)}{filaTotal}*{ColumnNumberToLetter(colInicio + 1)}{row}";
            int filaCargaDivRes = row;

            row++; // Fila vacía explícita
            row++; // Iniciar resumen

            // Carga estimada
            ws2.Cells[row, colInicio + 2].Value2 = "Carga estimada Residencias y Viviendas (kVA)";
            ws2.Cells[row, colInicio + 3].Formula = $"={ColumnNumberToLetter(colInicio + 4)}{filaTotal}";

            row++;
            ws2.Cells[row, colInicio + 2].Value2 = "Carga diversificada Residencias y Viviendas (kVA)";
            ws2.Cells[row, colInicio + 3].Formula = $"={ColumnNumberToLetter(colInicio + 4)}{filaCargaDivRes}";

            row++;
            ws2.Cells[row, colInicio + 2].Value2 = "Transformador Seleccionado (kVA)";
            if (transformadorKVA > 0)
                ws2.Cells[row, colInicio + 3].Value2 = transformadorKVA;


            /* -- Bordes tabla principal --------------------------------------------- */
            var tablaRango = ws2.Range[
                $"{ColumnNumberToLetter(colInicio)}{headerRow}",
                $"{ColumnNumberToLetter(colInicio + 4)}{filaCargaDivRes}"
            ];
            tablaRango.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            tablaRango.Borders.Weight = Excel.XlBorderWeight.xlThin;

            /* -- Estética resumen --------------------------------------------------- */
            string colTexto = ColumnNumberToLetter(colInicio + 2); // F
            string colValor = ColumnNumberToLetter(colInicio + 3); // G

            ws2.Range[$"{colTexto}{filaTotal + 3}:{colValor}{row}"].HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;

            // Fondo amarillo solo en la columna de texto
            for (int i = 0; i < 3; i++)
            {
                int f = filaTotal + 3 + i;
                ws2.Range[$"{colTexto}{f}"].Interior.Color = System.Drawing.ColorTranslator.ToOle(
                    System.Drawing.Color.FromArgb(0xFF, 0xC0, 0x00)); // Amarillo
                ws2.Range[$"{colValor}{f}"].Interior.ColorIndex = Excel.XlColorIndex.xlColorIndexNone;
            }

            // Bordes al resumen
            var resumenRngTexto = ws2.Range[$"{colTexto}{filaTotal + 3}:{colTexto}{row}"];
            var resumenRngValor = ws2.Range[$"{colValor}{filaTotal + 3}:{colValor}{row}"];

            resumenRngTexto.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            resumenRngTexto.Borders.Weight = Excel.XlBorderWeight.xlThin;

            resumenRngValor.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            resumenRngValor.Borders.Weight = Excel.XlBorderWeight.xlThin;

            ws1.Columns.AutoFit();
            ws2.Columns.AutoFit();

            /* 6️⃣  Guardar y cerrar ---------------------------------------------- */
            wb.SaveAs(filePath);
            wb.Close(); xl.Quit();

            System.Runtime.InteropServices.Marshal.ReleaseComObject(ws1);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(ws2);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);

            TaskDialog.Show("Exportación", $"Archivo guardado:\n{filePath}");
        }



        // Cambia la firma de la función para que devuelva la fila de "Carga diversificada Zonas Comunes (kVA)"
        private int ExportarAHojaComun(Excel.Worksheet ws,
                                       List<PredimResult> res,
                                       double pctNorm,
                                       double acLoadKVA,
                                       bool tieneVitri)
        {
            /* 1️⃣  ENCABEZADO (logo, título y rótulo) ----------------------- */
            InsertarEncabezadoExcel(ws);

            /* 1-bis. RÓTULO “ZONAS COMUNES” ───────────────────────────────── */
            // ——— construye la misma lista de cabeceras que usarás luego ———
            var headers = new List<string>{
        "Nombre del Área","Uso","Área (m²)","NTC2050",
        "ILUMINACIÓN","TOMAS"
    };
            if (acLoadKVA > 0) headers.Add("AIRE ACONDICIONADO");
            if (tieneVitri) headers.Add("VITRINISMO");
            headers.Add("CARGAS ESPECIALES");
            headers.Add("CARGA2 (kVA)");
            headers.Add("ÍNDICE POR ÁREA");

            int tituloRow = HEADER_ROWS + 1;          // justo debajo del cuadro de datos
            ws.Rows[tituloRow].Insert(Excel.XlInsertShiftDirection.xlShiftDown);

            string ultimaCol = ColumnNumberToLetter(headers.Count);
            var rngTitulo = ws.Range[$"A{tituloRow}:{ultimaCol}{tituloRow}"];
            rngTitulo.Merge();
            rngTitulo.Value2 = "ZONAS COMUNES";
            rngTitulo.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
            rngTitulo.VerticalAlignment = Excel.XlVAlign.xlVAlignCenter;
            rngTitulo.Font.Bold = true;
            rngTitulo.Font.Size = 12;
            // — Color de fondo (#FFC000) y borde grueso —
            rngTitulo.Interior.Color = System.Drawing.ColorTranslator.ToOle(
                                            System.Drawing.Color.FromArgb(0xFF, 0xC0, 0x00));

            rngTitulo.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            rngTitulo.Borders.Weight = Excel.XlBorderWeight.xlMedium;

            /* 1-ter. Re-calcula filas base ---------------------------------- */
            int headerRow = tituloRow + 1;   // fila donde irán las cabeceras
            int firstDataRow = headerRow + 1;   // primera fila de datos
            int row = firstDataRow;    // puntero que usarás en el foreach

            /* 2️⃣  FORMATO GENERAL Y CABECERAS … */
            ws.Cells.NumberFormat = "0.00";          // formato genérico para números
            ws.Range["H6:K6"].NumberFormat = "d/m/yyyy";   // <-- restaura el formato de la fecha
            ws.Columns[2].NumberFormat = "0%";       // porcentaje para la 2.ª columna


            for (int c = 0; c < headers.Count; c++)
                ws.Cells[headerRow, c + 1] = headers[c];
            // — Negrita para toda la fila de cabeceras —
            var rngHeaders = ws.Range[$"A{headerRow}:{ultimaCol}{headerRow}"];
            rngHeaders.Font.Bold = true;

            /* ─── Índices de columna para usar en fórmulas ----------------- */
            int cArea = headers.IndexOf("Área (m²)") + 1;
            int cVA = headers.IndexOf("NTC2050") + 1;
            int cIlum = headers.IndexOf("ILUMINACIÓN") + 1;
            int cTomas = headers.IndexOf("TOMAS") + 1;
            int cAC = acLoadKVA > 0 ? headers.IndexOf("AIRE ACONDICIONADO") + 1 : -1;
            int cVit = tieneVitri ? headers.IndexOf("VITRINISMO") + 1 : -1;
            int cEsp = headers.IndexOf("CARGAS ESPECIALES") + 1;
            int cC2 = headers.IndexOf("CARGA2 (kVA)") + 1;
            int cIdx = headers.IndexOf("ÍNDICE POR ÁREA") + 1;

            /* ─── Volcado de datos por espacio ----------------------------- */
            foreach (var r in res)
            {
                ws.Cells[row, 1] = r.AreaName;
                ws.Cells[row, 2] = r.Uso;
                string cellA = ColumnNumberToLetter(cArea) + row;

                if (r.Uso == "Vitrinismo")
                {
                    ws.Cells[row, cArea].Value2 = r.AreaM2;

                    if (cVit != -1)
                    {
                        string kvaPerM = KVA_PER_M.ToString(CultureInfo.InvariantCulture);
                        ws.Cells[row, cVit].Formula = $"={cellA}*{kvaPerM}";
                    }
                }

                else
                {
                    ws.Cells[row, cArea].Value2 = r.AreaM2;
                    ws.Cells[row, cVA].Value2 = r.VaPorM2;
                    string cellV = ColumnNumberToLetter(cVA) + row;
                    ws.Cells[row, cIlum].Formula = $"={cellA}*{cellV}/1000";
                    ws.Cells[row, cTomas].Formula = $"={cellA}*{cellV}/1000";
                }

                if (acLoadKVA > 0 && r.Uso != "Vitrinismo" && cAC != -1)
                    ws.Cells[row, cAC].Formula =
                        $"={cellA}*{acLoadKVA.ToString(CultureInfo.InvariantCulture)}/1000";

                if (r.EsManual && r.Uso != "Vitrinismo")
                    ws.Cells[row, cEsp].Value2 = r.CargaKVA;

                var parts = new List<string>{
            ColumnNumberToLetter(cIlum)  + row,
            ColumnNumberToLetter(cTomas) + row
        };
                if (acLoadKVA > 0 && cAC != -1) parts.Add(ColumnNumberToLetter(cAC) + row);
                if (r.Uso == "Vitrinismo" && cVit != -1) parts.Add(ColumnNumberToLetter(cVit) + row);
                parts.Add(ColumnNumberToLetter(cEsp) + row);

                ws.Cells[row, cC2].Formula = "=" + string.Join("+", parts);

                if (r.Uso != "Vitrinismo")
                    ws.Cells[row, cIdx].Formula =
                        $"=IF({cellA}=0,\"\",{ColumnNumberToLetter(cC2)}{row}/{cellA})";

                row++;                 // avanza a la siguiente fila de datos
            }

            /* ─── Total y carga diversificada ------------------------------ */
            int totalRow = row;
            ws.Cells[totalRow, 1] = "TOTAL CARGA";
            for (int c = cIlum; c <= cC2; c++)
            {
                string L = ColumnNumberToLetter(c);
                ws.Cells[totalRow, c].Formula =
                    $"=SUM({L}{firstDataRow}:{L}{totalRow - 1})";
            }

            int divRow = totalRow + 1;
            ws.Cells[divRow, 1] = "CARGA DIVERSIFICADA";
            ws.Cells[divRow, 2] = pctNorm;
            for (int c = cIlum; c <= cC2; c++)
            {
                string L = ColumnNumberToLetter(c);
                ws.Cells[divRow, c].Formula = $"={L}{totalRow}*$B{divRow}";
            }

            /* ─── Bloque de resumen final ---------------------------------- */
            int rowZonasComunes = divRow + 3;
            string colCarga2 = ColumnNumberToLetter(cC2);

            ws.Cells[rowZonasComunes, 5].Value2 = "Carga estimada Zonas Comunes (kVA)";
            ws.Cells[rowZonasComunes, 6].Formula = $"={colCarga2}{totalRow}";

            ws.Cells[rowZonasComunes + 1, 5].Value2 = "Carga diversificada Zonas Comunes (kVA)";
            ws.Cells[rowZonasComunes + 1, 6].Formula = $"={colCarga2}{divRow}";

            /* —— fila que necesita la hoja Residencial —— */
            int filaCargaDivComun = rowZonasComunes + 1;

            int rowTransformador = rowZonasComunes + 2;
            string celdaDiv = $"F{rowZonasComunes + 1}";
            ws.Cells[rowTransformador, 5].Value2 = "Transformador Seleccionado (kVA)";

            string listSep = ws.Application.International[Excel.XlApplicationInternational.xlListSeparator].ToString();
            bool esEspañol = listSep == ";";

            string formulaEspTr =
                $"=SI({celdaDiv}<31;30;SI({celdaDiv}<50;45;SI({celdaDiv}<61;75;SI({celdaDiv}<116;112,5;" +
                $"SI({celdaDiv}<155;150;SI({celdaDiv}<231;225;SI({celdaDiv}<316;300;SI({celdaDiv}<416;400;" +
                $"SI({celdaDiv}<521;500;SI({celdaDiv}<655;630;SI({celdaDiv}<811;800;SI({celdaDiv}<1020;1000;1250))))))))))))";

            string formulaEngTr =
                $"=IF({celdaDiv}<31,30,IF({celdaDiv}<50,45,IF({celdaDiv}<61,75,IF({celdaDiv}<116,112.5," +
                $"IF({celdaDiv}<155,150,IF({celdaDiv}<231,225,IF({celdaDiv}<316,300,IF({celdaDiv}<416,400," +
                $"IF({celdaDiv}<521,500,IF({celdaDiv}<655,630,IF({celdaDiv}<811,800,IF({celdaDiv}<1020,1000,1250))))))))))))";

            if (esEspañol)
                ws.Cells[rowTransformador, 6].FormulaLocal = formulaEspTr;
            else
                ws.Cells[rowTransformador, 6].Formula = formulaEngTr;

            int rowSuplencia = rowTransformador + 1;
            ws.Cells[rowSuplencia, 5].Value2 = "Carga estimada Servicios Comunes suplencia (kVA)";
            ws.Cells[rowSuplencia, 6].Formula = $"={colCarga2}{divRow}";

            int rowPlanta = rowSuplencia + 1;
            string celdaSupl = $"F{rowSuplencia}";
            ws.Cells[rowPlanta, 5].Value2 = "Planta eléctrica suplencia (kVA)";

            string formulaEspPl =
                $"=SI({celdaSupl}<31;30;SI(Y({celdaSupl}>30;{celdaSupl}<50);45;SI(Y({celdaSupl}>49;{celdaSupl}<61);75;" +
                $"SI(Y({celdaSupl}>60;{celdaSupl}<116);112,5;SI(Y({celdaSupl}>115;{celdaSupl}<155);150;" +
                $"SI(Y({celdaSupl}>154;{celdaSupl}<231);225;SI(Y({celdaSupl}>230;{celdaSupl}<316);300;" +
                $"SI(Y({celdaSupl}>315;{celdaSupl}<416);400;SI(Y({celdaSupl}>415;{celdaSupl}<521);500;" +
                $"SI(Y({celdaSupl}>520;{celdaSupl}<640);630;SI(Y({celdaSupl}>639;{celdaSupl}<811);800;" +
                $"SI(Y({celdaSupl}>810;{celdaSupl}<1020);1000;1250))))))))))))";

            string formulaEngPl =
                $"=IF({celdaSupl}<31,30,IF(AND({celdaSupl}>30,{celdaSupl}<50),45,IF(AND({celdaSupl}>49,{celdaSupl}<61),75," +
                $"IF(AND({celdaSupl}>60,{celdaSupl}<116),112.5,IF(AND({celdaSupl}>115,{celdaSupl}<155),150," +
                $"IF(AND({celdaSupl}>154,{celdaSupl}<231),225,IF(AND({celdaSupl}>230,{celdaSupl}<316),300," +
                $"IF(AND({celdaSupl}>315,{celdaSupl}<416),400,IF(AND({celdaSupl}>415,{celdaSupl}<521),500," +
                $"IF(AND({celdaSupl}>520,{celdaSupl}<640),630,IF(AND({celdaSupl}>639,{celdaSupl}<811),800," +
                $"IF(AND({celdaSupl}>810,{celdaSupl}<1020),1000,1250))))))))))))";

            if (esEspañol)
                ws.Cells[rowPlanta, 6].FormulaLocal = formulaEspPl;
            else
                ws.Cells[rowPlanta, 6].Formula = formulaEngPl;

            // Estética
            ws.Range[$"E{rowZonasComunes}:F{rowPlanta}"].HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;

            for (int i = 0; i < 5; i++)
            {
                int fila = rowZonasComunes + i;
                ws.Range[$"E{fila}"].Interior.Color = System.Drawing.ColorTranslator.ToOle(
                    System.Drawing.Color.FromArgb(0xFF, 0xC0, 0x00)); // amarillo
                ws.Range[$"F{fila}"].Interior.ColorIndex = Excel.XlColorIndex.xlColorIndexNone;
            }

            var rngResumen = ws.Range[$"E{rowZonasComunes}:F{rowPlanta}"];
            rngResumen.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            rngResumen.Borders.Weight = Excel.XlBorderWeight.xlThin;

            // Ajustar ancho automático
            ws.Columns.AutoFit();

            // ░░ Aplicar bordes a toda la tabla de datos ░░
            int lastCol = headers.Count;
            string colFinal = ColumnNumberToLetter(lastCol);
            var rngTablaCompleta = ws.Range[$"A{firstDataRow}:{colFinal}{divRow}"];

            rngTablaCompleta.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
            rngTablaCompleta.Borders.Weight = Excel.XlBorderWeight.xlThin;


            // Devuelve la fila exacta para usarla después
            return filaCargaDivComun;
        }




        private Dictionary<string, double> CargarDiccionarioDesdeArchivo(string path)
        {
            var dic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            string ext = System.IO.Path.GetExtension(path).ToLower();
            try
            {
                if (ext == ".csv")
                {
                    var lines = System.IO.File.ReadAllLines(path);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var p = lines[i].Split(',');
                        if (p.Length < 2) continue;
                        if (double.TryParse(p[1].Trim().Replace(",", "."), NumberStyles.Any,
                                           CultureInfo.InvariantCulture, out double va))
                        {
                            dic[p[0].Trim()] = va;
                        }
                    }
                }
                else if (ext == ".xlsx")
                {
                    var xl = new Excel.Application();
                    var wb = xl.Workbooks.Open(path);
                    var ws = (Excel.Worksheet)wb.Sheets[1];
                    int rows = ws.UsedRange.Rows.Count;
                    for (int i = 2; i <= rows; i++)
                    {
                        string uso = (ws.Cells[i, 1] as Excel.Range).Text.ToString().Trim();
                        string sVA = (ws.Cells[i, 2] as Excel.Range).Text.ToString().Replace(",", ".");
                        if (double.TryParse(sVA, NumberStyles.Any,
                                           CultureInfo.InvariantCulture, out double va))
                        {
                            dic[uso] = va;
                        }
                    }
                    wb.Close(false); xl.Quit();
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(ws);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al leer archivo", ex.Message);
            }
            return dic;
        }

        private List<PredimResult> ObtenerEntradasManual()
        {
            var lista = new List<PredimResult>();

            while (true)
            {
                /* 1️⃣  Nombre del área especial */
                string nombre = Microsoft.VisualBasic.Interaction.InputBox(
                                    "Nombre del área especial (ej: HVAC)",
                                    "Área Especial");

                if (string.IsNullOrWhiteSpace(nombre))
                    break;                               // ← cancelar / salir

                /* 2️⃣  Carga total en kVA – SOLO NÚMEROS */
                double kva = 0;                          // valor a devolver
                while (true)
                {
                    string sKva = Microsoft.VisualBasic.Interaction.InputBox(
                                       $"Carga total para '{nombre}' (kVA – solo número)",
                                       "Carga (kVA)", "0");

                    /* ○ Usuario canceló esta ventana → volver a pedir nombre   */
                    if (string.IsNullOrWhiteSpace(sKva))
                        goto PreguntarOtroNombre;

                    /* ○ ¿Texto numérico y > 0?  */
                    if (double.TryParse(sKva.Replace(",", "."),
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out kva) && kva > 0)
                        break;                           // ✅ dato correcto

                    /* ○ Mensaje de error y repite la pregunta */
                    MessageBox.Show(
                        "Por favor digita un valor numérico positivo en kVA.",
                        "Valor inválido",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                /* 3️⃣  Agregar registro a la lista */
                lista.Add(new PredimResult
                {
                    AreaName = nombre,
                    Uso = nombre,
                    AreaM2 = 0,
                    VaPorM2 = 0,
                    CargaKVA = kva,
                    CargaVA = kva * 1000,
                    EsManual = true
                });

            PreguntarOtroNombre:
                /* 4️⃣  ¿Otra carga especial? */
                if (MessageBox.Show("¿Deseas agregar otra carga especial?",
                                    "Continuar",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question) != DialogResult.Yes)
                    break;
            }

            return lista;
        }


        private List<PredimResult> ObtenerSpaces(Document doc,
                                                 Dictionary<string, double> dic,
                                                 bool soloZonasComunes = false)
        {
            var lista = new List<PredimResult>();
            var coll = new FilteredElementCollector(doc)
                       .OfCategory(BuiltInCategory.OST_MEPSpaces)
                       .WhereElementIsNotElementType();

            foreach (Space sp in coll)
            {
                var pUso = sp.LookupParameter("Uso");
                if (pUso == null) continue;

                string usoStr = pUso.AsString() ?? "";
                double areaM2 = sp.Area * 0.092903;

                // Si solo queremos zonas comunes, saltamos apartamentos
                if (soloZonasComunes &&
                    (usoStr.Equals("Apto", StringComparison.OrdinalIgnoreCase) ||
                     usoStr.Equals("Apartamento", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                dic.TryGetValue(usoStr, out double vaM2);
                double cargaKVA = areaM2 * vaM2 / 1000.0;

                lista.Add(new PredimResult
                {
                    AreaName = sp.Name,
                    Uso = usoStr,
                    AreaM2 = areaM2,
                    VaPorM2 = vaM2,
                    CargaKVA = cargaKVA,
                    CargaVA = cargaKVA * 1000,
                    EsManual = false
                });
            }

            return lista;
        }


        private void MostrarResumen(List<PredimResult> res)
        {
            var sb = new System.Text.StringBuilder("Resultados de Predimensionamiento:\n\n");
            int i = 1;
            foreach (var r in res)
            {
                sb.AppendLine($"{i++}. {r.AreaName}");
                if (r.EsManual)
                    sb.AppendLine($"   Carga especial: {r.CargaKVA:F2} kVA\n");
                else
                    sb.AppendLine(
                      $"   Uso   : {r.Uso}\n" +
                      $"   Área  : {r.AreaM2:F2} m²\n" +
                      $"   VA/m² : {r.VaPorM2:F0}\n" +
                      $"   Carga : {r.CargaKVA:F2} kVA\n");
            }
            TaskDialog.Show("Resumen", sb.ToString());
        }

        // Sustituye TODO el cuerpo actual por esto:
        private void ExportarAExcel(List<PredimResult> res,
                                    double pctNorm,
                                    double acLoadKVA,
                                    bool tieneVitri)
        {
            /* 1️⃣  Diálogo de guardado */
            string filePath = null;
            using (var sfd = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                Title = "Guardar Excel"
            })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                    filePath = sfd.FileName;
            }
            if (string.IsNullOrEmpty(filePath)) return;

            /* 2️⃣  Crea libro y hoja */
            var xl = new Excel.Application { Visible = false };
            var wb = xl.Workbooks.Add(Type.Missing);
            var ws = (Excel.Worksheet)wb.Sheets[1];
            ws.Name = "CargasEspacios";

            /* 3️⃣  ¡Un solo llamado hace TODO el formato! */
            ExportarAHojaComun(ws,           // worksheet destino
                               res,          // lista de resultados
                               pctNorm,      // porcentaje de diversificación
                               acLoadKVA,    // aire acondicionado
                               tieneVitri);  // vitrinismo sí/no

            /* 4️⃣  Ajustes finales y guardar */
            ws.Columns.AutoFit();
            wb.SaveAs(filePath);

            wb.Close(); xl.Quit();
            System.Runtime.InteropServices.Marshal.ReleaseComObject(ws);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);

            TaskDialog.Show("Exportación", $"Archivo guardado:\n{filePath}");
        }


        private static string ColumnNumberToLetter(int col)
        {
            string s = "";
            while (col > 0)
            {
                int m = (col - 1) % 26;
                s = (char)(65 + m) + s;
                col = (col - m) / 26;
            }
            return s;
        }

        // ---------- SELECCIONA TRANSFORMADOR DESDE HOJA CODENSA ----------
        private double SeleccionarTransformador(
                string excelPath,    // ruta del Excel que el usuario eligió
                int estrato,         // estrato (1-6) digitado
                double kvaPorUsuario,// kVA/usuario calculado
                int usuariosTotales) // nº de apartamentos detectados
        {
            var xl = new Excel.Application();
            var wb = xl.Workbooks.Open(excelPath);
            Excel.Worksheet ws = null;
            double result = 0;

            try
            {
                /* 1️⃣  Intentar abrir la hoja "CODENSA" ------------------------------ */
                try
                {
                    ws = (Excel.Worksheet)wb.Sheets["CODENSA"];
                }
                catch
                {
                    TaskDialog.Show("Archivo inválido",
                        "No se encontró la hoja \"CODENSA\" en el Excel seleccionado.\n" +
                        "Agrega la tabla de transformadores en una hoja con ese nombre o verifica la ortografía.");
                    return 0;   // aborta antes de que Interop lance la excepción
                }

                /* 2️⃣  Lógica original (sin cambios) --------------------------------- */
                int rowStart = -1;
                for (int r = 1; r <= 500 && rowStart == -1; r++)
                {
                    string txt = (ws.Cells[r, 1] as Excel.Range).Text.ToString().Trim();
                    if (txt.Equals($"ESTRATO {estrato}", StringComparison.OrdinalIgnoreCase))
                        rowStart = r;
                }
                if (rowStart == -1) return 0;

                int headerRow = rowStart + 1;
                int dataStart = headerRow + 1;

                // Columna cuyo encabezado se acerque más al kVA/usuario
                int bestCol = -1; double minDiff = double.MaxValue;
                for (int c = 3; c <= 60; c++)
                {
                    string h = (ws.Cells[headerRow, c] as Excel.Range).Text.ToString().Replace(",", ".");
                    if (double.TryParse(h, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        double diff = Math.Abs(val - kvaPorUsuario);
                        if (diff < minDiff) { minDiff = diff; bestCol = c; }
                    }
                }
                if (bestCol == -1) return 0;

                // Fila cuyo nº de clientes se acerque al total de usuarios
                int bestRow = -1; minDiff = double.MaxValue;
                for (int r = dataStart; r <= dataStart + 200; r++)
                {
                    string n = (ws.Cells[r, bestCol] as Excel.Range).Text.ToString();
                    if (int.TryParse(n, out int clientes))
                    {
                        double diff = Math.Abs(clientes - usuariosTotales);
                        if (diff < minDiff) { minDiff = diff; bestRow = r; }
                    }
                }
                if (bestRow == -1) return 0;

                // kVA del transformador (columna 1)
                string kvatxt = (ws.Cells[bestRow, 1] as Excel.Range).Text.ToString().Replace(",", ".");
                double.TryParse(kvatxt, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }
            finally
            {
                wb.Close(false); xl.Quit();
                if (ws != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(wb);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(xl);
            }
            return result;  // 0 si algo no coincidió o faltaba la hoja
        }

        /// Devuelve el perímetro para un tramo “especial” (circular, ovalado, etc.)
        /// Si el usuario cancela ⟶ null
        private double? PedirPerimetroEspecial()
        {
            while (true)
            {
                string sPer = Microsoft.VisualBasic.Interaction.InputBox(
                    "Perímetro total de la vitrina especial (m):",
                    "Perímetro especial", "0");

                // Cancelado
                if (sPer == "") return null;

                if (double.TryParse(sPer.Replace(",", "."), NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out double per) &&
                    per > 0)
                    return per;

                MessageBox.Show("Debes digitar un número válido (> 0).",
                                "Valor inválido",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }

        private double PedirPorcentajeDiversificado(double sugerencia)
        {
            while (true)
            {
                string s = Microsoft.VisualBasic.Interaction.InputBox(
                               "Porcentaje de carga diversificada (1-100):",
                               "Carga diversificada",
                               sugerencia.ToString(CultureInfo.InvariantCulture));

                // Cancelar / cerrar ventana
                if (string.IsNullOrWhiteSpace(s)) return -1;

                // ¿Número?
                if (double.TryParse(s.Replace(",", "."), NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out double pct)
                    && pct >= 1 && pct <= 100)
                    return pct / 100.0;            // ← normalizado 0-1

                MessageBox.Show("Valor invalido porfavor colocar valores correctos numero entero entre 1 al 100",
                                "Valor inválido",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }
        public class PredimResult
        {
            public string AreaName { get; set; }
            public string Uso { get; set; }
            public double AreaM2 { get; set; }
            public double VaPorM2 { get; set; }
            public double CargaVA { get; set; }
            public double CargaKVA { get; set; }
            public bool EsManual { get; set; }
        }
    }
}
