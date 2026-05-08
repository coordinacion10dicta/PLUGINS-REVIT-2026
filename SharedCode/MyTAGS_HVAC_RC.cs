#if REVIT2018 || REVIT2019 || REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
#define REVIT
#endif

#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
#define SUPPORTS_ANNOTATION_CROP
#define SUPPORTS_LEADER_ELBOW
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Reflection;
using System.Globalization;

// Revit
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

// Alias WinForms / Drawing / Excel
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Excel = Microsoft.Office.Interop.Excel;
using MiNamespace;
using System.Globalization;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_HVAC_RC : IExternalCommand
    {
        // Último archivo de Excel generado/actualizado en esta sesión de Revit
        private static string _lastExcelPath = null;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    message = "No hay un documento activo de Revit.";
                    return Result.Failed;
                }

                // ---------------- MENÚ INICIAL ----------------
                using (HVACMainMenuForm menu = new HVACMainMenuForm())
                {
                    var drMenu = menu.ShowDialog();
                    if (drMenu != WinForms.DialogResult.OK)
                        return Result.Cancelled;

                    if (menu.OpcionSeleccionada == "LIMPIAR")
                    {
                        LimpiarColoresYTextosVista(doc, uidoc.ActiveView);
                        TaskDialog.Show("MyTAGS_HVAC_RC",
                            "Se limpiaron los colores y textos de rutas en la vista actual.");
                        return Result.Succeeded;
                    }
                }

                // ---------------- TIPO DE SISTEMA ----------------
                string tipoSistema;
                string nombreHoja;
                int numeroSistema = 1;
                string sufijoSistema = "01";

                using (SeleccionarSistemaForm selForm = new SeleccionarSistemaForm())
                {
                    var drSel = selForm.ShowDialog();
                    if (drSel != WinForms.DialogResult.OK)
                        return Result.Cancelled;

                    tipoSistema = selForm.TipoSistemaSeleccionado;
                }

                // Preguntar número de sistema (1, 2, 5, etc.)
                using (SeleccionarNumeroSistemaForm numForm = new SeleccionarNumeroSistemaForm(tipoSistema))
                {
                    var drNum = numForm.ShowDialog();
                    if (drNum != WinForms.DialogResult.OK)
                        return Result.Cancelled;

                    numeroSistema = numForm.NumeroSistema;
                    sufijoSistema = numeroSistema.ToString("00"); // 1 -> "01", 5 -> "05"
                }

                switch (tipoSistema)
                {
                    case "SUMINISTRO":
                        nombreHoja = "VS-RC";
                        break;
                    case "RE_VENTILACION":
                        nombreHoja = "VE-RC";
                        break;
                    case "EXTRACTOR":
                        nombreHoja = "EI-RC";
                        break;
                    default:
                        throw new Exception("No se pudo determinar el tipo de sistema seleccionado.");
                }

                bool usarRutas = tipoSistema != "EXTRACTOR";

                List<RouteInfo> rutas = null;
                List<string> snapshotPaths = null;

                if (usarRutas)
                {
                    // Seleccionar rutas A→B
                    rutas = SeleccionarRutasPorPuntos(uidoc);

                    if (rutas.Count == 0)
                    {
                        TaskDialog.Show("Rutas HVAC",
                            "No se seleccionó ninguna ruta. No se generará Excel.");
                        return Result.Cancelled;
                    }

                    // Colorear rutas y crear texto "Ruta X"
                    using (RouteColorForm colorForm = new RouteColorForm(doc, uidoc.ActiveView, rutas))
                    {
                        colorForm.ShowDialog();
                    }

                    // Generar pantallazo por cada ruta (vista 3D)
                    snapshotPaths = GenerarPantallazosRutas3D(doc, rutas);
                }

                // ---------------- MODO EXCEL: NUEVO VS EXISTENTE ----------------
                bool usarExcelExistente = false;
                string plantillaPath = null;
                string destinoPath = null;

                using (ExcelModeForm excelForm = new ExcelModeForm())
                {
                    var drExcel = excelForm.ShowDialog();
                    if (drExcel != WinForms.DialogResult.OK)
                        return Result.Cancelled;

                    usarExcelExistente = excelForm.UsarArchivoExistente;
                }

                if (usarExcelExistente)
                {
                    // Detectar/seleccionar archivo existente
                    string existente = ObtenerExcelExistente();
                    if (string.IsNullOrEmpty(existente))
                        return Result.Cancelled;

                    plantillaPath = existente; // abrimos directamente ese archivo
                    destinoPath = existente;   // y lo guardamos sobre sí mismo
                }
                else
                {
                    // Archivo nuevo desde la plantilla base
                    plantillaPath = ObtenerPlantillaExcel();

                    destinoPath = PedirRutaGuardarExcelDestino();
                    if (string.IsNullOrEmpty(destinoPath))
                        return Result.Cancelled;
                }

                // ---------------- DATOS POR RUTA ----------------
                List<RouteExcelData> datosRutas = new List<RouteExcelData>();

                if (usarRutas)
                {
                    foreach (var r in rutas)
                    {
                        int codos90MasT = r.Codos90 + r.Tees;
                        int codos45 = r.Codos45;
                        string caudalAuto = r.CaudalCfmTexto ?? string.Empty;
                        string nombreRuta = $"Ruta {r.Indice}";

                        // C40 = sumatoria de tramos rectos desde Revit (Length) en pies
                        double totalFt = CalcularLongitudTramosRectos_FT(doc, r.ElementIds);
                        string totalTramosRectos = FormatearLongitudParaExcel_Pies(totalFt);

                        using (DatosVS01Form form = new DatosVS01Form(nombreRuta, codos90MasT, codos45, true, caudalAuto))
                        {
                            WinForms.DialogResult dr = form.ShowDialog();
                            if (dr != WinForms.DialogResult.OK)
                                return Result.Cancelled;

                            RouteExcelData data = new RouteExcelData
                            {
                                NombreRuta = nombreRuta,
                                AreaAtendida = form.ValorAreaAtendida,
                                CaudalCfm = form.ValorCaudalCfm,
                                ValorC36 = form.ValorC36,
                                ValorC37 = form.ValorC37,
                                TramosRectos = totalTramosRectos,
                                Codos90MasT = form.ValorCodos90,
                                Codos45 = form.ValorCodos45,
                                Descripciones = form.Descripciones,
                                Cantidades = form.Cantidades,
                                CaidasPresion = form.CaidasPresion
                            };
                            datosRutas.Add(data);
                        }
                    }
                }
                else
                {
                    // Extractor individual: pedimos equipo para leer caudal
                    string caudalAutoExtractor = string.Empty;
                    Element equipoExtractor = null;

                    try
                    {
                        Reference refEq = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new MechanicalEquipmentSelectionFilter(),
                            "Seleccione el EQUIPO mecánico del extractor individual");

                        equipoExtractor = doc.GetElement(refEq);
                        caudalAutoExtractor = ObtenerCaudalDesdeEquipo(equipoExtractor);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }

                    string nombreRutaExtractor = "Extractor individual";

                    using (DatosVS01Form form = new DatosVS01Form(nombreRutaExtractor, 0, 0, false, caudalAutoExtractor))
                    {
                        WinForms.DialogResult dr = form.ShowDialog();
                        if (dr != WinForms.DialogResult.OK)
                            return Result.Cancelled;

                        RouteExcelData data = new RouteExcelData
                        {
                            NombreRuta = nombreRutaExtractor,
                            AreaAtendida = form.ValorAreaAtendida,
                            CaudalCfm = form.ValorCaudalCfm,
                            TramosRectos = null,
                            Codos90MasT = form.ValorCodos90,
                            Codos45 = form.ValorCodos45,
                            Descripciones = form.Descripciones,
                            Cantidades = form.Cantidades,
                            CaidasPresion = form.CaidasPresion
                        };
                        datosRutas.Add(data);
                    }

                    // Para extractor individual generamos un 3D similar al de las rutas
                    if (equipoExtractor != null)
                        snapshotPaths = GenerarPantallazoExtractor3D(doc, equipoExtractor);
                    else
                        snapshotPaths = GenerarPantallazoVistaSimple(doc, uidoc.ActiveView); // fallback por si acaso
                }


                // ---------------- EXCEL DESTINO ----------------
                // ESCRIBIR TODAS LAS RUTAS + IMÁGENES
                // nombreHojaBase = VS-RC / VE-RC / EI-RC según tipoSistema
                string nombreHojaBase = nombreHoja;  // ya está calculado

                // sufijoSistema ya viene de la selección de número de sistema (01, 02, 05, ...)

                // Hoja de trabajo por sistema (multi-rutas)
                string nombreHojaSistema = nombreHojaBase + "-" + sufijoSistema;

                // Prefijo para la hoja resumen: VS / VE / EI
                string prefijoResumen;
                if (tipoSistema == "SUMINISTRO")
                    prefijoResumen = "VS";
                else if (tipoSistema == "RE_VENTILACION")
                    prefijoResumen = "VE";
                else
                    prefijoResumen = "EI";

                // Hoja resumen por sistema (ruta ganadora)
                string nombreHojaResumen = prefijoResumen + "-" + sufijoSistema;

                // ¿Trabajamos sobre archivo existente o uno nuevo desde plantilla?
                bool actualizarExcelExistente = usarExcelExistente;

                RellenarHojaSistemaYGuardarCopia_Multiruta(
                    plantillaPath,
                    destinoPath,
                    nombreHojaBase,
                    nombreHojaSistema,
                    nombreHojaResumen,
                    datosRutas,
                    snapshotPaths,
                    actualizarExcelExistente);

                // Guardamos la ruta para futuros complementos
                _lastExcelPath = destinoPath;

                TaskDialog.Show(
                    "MyTAGS_HVAC_RC",
                    $"El archivo de Excel se ha actualizado correctamente ({datosRutas.Count} ruta(s)).\n\n" +
                    $"Ruta del archivo:\n{destinoPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MyTAGS_HVAC_RC",
                    "Se produjo un error durante la generación del Excel:\n\n" + ex.Message);
                message = ex.Message;
                return Result.Cancelled; // para que Revit no muestre 'Error - cannot be ignored'
            }
        }

        // ============================================================
        // LOCALIZAR PLANTILLA EN \Resources
        // ============================================================

        private string ObtenerPlantillaExcel()
        {
            string fileName = "RutaCritica.xlsx";

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string resourcesAlongDll = Path.Combine(asmDir, "Resources");

            string resourcesAtSolutionRoot = Path.GetFullPath(
                Path.Combine(asmDir, @"..\..\..\..\Resources"));

            string[] candidateDirs = new[]
            {
                resourcesAlongDll,
                resourcesAtSolutionRoot
            };

            foreach (string dir in candidateDirs)
            {
                string full = Path.Combine(dir, fileName);
                if (File.Exists(full))
                    return full;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("No se encontró la plantilla de Excel.");
            sb.AppendLine();
            sb.AppendLine("Rutas probadas:");
            foreach (string dir in candidateDirs)
            {
                sb.AppendLine(Path.Combine(dir, fileName));
            }
            sb.AppendLine();
            sb.AppendLine("Verifica que 'RutaCritica.xlsx' exista en alguna de las rutas anteriores y que tenga:");
            sb.AppendLine("- Build Action = Content");
            sb.AppendLine("- Copy to Output Directory = Copy always");

            throw new FileNotFoundException(sb.ToString());
        }

        private string ObtenerExcelExistente()
        {
            // Si ya tenemos un archivo anterior registrado, ofrecer usarlo
            if (!string.IsNullOrWhiteSpace(_lastExcelPath) && File.Exists(_lastExcelPath))
            {
                TaskDialogResult res = TaskDialog.Show(
                    "Archivo detectado",
                    $"Se detectó el último archivo generado:\n{_lastExcelPath}\n\n" +
                    "¿Desea usar este archivo para complementarlo?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (res == TaskDialogResult.Yes)
                    return _lastExcelPath;
            }

            // Si no hay anterior, o el usuario dijo que NO, pedir uno con OpenFileDialog
            using (WinForms.OpenFileDialog ofd = new WinForms.OpenFileDialog())
            {
                ofd.Title = "Seleccione el archivo de Excel existente a complementar";
                ofd.Filter = "Archivos de Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls";

                if (ofd.ShowDialog() == WinForms.DialogResult.OK)
                    return ofd.FileName;
            }

            return null;
        }

        // ============================================================
        // LIMPIAR COLORES / TEXTOS EN LA VISTA
        // ============================================================

        private void LimpiarColoresYTextosVista(Document doc, View view)
        {
            if (doc == null || view == null) return;

            using (Transaction t = new Transaction(doc, "Limpiar colores de rutas HVAC"))
            {
                t.Start();

                var categorias = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_FlexDuctCurves,
                    BuiltInCategory.OST_DuctFitting,
                    BuiltInCategory.OST_DuctTerminal,
                    BuiltInCategory.OST_MechanicalEquipment
                };

                var catIds = categorias
                    .Select(c => new ElementId((int)c))
                    .ToList();

                ElementMulticategoryFilter catFilter =
                    new ElementMulticategoryFilter(catIds);

                var elems = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(catFilter)
                    .WhereElementIsNotElementType()
                    .ToElements();

                OverrideGraphicSettings cleanOgs = new OverrideGraphicSettings();

                foreach (var e in elems)
                {
                    view.SetElementOverrides(e.Id, cleanOgs);
                }

                var textNotes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();

                foreach (var tn in textNotes)
                {
                    string txt = tn.Text ?? "";
                    if (txt.StartsWith("Ruta ", StringComparison.OrdinalIgnoreCase))
                    {
                        doc.Delete(tn.Id);
                    }
                }

                t.Commit();
            }
        }

        // ============================================================
        // SELECCIÓN DE RUTAS A→B
        // ============================================================

        private List<RouteInfo> SeleccionarRutasPorPuntos(UIDocument uidoc)
        {
            List<RouteInfo> rutas = new List<RouteInfo>();
            Document doc = uidoc.Document;
            int indice = 1;

            while (true)
            {
                try
                {
                    // 1) Preguntar si quiere definir otra ruta
                    TaskDialogResult resIntro = TaskDialog.Show(
                        "Nueva ruta",
                        $"¿Desea definir la ruta {indice}?",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                    if (resIntro == TaskDialogResult.No)
                        break;

                    // 2) Elegir tipo de ruta con un formulario (1 y 2)
                    bool rutaEquipoRejilla;
                    using (SeleccionarTipoRutaForm tipoForm = new SeleccionarTipoRutaForm())
                    {
                        var drTipo = tipoForm.ShowDialog();
                        if (drTipo != WinForms.DialogResult.OK)
                            break; // si cancela, salimos del bucle

                        rutaEquipoRejilla = tipoForm.EsRutaEquipoRejilla;
                    }

                    Element elemInicio = null;
                    Element elemFin = null;
                    Element equipo = null;

                    // 3A) Ruta EQUIPO → REJILLA
                    if (rutaEquipoRejilla)
                    {
                        Reference refEq = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new MechanicalEquipmentSelectionFilter(),
                            "Seleccione el EQUIPO mecánico (punto A)");

                        equipo = doc.GetElement(refEq);
                        if (equipo == null)
                            continue;

                        elemInicio = equipo;

                        Reference refGr = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new DuctTerminalSelectionFilter(),
                            "Seleccione la REJILLA / difusor (punto B)");

                        elemFin = doc.GetElement(refGr);
                        if (elemFin == null)
                            continue;
                    }
                    // 3B) Ruta REJILLA → REJILLA
                    else
                    {
                        Reference refGrA = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new DuctTerminalSelectionFilter(),
                            "Seleccione la REJILLA ORIGEN (punto A)");

                        elemInicio = doc.GetElement(refGrA);
                        if (elemInicio == null)
                            continue;

                        Reference refGrB = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new DuctTerminalSelectionFilter(),
                            "Seleccione la REJILLA DESTINO (punto B)");

                        elemFin = doc.GetElement(refGrB);
                        if (elemFin == null)
                            continue;
                    }

                    // 4) Buscar ruta entre los dos elementos
                    if (!TryFindRouteBetweenElements(doc, elemInicio, elemFin, out List<ElementId> rutaElementos))
                    {
                        TaskDialog.Show(
                            "Ruta no encontrada",
                            $"No se pudo encontrar una ruta conectada entre los elementos seleccionados para la ruta {indice}.");
                        continue;
                    }

                    // 5) Contar codos y tees
                    int c90, c45, cT;
                    ContarCodosYTeesEnRuta(doc, rutaElementos, out c90, out c45, out cT);

                    // 6) Intentar obtener caudal
                    string caudalTexto = string.Empty;
                    if (rutaEquipoRejilla && equipo != null)
                    {
                        caudalTexto = ObtenerCaudalDesdeEquipo(equipo);
                    }
                    else if (elemInicio != null)
                    {
                        // Para ruta rejilla→rejilla, intentamos leer caudal de la rejilla origen
                        caudalTexto = ObtenerCaudalDesdeEquipo(elemInicio);
                    }

                    // 7) Registrar la ruta
                    rutas.Add(new RouteInfo
                    {
                        Indice = indice,
                        Codos90 = c90,
                        Codos45 = c45,
                        Tees = cT,
                        ElementIds = rutaElementos,
                        EndElementId = elemFin.Id,
                        CaudalCfmTexto = caudalTexto
                    });

                    TaskDialogResult res = TaskDialog.Show(
                        "Ruta registrada",
                        $"Ruta {indice} registrada.\n" +
                        $"Codos 90° = {c90}\n" +
                        $"Codos 45° = {c45}\n" +
                        $"Tees = {cT}\n\n" +
                        "¿Desea agregar otra ruta?",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                    indice++;

                    if (res == TaskDialogResult.No)
                        break;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Si el usuario cancela alguna selección, salimos del bucle
                    break;
                }
            }

            return rutas;
        }

        private bool TryFindRouteBetweenElements(
            Document doc,
            Element start,
            Element end,
            out List<ElementId> rutaElementos)
        {
            rutaElementos = new List<ElementId>();

            if (start == null || end == null)
                return false;

            ElementId startId = start.Id;
            ElementId endId = end.Id;

            Queue<ElementId> queue = new Queue<ElementId>();
            HashSet<ElementId> visited = new HashSet<ElementId>();
            Dictionary<ElementId, ElementId> parent =
                new Dictionary<ElementId, ElementId>();

            queue.Enqueue(startId);
            visited.Add(startId);

            bool found = false;

            while (queue.Count > 0)
            {
                ElementId currentId = queue.Dequeue();
                if (currentId == endId)
                {
                    found = true;
                    break;
                }

                Element current = doc.GetElement(currentId);
                if (current == null) continue;

                foreach (Element neighbor in GetConnectedElements(current))
                {
                    ElementId nid = neighbor.Id;
                    if (visited.Contains(nid)) continue;

                    visited.Add(nid);
                    parent[nid] = currentId;
                    queue.Enqueue(nid);
                }
            }

            if (!found) return false;

            List<ElementId> reversed = new List<ElementId>();
            ElementId cur = endId;
            reversed.Add(cur);

            while (parent.ContainsKey(cur))
            {
                cur = parent[cur];
                reversed.Add(cur);
            }

            reversed.Reverse();
            rutaElementos = reversed;

            return true;
        }

        private IEnumerable<Element> GetConnectedElements(Element elem)
        {
            List<Element> result = new List<Element>();
            HashSet<ElementId> added = new HashSet<ElementId>();

            foreach (Connector c in GetConnectors(elem))
            {
                foreach (Connector refConn in c.AllRefs)
                {
                    Element owner = refConn.Owner;
                    if (owner == null) continue;

                    ElementId id = owner.Id;
                    if (id == elem.Id) continue;
                    if (added.Contains(id)) continue;

                    added.Add(id);
                    result.Add(owner);
                }
            }

            return result;
        }

        private IEnumerable<Connector> GetConnectors(Element e)
        {
            List<Connector> conns = new List<Connector>();

            if (e is FamilyInstance fi)
            {
                MEPModel mep = fi.MEPModel;
                if (mep != null)
                {
                    ConnectorManager cm = mep.ConnectorManager;
                    if (cm != null)
                    {
                        foreach (Connector c in cm.Connectors)
                            conns.Add(c);
                    }
                }
            }
            else if (e is MEPCurve mc)
            {
                ConnectorManager cm = mc.ConnectorManager;
                if (cm != null)
                {
                    foreach (Connector c in cm.Connectors)
                        conns.Add(c);
                }
            }

            return conns;
        }

        private void ContarCodosYTeesEnRuta(
            Document doc,
            ICollection<ElementId> idsRuta,
            out int count90,
            out int count45,
            out int countTee)
        {
            count90 = 0;
            count45 = 0;
            countTee = 0;

            if (idsRuta == null || idsRuta.Count == 0)
                return;

            const double tolGrados = 1.0;

            foreach (ElementId id in idsRuta)
            {
                Element e = doc.GetElement(id);
                if (e == null)
                    continue;

                if (e.Category == null ||
                    e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_DuctFitting)
                    continue;

                MechanicalFitting mf = null;
                if (e is FamilyInstance fi)
                {
                    mf = fi.MEPModel as MechanicalFitting;
                }

                // Tee normal
                if (mf != null)
                {
                    if (mf.PartType == PartType.Tee)
                    {
                        countTee++;
                        continue;
                    }

                    // Detecta Tap/Takeoff/Shoe por parámetro
                    if (EsZapatoOTakeoff(e))
                    {
                        countTee++;
                        continue;
                    }

                    if (mf.PartType != PartType.Elbow)
                        continue;
                }
                else
                {
                    if (EsZapatoOTakeoff(e))
                    {
                        countTee++;
                        continue;
                    }
                }

                Parameter p = e.LookupParameter("Angle");
                if (p == null || p.StorageType != StorageType.Double)
                    continue;

                double angleRad = p.AsDouble();
                double angleDeg = angleRad * 180.0 / Math.PI;
                double angleAbs = Math.Abs(angleDeg);

                if (Math.Abs(angleAbs - 90.0) <= tolGrados)
                {
                    count90++;
                }
                else if (Math.Abs(angleAbs - 45.0) <= tolGrados)
                {
                    count45++;
                }
            }
        }

        private bool EsZapatoOTakeoff(Element e)
        {
            if (e == null) return false;

            Parameter pPart = e.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            if (pPart != null && pPart.StorageType == StorageType.Integer)
            {
                int raw = pPart.AsInteger();

                string name = Enum.GetName(typeof(PartType), raw);

                if (!string.IsNullOrEmpty(name))
                {
                    string n = name.ToLowerInvariant();
                    if (n == "tap" || n == "takeoff" || n.Contains("shoe"))
                        return true;
                }
            }

            if (e is FamilyInstance fi)
            {
                string fam = fi.Symbol?.FamilyName ?? "";
                string typ = fi.Symbol?.Name ?? "";
                string key = (fam + " " + typ).ToLowerInvariant();

                if (key.Contains("zapato") || key.Contains("shoe") || key.Contains("tap") || key.Contains("takeoff"))
                    return true;
            }

            return false;
        }

        // ============================================================
        // C40 - SUMATORIA TRAMOS RECTOS (DUCTOS) POR RUTA "Length"
        // ============================================================

        private double CalcularLongitudTramosRectos_FT(Document doc, ICollection<ElementId> idsRuta)
        {
            double totalFt = 0.0;
            if (doc == null || idsRuta == null || idsRuta.Count == 0) return totalFt;

            foreach (var id in idsRuta)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                bool isDuctCurve = e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DuctCurves;
                bool isFlexDuct = e.Category != null && e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_FlexDuctCurves;

                if (!isDuctCurve && !isFlexDuct)
                    continue;

                Parameter pLen = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (pLen == null) pLen = e.LookupParameter("Length");

                if (pLen == null || pLen.StorageType != StorageType.Double)
                    continue;

                totalFt += pLen.AsDouble(); // unidades internas (feet)
            }

            return totalFt;
        }

        private string FormatearLongitudParaExcel_Pies(double lengthFt)
        {
            // Solo formateamos: coma decimal (es-ES) y sin unidad.
            return lengthFt.ToString("0.##", new CultureInfo("es-ES"));
        }

        // ============================================================
        // OBTENER CAUDAL DESDE EL EQUIPO (parámetro "DC - Flujo de aire")
        // ============================================================

        private string ObtenerCaudalDesdeEquipo(Element equipo)
        {
            if (equipo == null) return string.Empty;

            Parameter pFlow = equipo.LookupParameter("DC - Flujo de aire");
            if (pFlow == null) return string.Empty;

            try
            {
                if (pFlow.StorageType == StorageType.Double)
                {
                    string vs = pFlow.AsValueString();
                    if (!string.IsNullOrWhiteSpace(vs))
                        return vs;

                    double v = pFlow.AsDouble();
                    return v.ToString("0.##");
                }
                else
                {
                    return pFlow.AsString() ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        // ============================================================
        // GENERAR PANTALLAZOS POR RUTA (2D)
        // ============================================================

        private List<string> GenerarPantallazosRutas(Document doc, View view, List<RouteInfo> rutas)
        {
            List<string> result = new List<string>();
            if (doc == null || view == null) return result;
            if (rutas == null || rutas.Count == 0) return result;

            string tempDir = Path.Combine(Path.GetTempPath(), "MyTAGS_HVAC_RC_Snapshots");
            Directory.CreateDirectory(tempDir);

            ElementId solidFillId = ElementId.InvalidElementId;
            try
            {
                var fpCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement));

                foreach (FillPatternElement fpe in fpCollector)
                {
                    FillPattern fp = fpe.GetFillPattern();
                    if (fp.IsSolidFill)
                    {
                        solidFillId = fpe.Id;
                        break;
                    }
                }
            }
            catch { }

            foreach (var ruta in rutas)
            {
                if (ruta.ElementIds == null || ruta.ElementIds.Count == 0)
                    continue;

                var backup = new Dictionary<ElementId, OverrideGraphicSettings>();

                using (Transaction tColor = new Transaction(doc, $"Color temporal Ruta {ruta.Indice}"))
                {
                    tColor.Start();

                    foreach (ElementId id in ruta.ElementIds)
                    {
                        if (!backup.ContainsKey(id))
                            backup[id] = view.GetElementOverrides(id);

                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLineColor(ruta.RouteColor);
                        ogs.SetProjectionLineWeight(3);
#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                        ogs.SetCutLineColor(ruta.RouteColor);
                        ogs.SetCutLineWeight(3);
                        if (solidFillId != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceForegroundPatternId(solidFillId);
                            ogs.SetSurfaceForegroundPatternColor(ruta.RouteColor);
                            ogs.SetCutForegroundPatternId(solidFillId);
                            ogs.SetCutForegroundPatternColor(ruta.RouteColor);
                        }
#endif
                        view.SetElementOverrides(id, ogs);
                    }

                    tColor.Commit();
                }

                string baseName = $"Ruta_{ruta.Indice}";
                string basePathNoExt = Path.Combine(tempDir, baseName);

                ImageExportOptions opt = new ImageExportOptions
                {
                    ExportRange = ExportRange.CurrentView,
                    FilePath = basePathNoExt,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    FitDirection = FitDirectionType.Horizontal
                };

                doc.ExportImage(opt);

                string pattern = $"{baseName}*.png";
                string[] files = Directory.GetFiles(tempDir, pattern);
                if (files.Length == 0)
                    files = Directory.GetFiles(tempDir, "*.png");

                if (files.Length > 0)
                    result.Add(files[0]);

                using (Transaction tRestore = new Transaction(doc, $"Restaurar Ruta {ruta.Indice}"))
                {
                    tRestore.Start();
                    foreach (var kvp in backup)
                        view.SetElementOverrides(kvp.Key, kvp.Value);
                    tRestore.Commit();
                }
            }

            return result;
        }

        private List<string> GenerarPantallazoVistaSimple(Document doc, View view)
        {
            List<string> result = new List<string>();
            if (doc == null || view == null) return result;

            string tempDir = Path.Combine(Path.GetTempPath(), "MyTAGS_HVAC_RC_Snapshots");
            Directory.CreateDirectory(tempDir);

            string baseName = "Ruta_Extractor";
            string basePathNoExt = Path.Combine(tempDir, baseName);

            ImageExportOptions opt = new ImageExportOptions
            {
                ExportRange = ExportRange.CurrentView,
                FilePath = basePathNoExt,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal
            };

            doc.ExportImage(opt);

            string pattern = baseName + "*.png";
            string[] files = Directory.GetFiles(tempDir, pattern);
            if (files.Length == 0)
                files = Directory.GetFiles(tempDir, "*.png");

            if (files.Length > 0)
                result.Add(files[0]);

            return result;
        }

        // ============================================================
        // DIÁLOGO PARA GUARDAR EXCEL DESTINO
        // ============================================================

        private string PedirRutaGuardarExcelDestino()
        {
            using (WinForms.SaveFileDialog sfd = new WinForms.SaveFileDialog())
            {
                sfd.Title = "Selecciona dónde guardar la copia del Excel diligenciado";
                sfd.Filter = "Excel (*.xlsx)|*.xlsx|Excel habilitado para macros (*.xlsm)|*.xlsm|Excel 97-2003 (*.xls)|*.xls";
                sfd.AddExtension = true;
                sfd.OverwritePrompt = true;

                if (sfd.ShowDialog() == WinForms.DialogResult.OK)
                    return sfd.FileName;

                return null;
            }
        }

        private void Apply3DTemplateAndHideLevels(Document doc, View3D v3d, string templateName)
        {
            if (doc == null || v3d == null) return;

            View template = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));

            using (Transaction t = new Transaction(doc, "Asignar template 3D y ocultar niveles"))
            {
                t.Start();

                if (template != null)
                {
                    v3d.ViewTemplateId = template.Id;

                    try
                    {
                        template.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    v3d.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
                }
                catch
                {
                }

                t.Commit();
            }
        }

        private View3D GetOrCreate3DView(Document doc, string viewName = "MyTAGS_HVAC_RC_3D")
        {
            if (doc == null) return null;

            const string templateName = "ING - TRB - HVA - HVAC";

            View3D existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                Apply3DTemplateAndHideLevels(doc, existing, templateName);
                return existing;
            }

            ViewFamilyType vft3d = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft3d == null)
                throw new Exception("No se encontró ViewFamilyType de 3D en el proyecto.");

            View3D v3d = null;

            using (Transaction t = new Transaction(doc, "Crear vista 3D MyTAGS_HVAC_RC"))
            {
                t.Start();

                v3d = View3D.CreateIsometric(doc, vft3d.Id);
                v3d.Name = viewName;

                v3d.IsSectionBoxActive = true;

                t.Commit();
            }

            Apply3DTemplateAndHideLevels(doc, v3d, templateName);

            return v3d;
        }

        private List<ElementId> GetConnectedNetwork(Document doc, Element startElem, int hardLimit = 4000)
        {
            var result = new List<ElementId>();
            if (doc == null || startElem == null) return result;

            var q = new Queue<ElementId>();
            var visited = new HashSet<ElementId>();

            q.Enqueue(startElem.Id);
            visited.Add(startElem.Id);

            while (q.Count > 0)
            {
                ElementId curId = q.Dequeue();
                result.Add(curId);

                if (result.Count >= hardLimit)
                    break;

                Element cur = doc.GetElement(curId);
                if (cur == null) continue;

                foreach (Element nb in GetConnectedElements(cur))
                {
                    if (nb == null) continue;
                    if (visited.Contains(nb.Id)) continue;

                    visited.Add(nb.Id);
                    q.Enqueue(nb.Id);
                }
            }

            return result;
        }

        private BoundingBoxXYZ GetSectionBoxFromElements(Document doc, IEnumerable<ElementId> ids, double marginFeet = 2.0)
        {
            if (doc == null || ids == null) return null;

            bool hasAny = false;
            XYZ min = null, max = null;

            foreach (var id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;

                XYZ bmin = bb.Min;
                XYZ bmax = bb.Max;

                if (!hasAny)
                {
                    min = bmin; max = bmax; hasAny = true;
                }
                else
                {
                    min = new XYZ(Math.Min(min.X, bmin.X), Math.Min(min.Y, bmin.Y), Math.Min(min.Z, bmin.Z));
                    max = new XYZ(Math.Max(max.X, bmax.X), Math.Max(max.Y, bmax.Y), Math.Max(max.Z, bmax.Z));
                }
            }

            if (!hasAny) return null;

            min = new XYZ(min.X - marginFeet, min.Y - marginFeet, min.Z - marginFeet);
            max = new XYZ(max.X + marginFeet, max.Y + marginFeet, max.Z + marginFeet);

            BoundingBoxXYZ section = new BoundingBoxXYZ();
            section.Transform = Transform.Identity;
            section.Min = min;
            section.Max = max;

            return section;
        }

        private void Apply3DOverridesForSystemAndRoute(
            Document doc,
            View3D v3d,
            ICollection<ElementId> systemIds,
            ICollection<ElementId> routeIds,
            Color systemColor,
            Color routeColor)
        {
            if (doc == null || v3d == null) return;

            OverrideGraphicSettings clean = new OverrideGraphicSettings();

            using (Transaction t = new Transaction(doc, "Aplicar overrides 3D (sistema/ruta)"))
            {
                t.Start();

                if (systemIds != null)
                {
                    foreach (var id in systemIds)
                        v3d.SetElementOverrides(id, clean);
                }

                if (systemIds != null)
                {
                    var ogsSys = new OverrideGraphicSettings();
                    ogsSys.SetProjectionLineColor(systemColor);
                    ogsSys.SetProjectionLineWeight(3);

#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                    ogsSys.SetCutLineColor(systemColor);
                    ogsSys.SetCutLineWeight(3);
#endif
                    foreach (var id in systemIds)
                        v3d.SetElementOverrides(id, ogsSys);
                }

                if (routeIds != null)
                {
                    var ogsRoute = new OverrideGraphicSettings();
                    ogsRoute.SetProjectionLineColor(routeColor);
                    ogsRoute.SetProjectionLineWeight(3);

#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                    ogsRoute.SetCutLineColor(routeColor);
                    ogsRoute.SetCutLineWeight(3);
#endif
                    foreach (var id in routeIds)
                        v3d.SetElementOverrides(id, ogsRoute);
                }

                t.Commit();
            }
        }

        private string ExportViewToPng(Document doc, View view, string basePathNoExt)
        {
            if (doc == null || view == null) return null;

            ImageExportOptions opt = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = basePathNoExt,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal
            };

            opt.SetViewsAndSheets(new List<ElementId> { view.Id });
            doc.ExportImage(opt);

            string dir = Path.GetDirectoryName(basePathNoExt);
            string file = Path.GetFileName(basePathNoExt);

            string[] files = Directory.GetFiles(dir, file + "*.png");
            if (files.Length > 0) return files[0];

            string[] any = Directory.GetFiles(dir, "*.png");
            return any.Length > 0 ? any[0] : null;
        }
        private List<string> GenerarPantallazoExtractor3D(Document doc, Element equipoExtractor)
        {
            List<string> result = new List<string>();
            if (doc == null || equipoExtractor == null)
                return result;

            string tempDir = Path.Combine(Path.GetTempPath(), "MyTAGS_HVAC_RC_Extractor3D");
            Directory.CreateDirectory(tempDir);

            // Reutilizamos la misma vista 3D que para las rutas
            View3D v3d = GetOrCreate3DView(doc, "MyTAGS_HVAC_RC_3D");
            if (v3d == null)
                throw new Exception("No se pudo crear/encontrar la vista 3D para el extractor.");

            // Red de elementos conectados al equipo
            List<ElementId> networkIds = GetConnectedNetwork(doc, equipoExtractor);
            if (networkIds == null || networkIds.Count == 0)
                networkIds = new List<ElementId> { equipoExtractor.Id };

            // Section box alrededor de la red
            BoundingBoxXYZ section = GetSectionBoxFromElements(doc, networkIds, marginFeet: 2.0);

            using (Transaction t = new Transaction(doc, "Configurar SectionBox 3D Extractor"))
            {
                t.Start();

                v3d.IsSectionBoxActive = true;
                if (section != null)
                    v3d.SetSectionBox(section);

                try
                {
                    if (v3d.IsTemporaryHideIsolateActive())
                        v3d.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
                catch
                {
                }

                t.Commit();
            }

            // Solo resaltamos ductos/fittings en verde; la rejilla y el entorno quedan con su color
            List<ElementId> ductIds = new List<ElementId>();
            foreach (ElementId id in networkIds)
            {
                Element e = doc.GetElement(id);
                if (e == null || e.Category == null) continue;

                int cat = e.Category.Id.IntegerValue;
                if (cat == (int)BuiltInCategory.OST_DuctCurves ||
                    cat == (int)BuiltInCategory.OST_FlexDuctCurves ||
                    cat == (int)BuiltInCategory.OST_DuctFitting)
                {
                    ductIds.Add(id);
                }
            }

            Color green = new Color(0, 255, 0);

            // systemIds = null → solo aplica override a routeIds (ductos)
            Apply3DOverridesForSystemAndRoute(
                doc,
                v3d,
                null,
                ductIds,
                new Color(0, 0, 255),   // no se usa si systemIds es null
                green);

            // Exportar imagen
            string baseName = "Extractor3D";
            string basePath = Path.Combine(tempDir, baseName);

            string pngPath = ExportViewToPng(doc, v3d, basePath);
            if (!string.IsNullOrWhiteSpace(pngPath))
                result.Add(pngPath);

            return result;
        }


        private List<string> GenerarPantallazosRutas3D(Document doc, List<RouteInfo> rutas)
        {
            List<string> result = new List<string>();
            if (doc == null || rutas == null || rutas.Count == 0) return result;

            string tempDir = Path.Combine(Path.GetTempPath(), "MyTAGS_HVAC_RC_Snapshots_3D");
            Directory.CreateDirectory(tempDir);

            View3D v3d = GetOrCreate3DView(doc, "MyTAGS_HVAC_RC_3D");
            if (v3d == null) throw new Exception("No se pudo crear/encontrar la vista 3D para snapshots.");

            //Color systemBlue = new Color(0, 0, 255);

            foreach (var ruta in rutas)
            {
                Element startElem = null;
                if (ruta.ElementIds != null && ruta.ElementIds.Count > 0)
                    startElem = doc.GetElement(ruta.ElementIds.First());

                List<ElementId> systemIds = startElem != null
                    ? GetConnectedNetwork(doc, startElem)
                    : new List<ElementId>();

                BoundingBoxXYZ section = GetSectionBoxFromElements(doc,
                    (systemIds != null && systemIds.Count > 0) ? systemIds : ruta.ElementIds,
                    marginFeet: 2.0);

                using (Transaction t = new Transaction(doc, $"Configurar SectionBox 3D Ruta {ruta.Indice}"))
                {
                    t.Start();
                    v3d.IsSectionBoxActive = true;
                    if (section != null) v3d.SetSectionBox(section);

                    try
                    {
                        if (v3d.IsTemporaryHideIsolateActive())
                            v3d.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    }
                    catch { }

                    t.Commit();
                }

                Color routeColor = ruta.RouteColor != null ? ruta.RouteColor : new Color(255, 0, 0);

                Apply3DOverridesForSystemAndRoute(doc, v3d, null, ruta.ElementIds, new Color(0, 0, 0), routeColor);


                string baseName = $"Ruta3D_{ruta.Indice}";
                string basePath = Path.Combine(tempDir, baseName);

                string pngPath = ExportViewToPng(doc, v3d, basePath);
                result.Add(pngPath);
            }

            return result;
        }

        // ============================================================
        // EXCEL – MULTIRUTA HORIZONTAL
        // ============================================================

        private string GetExcelColumnLetter(int colIndex)
        {
            int dividend = colIndex;
            string col = "";
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                col = Convert.ToChar('A' + modulo) + col;
                dividend = (dividend - 1) / 26;
            }
            return col;
        }

        private void ReplicarPlantillaPorRuta(Excel.Worksheet xlWs, int rutas)
        {
            if (xlWs == null) return;
            if (rutas <= 1) return;

            int maxRows = 70;

            for (int r = 2; r <= rutas; r++)
            {
                int srcStartCol = 1; // A
                int srcEndCol = 5;   // E

                int dstStartCol = 1 + (r - 1) * 6; // Ruta2:7(G), Ruta3:13(M)...
                int dstEndCol = dstStartCol + 4;

                string srcStart = GetExcelColumnLetter(srcStartCol) + "1";
                string srcEnd = GetExcelColumnLetter(srcEndCol) + maxRows.ToString();

                string dstStart = GetExcelColumnLetter(dstStartCol) + "1";
                string dstEnd = GetExcelColumnLetter(dstEndCol) + maxRows.ToString();

                Excel.Range srcRange = xlWs.Range[srcStart, srcEnd];
                Excel.Range dstRange = xlWs.Range[dstStart, dstEnd];

                srcRange.Copy(dstRange);
            }
        }

        private void InsertarImagenesRutas(
            Excel.Worksheet xlWs,
            List<string> snapshotPaths,
            int filaAnchorImagen,
            int colBaseBloque)
        {
            if (xlWs == null) return;
            if (snapshotPaths == null || snapshotPaths.Count == 0) return;

            for (int r = 0; r < snapshotPaths.Count; r++)
            {
                string path = snapshotPaths[r];
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    continue;

                int baseCol = colBaseBloque + r * 6;
                int colImagen = baseCol + 1; // B, H, N...

                Excel.Range cellAnchor = xlWs.Cells[filaAnchorImagen, colImagen] as Excel.Range;
                if (cellAnchor == null) continue;

                // Coordenadas de la celda
                float left = (float)(double)cellAnchor.Left;
                float top = (float)(double)cellAnchor.Top;

                // Leer tamaño real de la imagen (en píxeles)
                float imgWidth;
                float imgHeight;
                using (Drawing.Image img = Drawing.Image.FromFile(path))
                {
                    imgWidth = img.Width;
                    imgHeight = img.Height;
                }

                // Usamos 'dynamic' para no depender de Microsoft.Office.Core.MsoTriState
                dynamic shapes = xlWs.Shapes;

                dynamic picShape = shapes.AddPicture(
                    path,
                    0,     // LinkToFile = msoFalse (no link)
                    -1,    // SaveWithDocument = msoCTrue (embebida en el archivo)
                    left,
                    top,
                    imgWidth,
                    imgHeight
                );

                // Escalar si es demasiado ancho
                double maxWidth = 260.0;
                if ((double)picShape.Width > maxWidth)
                {
                    double scale = maxWidth / (double)picShape.Width;
                    picShape.Width = maxWidth;
                    picShape.Height = (double)picShape.Height * scale;
                }

                // Que se mueva y dimensione con las celdas
                picShape.Placement = Excel.XlPlacement.xlMoveAndSize;
            }
        }

        private void RellenarHojaSistemaYGuardarCopia_Multiruta(
            string plantillaPath,
            string destinoPath,
            string nombreHojaBase,
            string nombreHojaSistema,
            string nombreHojaResumen,
            List<RouteExcelData> datosRutas,
            List<string> snapshotPaths,
            bool actualizarExcelExistente)
        {
            Excel.Application xlApp = null;
            Excel.Workbook xlWb = null;
            Excel.Worksheet xlWsBase = null;    // plantilla VS-RC / VE-RC / EI-RC
            Excel.Worksheet xlWs = null;        // hoja VS-RC-01 / VE-RC-05 ...
            Excel.Worksheet xlWsResumen = null; // hoja VS-01 / VE-05 ...
            bool success = false;

            try
            {
                xlApp = new Excel.Application();
                xlApp.DisplayAlerts = false;

                // Abrir libro: existente o desde plantilla
                if (actualizarExcelExistente)
                {
                    xlWb = xlApp.Workbooks.Open(destinoPath);
                }
                else
                {
                    xlWb = xlApp.Workbooks.Open(plantillaPath);
                }

                // 1) Localizar hoja BASE (plantilla: VS-RC / VE-RC / EI-RC)
                xlWsBase = xlWb.Worksheets[nombreHojaBase] as Excel.Worksheet;
                if (xlWsBase == null)
                    throw new Exception($"No se encontró la hoja plantilla '{nombreHojaBase}' en el archivo de Excel.");

                // 2) Buscar si ya existe la hoja del sistema (VS-RC-01, VE-RC-05, etc.)
                foreach (Excel.Worksheet ws in xlWb.Worksheets)
                {
                    if (ws.Name == nombreHojaSistema)
                    {
                        xlWs = ws;
                        break;
                    }
                }

                // 3) Si no existe, clonar la plantilla y renombrar
                if (xlWs == null)
                {
                    xlWsBase.Copy(After: xlWsBase);         // crea copia justo después
                    xlWs = xlWb.ActiveSheet as Excel.Worksheet;
                    xlWs.Name = nombreHojaSistema;
                }

                // ------------------------------------------------
                //  A partir de aquí TRABAJAMOS SIEMPRE SOBRE xlWs
                //  (la hoja del sistema VS-RC-01 / VE-RC-05...)
                // ------------------------------------------------

                // Replicar estructura por cada ruta
                ReplicarPlantillaPorRuta(xlWs, datosRutas.Count);

                // ------------------------------------------------
                //  RELLENAR DATOS POR RUTA
                // ------------------------------------------------
                for (int r = 0; r < datosRutas.Count; r++)
                {
                    RouteExcelData data = datosRutas[r];

                    int baseCol = 1 + r * 6;   // 1=A, 7=G, 13=M...

                    string colArea = GetExcelColumnLetter(baseCol + 1); // B, H, N...
                    string colCaudal = colArea;
                    string colCodo = GetExcelColumnLetter(baseCol + 2); // C, I, O...
                    string colDesc = GetExcelColumnLetter(baseCol);     // A, G, M...
                    string colUni = GetExcelColumnLetter(baseCol + 2);  // C, I, O...
                    string colDP = GetExcelColumnLetter(baseCol + 3);   // D, J, P...

                    string colMargen = GetExcelColumnLetter(baseCol + 3);
                    if (!string.IsNullOrWhiteSpace(data.NombreRuta))
                    {
                        xlWs.Range[colMargen + "9"].Value2 = data.NombreRuta;
                    }

                    xlWs.Range[colArea + "10"].Value2 = string.IsNullOrWhiteSpace(data.AreaAtendida)
                        ? null : (object)data.AreaAtendida;
                    xlWs.Range[colCaudal + "11"].Value2 = string.IsNullOrWhiteSpace(data.CaudalCfm)
                        ? null : (object)data.CaudalCfm;

                    xlWs.Range[colCodo + "36"].Value2 = string.IsNullOrWhiteSpace(data.ValorC36)
                        ? null : (object)data.ValorC36;
                    xlWs.Range[colCodo + "37"].Value2 = string.IsNullOrWhiteSpace(data.ValorC37)
                        ? null : (object)data.ValorC37;

                    // C40 = tramos rectos
                    xlWs.Range[colCodo + "40"].Value2 = string.IsNullOrWhiteSpace(data.TramosRectos)
                        ? null : (object)data.TramosRectos;

                    xlWs.Range[colCodo + "41"].Value2 = string.IsNullOrWhiteSpace(data.Codos90MasT)
                        ? null : (object)data.Codos90MasT;
                    xlWs.Range[colCodo + "42"].Value2 = string.IsNullOrWhiteSpace(data.Codos45)
                        ? null : (object)data.Codos45;

                    string[] desc = data.Descripciones ?? new string[0];
                    string[] uni = data.Cantidades ?? new string[0];
                    string[] dp = data.CaidasPresion ?? new string[0];

                    for (int i = 0; i < 10; i++)
                    {
                        int row = 43 + i;

                        string d = i < desc.Length ? desc[i] : string.Empty;
                        string u = i < uni.Length ? uni[i] : string.Empty;
                        string p = i < dp.Length ? dp[i] : string.Empty;

                        xlWs.Range[colDesc + row.ToString()].Value2 =
                            string.IsNullOrWhiteSpace(d) ? null : (object)d;
                        xlWs.Range[colUni + row.ToString()].Value2 =
                            string.IsNullOrWhiteSpace(u) ? null : (object)u;
                        xlWs.Range[colDP + row.ToString()].Value2 =
                            string.IsNullOrWhiteSpace(p) ? null : (object)p;
                    }
                }

                // ------------------------------------------------
                //  CÁLCULO DE RUTA GANADORA (mayor caída de presión)
                //  Usando B12, H12, N12... como antes
                // ------------------------------------------------
                List<(int routeIndex, Excel.Range celda, double valor)> listaValores =
                    new List<(int, Excel.Range, double)>();

                for (int r = 0; r < datosRutas.Count; r++)
                {
                    int baseCol = 1 + r * 6;
                    int colB = baseCol + 1;

                    string letraCol = GetExcelColumnLetter(colB);
                    Excel.Range celda = xlWs.Range[letraCol + "12"];

                    if (celda != null && celda.Value2 != null)
                    {
                        double val;
                        if (double.TryParse(celda.Value2.ToString(), out val))
                        {
                            listaValores.Add((r, celda, val));
                        }
                    }
                }

                int bestRouteIndex = -1;

                if (listaValores.Count > 0)
                {
                    double maxVal = listaValores.Max(t => t.valor);

                    foreach (var item in listaValores)
                    {
                        if (item.valor == maxVal)
                        {
                            item.celda.Interior.Color =
                                System.Drawing.ColorTranslator.ToOle(
                                    System.Drawing.Color.FromArgb(0, 255, 255));
                        }
                    }

                    bestRouteIndex = listaValores.First(t => t.valor == maxVal).routeIndex;
                }

                // ------------------------------------------------
                //  HOJA RESUMEN POR SISTEMA (VS-05, VE-03, EI-02...)
                // ------------------------------------------------
                if (bestRouteIndex >= 0)
                {
                    int maxRows = 70;
                    int baseColBest = 1 + bestRouteIndex * 6;

                    string startColBest = GetExcelColumnLetter(baseColBest);
                    string endColBest = GetExcelColumnLetter(baseColBest + 4);

                    Excel.Range srcRange = xlWs.Range[startColBest + "1", endColBest + maxRows.ToString()];

                    string resumenSheetName = nombreHojaResumen;

                    // Buscar si ya existe esa hoja resumen
                    foreach (Excel.Worksheet ws in xlWb.Worksheets)
                    {
                        if (ws.Name == resumenSheetName)
                        {
                            xlWsResumen = ws;
                            break;
                        }
                    }

                    // Si no existe, se crea
                    if (xlWsResumen == null)
                    {
                        xlWsResumen = xlWb.Worksheets.Add();
                        xlWsResumen.Name = resumenSheetName;
                    }

                    // Limpiar y copiar formato/datos de la ruta ganadora
                    xlWsResumen.Cells.Clear();

                    Excel.Range dstRange = xlWsResumen.Range["A1", "E" + maxRows.ToString()];
                    srcRange.Copy(dstRange);

                    // Copiar alturas de fila
                    for (int r = 1; r <= maxRows; r++)
                    {
                        Excel.Range srcRow = xlWs.Rows[r];
                        Excel.Range dstRow = xlWsResumen.Rows[r];
                        dstRow.RowHeight = srcRow.RowHeight;
                    }

                    // Copiar anchos de columna
                    for (int c = 0; c < 5; c++)
                    {
                        int srcColIndex = baseColBest + c;
                        int dstColIndex = 1 + c;

                        Excel.Range srcCol = xlWs.Columns[srcColIndex];
                        Excel.Range dstCol = xlWsResumen.Columns[dstColIndex];
                        dstCol.ColumnWidth = srcCol.ColumnWidth;
                    }

                    // Insertar pantallazo SOLO de la ruta ganadora
                    if (snapshotPaths != null &&
                        bestRouteIndex < snapshotPaths.Count &&
                        !string.IsNullOrWhiteSpace(snapshotPaths[bestRouteIndex]))
                    {
                        var snapGanadora = new List<string> { snapshotPaths[bestRouteIndex] };
                        InsertarImagenesRutas(xlWsResumen, snapGanadora, filaAnchorImagen: 17, colBaseBloque: 1);
                    }
                }

                // ------------------------------------------------
                //  IMÁGENES EN LA HOJA DEL SISTEMA (todas las rutas)
                // ------------------------------------------------
                if (snapshotPaths != null && snapshotPaths.Count > 0)
                {
                    InsertarImagenesRutas(xlWs, snapshotPaths, filaAnchorImagen: 17, colBaseBloque: 1);
                }

                // ------------------------------------------------
                //  GUARDAR
                // ------------------------------------------------
                if (actualizarExcelExistente)
                {
                    xlWb.Save();
                }
                else
                {
                    xlWb.SaveCopyAs(destinoPath);
                }

                success = true;
            }
            finally
            {
                try
                {
                    if (xlWb != null)
                        xlWb.Close(false);
                }
                catch { }

                try
                {
                    if (xlApp != null)
                        xlApp.Quit();
                }
                catch { }

                if (xlWsResumen != null) Marshal.ReleaseComObject(xlWsResumen);
                if (xlWs != null) Marshal.ReleaseComObject(xlWs);
                if (xlWsBase != null) Marshal.ReleaseComObject(xlWsBase);
                if (xlWb != null) Marshal.ReleaseComObject(xlWb);
                if (xlApp != null) Marshal.ReleaseComObject(xlApp);

                xlWsResumen = null;
                xlWs = null;
                xlWsBase = null;
                xlWb = null;
                xlApp = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (success && !string.IsNullOrWhiteSpace(destinoPath) && File.Exists(destinoPath))
                {
                    Process.Start(destinoPath);
                }
            }
        }
    }

    // ============================================================
    // FORM PARA COLOREAR RUTAS Y TEXTOS
    // ============================================================

    public class RouteColorForm : WinForms.Form
    {
        private Document _doc;
        private View _view;
        private List<RouteInfo> _rutas;
        private WinForms.ListBox _lstRutas;
        private WinForms.Button _btnColorear;
        private WinForms.Button _btnRestaurar;
        private WinForms.Button _btnCerrar;

        private Dictionary<ElementId, OverrideGraphicSettings> _originalOverrides =
            new Dictionary<ElementId, OverrideGraphicSettings>();

        private List<ElementId> _createdTextNotes = new List<ElementId>();

        private ElementId _solidFillPatternId = ElementId.InvalidElementId;

        private List<Color> _palette = new List<Color>
        {
            new Color(255, 0, 0),
            new Color(0, 0, 255),
            new Color(0, 255, 0),
            new Color(255, 0, 255),
            new Color(0, 255, 255),
            new Color(255, 128, 0),
            new Color(128, 0, 255),
            new Color(255, 255, 0)
        };

        public RouteColorForm(Document doc, View view, List<RouteInfo> rutas)
        {
            _doc = doc;
            _view = view;
            _rutas = rutas ?? new List<RouteInfo>();

            this.Text = "Rutas HVAC - Colores y etiquetas";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(500, 320);

            try
            {
                var fpCollector = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement));

                foreach (FillPatternElement fpe in fpCollector)
                {
                    FillPattern fp = fpe.GetFillPattern();
                    if (fp.IsSolidFill)
                    {
                        _solidFillPatternId = fpe.Id;
                        break;
                    }
                }
            }
            catch
            {
                _solidFillPatternId = ElementId.InvalidElementId;
            }

            _lstRutas = new WinForms.ListBox()
            {
                Location = new Drawing.Point(10, 10),
                Size = new Drawing.Size(470, 220)
            };

            int i = 0;
            foreach (var r in _rutas)
            {
                Color c = _palette[i % _palette.Count];
                r.RouteColor = c;

                string itemText =
                    $"Ruta {r.Indice} | C90={r.Codos90}, C45={r.Codos45}, T={r.Tees} | Color RGB({c.Red},{c.Green},{c.Blue})";

                _lstRutas.Items.Add(itemText);
                i++;
            }

            _btnColorear = new WinForms.Button()
            {
                Text = "Aplicar colores y textos",
                Location = new Drawing.Point(10, 240),
                Size = new Drawing.Size(160, 30)
            };
            _btnColorear.Click += (s, e) => AplicarColoresYTextos();

            _btnRestaurar = new WinForms.Button()
            {
                Text = "Restaurar colores y textos",
                Location = new Drawing.Point(180, 240),
                Size = new Drawing.Size(180, 30)
            };
            _btnRestaurar.Click += (s, e) => RestaurarColoresYTextos();

            _btnCerrar = new WinForms.Button()
            {
                Text = "Cerrar",
                Location = new Drawing.Point(390, 240),
                Size = new Drawing.Size(90, 30),
                DialogResult = WinForms.DialogResult.OK
            };

            this.Controls.Add(_lstRutas);
            this.Controls.Add(_btnColorear);
            this.Controls.Add(_btnRestaurar);
            this.Controls.Add(_btnCerrar);

            this.AcceptButton = _btnCerrar;
        }

        private ElementId GetDefaultTextNoteTypeId()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType));

            if (!collector.Any())
                return ElementId.InvalidElementId;

            return collector.FirstElementId();
        }

        private void AplicarColoresYTextos()
        {
            if (_doc == null || _view == null || _rutas.Count == 0)
                return;

            if (_createdTextNotes.Count > 0)
            {
                using (Transaction tDel = new Transaction(_doc, "Eliminar textos de rutas anteriores"))
                {
                    tDel.Start();
                    foreach (var id in _createdTextNotes)
                    {
                        try { _doc.Delete(id); } catch { }
                    }
                    tDel.Commit();
                }
                _createdTextNotes.Clear();
            }

            ElementId textTypeId = GetDefaultTextNoteTypeId();
            if (textTypeId == ElementId.InvalidElementId)
            {
                WinForms.MessageBox.Show(
                    "No se encontró un tipo de texto (TextNoteType) en el modelo.",
                    "Rutas HVAC",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
            }

            using (Transaction t = new Transaction(_doc, "Colorear rutas HVAC y crear textos"))
            {
                t.Start();

                foreach (var ruta in _rutas)
                {
                    if (ruta.ElementIds == null) continue;

                    foreach (ElementId id in ruta.ElementIds)
                    {
                        if (!_originalOverrides.ContainsKey(id))
                        {
                            OverrideGraphicSettings ogsOriginal = _view.GetElementOverrides(id);
                            _originalOverrides[id] = ogsOriginal;
                        }

                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();

                        ogs.SetProjectionLineColor(ruta.RouteColor);
                        ogs.SetProjectionLineWeight(3);
#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                        ogs.SetCutLineColor(ruta.RouteColor);
                        ogs.SetCutLineWeight(3);
#endif

#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                        if (_solidFillPatternId != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceForegroundPatternId(_solidFillPatternId);
                            ogs.SetSurfaceForegroundPatternColor(ruta.RouteColor);
                            ogs.SetCutForegroundPatternId(_solidFillPatternId);
                            ogs.SetCutForegroundPatternColor(ruta.RouteColor);
                        }
#endif
                        _view.SetElementOverrides(id, ogs);
                    }

                    if (textTypeId != ElementId.InvalidElementId && ruta.EndElementId != null)
                    {
                        Element endElem = _doc.GetElement(ruta.EndElementId);
                        if (endElem != null)
                        {
                            XYZ pt = null;
                            if (endElem.Location is LocationPoint lp)
                            {
                                pt = lp.Point;
                            }
                            else if (endElem.Location is LocationCurve lc)
                            {
                                pt = lc.Curve.GetEndPoint(1);
                            }

                            if (pt != null)
                            {
                                XYZ offset = new XYZ(0.5, 0.5, 0);
                                XYZ textPoint = pt + offset;

                                TextNote note = TextNote.Create(
                                    _doc,
                                    _view.Id,
                                    textPoint,
                                    $"Ruta {ruta.Indice}",
                                    textTypeId);

                                if (note != null)
                                    _createdTextNotes.Add(note.Id);
                            }
                        }
                    }
                }

                t.Commit();
            }
        }

        private void RestaurarColoresYTextos()
        {
            if (_doc == null || _view == null)
                return;

            using (Transaction t = new Transaction(_doc, "Restaurar rutas HVAC"))
            {
                t.Start();

                foreach (var kvp in _originalOverrides)
                {
                    _view.SetElementOverrides(kvp.Key, kvp.Value);
                }
                _originalOverrides.Clear();

                foreach (var id in _createdTextNotes)
                {
                    try { _doc.Delete(id); } catch { }
                }
                _createdTextNotes.Clear();

                t.Commit();
            }
        }
    }

    // ============================================================
    // SELECTION FILTERS
    // ============================================================

    public class MechanicalEquipmentSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipment;
        }

        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    public class DuctTerminalSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DuctTerminal;
        }

        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    // ============================================================
    // CLASES AUXILIARES PARA RUTAS / EXCEL
    // ============================================================

    public class RouteInfo
    {
        public int Indice { get; set; }
        public int Codos90 { get; set; }
        public int Codos45 { get; set; }
        public int Tees { get; set; }
        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();
        public ElementId EndElementId { get; set; }
        public Color RouteColor { get; set; }
        public string CaudalCfmTexto { get; set; }

        public int TotalCodos => Codos90 + Codos45 + Tees;
    }

    public class RouteExcelData
    {
        public string NombreRuta { get; set; }
        public string AreaAtendida { get; set; }
        public string CaudalCfm { get; set; }
        public string ValorC36 { get; set; }
        public string ValorC37 { get; set; }

        // C40
        public string TramosRectos { get; set; }

        public string Codos90MasT { get; set; }
        public string Codos45 { get; set; }
        public string[] Descripciones { get; set; }
        public string[] Cantidades { get; set; }
        public string[] CaidasPresion { get; set; }
    }

    // ============================================================
    // FORM MENÚ INICIAL
    // ============================================================

    public class HVACMainMenuForm : WinForms.Form
    {
        private WinForms.Button btnLimpiar;
        private WinForms.Button btnFlujo;
        private WinForms.Button btnCancelar;

        public string OpcionSeleccionada { get; private set; } = null;

        public HVACMainMenuForm()
        {
            this.Text = "MyTAGS_HVAC_RC - Menú inicial";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(420, 160);

            btnLimpiar = new WinForms.Button()
            {
                Text = "1. Limpiar colores de rutas",
                Location = new Drawing.Point(20, 20),
                Size = new Drawing.Size(370, 30)
            };
            btnLimpiar.Click += (s, e) =>
            {
                OpcionSeleccionada = "LIMPIAR";
                this.DialogResult = WinForms.DialogResult.OK;
                this.Close();
            };

            btnFlujo = new WinForms.Button()
            {
                Text = "2. Generar rutas y Excel",
                Location = new Drawing.Point(20, 60),
                Size = new Drawing.Size(370, 30)
            };
            btnFlujo.Click += (s, e) =>
            {
                OpcionSeleccionada = "FLUJO";
                this.DialogResult = WinForms.DialogResult.OK;
                this.Close();
            };

            btnCancelar = new WinForms.Button()
            {
                Text = "Cancelar",
                Location = new Drawing.Point(310, 110),
                Size = new Drawing.Size(80, 25),
                DialogResult = WinForms.DialogResult.Cancel
            };

            this.Controls.Add(btnLimpiar);
            this.Controls.Add(btnFlujo);
            this.Controls.Add(btnCancelar);

            this.CancelButton = btnCancelar;
        }
    }

    // ============================================================
    // FORM SELECCIÓN TIPO DE SISTEMA
    // ============================================================

    public class SeleccionarSistemaForm : WinForms.Form
    {
        private WinForms.RadioButton rbSuministro;
        private WinForms.RadioButton rbReVentilacion;
        private WinForms.RadioButton rbExtractor;
        private WinForms.Button btnOK;
        private WinForms.Button btnCancel;

        public string TipoSistemaSeleccionado
        {
            get
            {
                if (rbSuministro.Checked) return "SUMINISTRO";
                if (rbReVentilacion.Checked) return "RE_VENTILACION";
                if (rbExtractor.Checked) return "EXTRACTOR";
                return null;
            }
        }

        public SeleccionarSistemaForm()
        {
            this.Text = "Seleccionar tipo de sistema";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(400, 200);

            rbSuministro = new WinForms.RadioButton()
            {
                Text = "Suministro (VS-RC)",
                Location = new Drawing.Point(20, 20),
                AutoSize = true,
                Checked = true
            };

            rbReVentilacion = new WinForms.RadioButton()
            {
                Text = "Re ventilación / Extracción (VE-01)",
                Location = new Drawing.Point(20, 50),
                AutoSize = true
            };

            rbExtractor = new WinForms.RadioButton()
            {
                Text = "Extractor individual (EI-RC)",
                Location = new Drawing.Point(20, 80),
                AutoSize = true
            };

            btnOK = new WinForms.Button()
            {
                Text = "Aceptar",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(210, 140),
                Size = new Drawing.Size(80, 25)
            };

            btnCancel = new WinForms.Button()
            {
                Text = "Cancelar",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(300, 140),
                Size = new Drawing.Size(80, 25)
            };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(rbSuministro);
            this.Controls.Add(rbReVentilacion);
            this.Controls.Add(rbExtractor);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
        }
    }

    // ============================================================
    // FORM SELECCIÓN MODO DE ARCHIVO EXCEL (NUEVO / EXISTENTE)
    // ============================================================

    public class ExcelModeForm : WinForms.Form
    {
        private WinForms.RadioButton rbNuevo;
        private WinForms.RadioButton rbExistente;
        private WinForms.Button btnOK;
        private WinForms.Button btnCancel;

        // true  = usar y actualizar un archivo ya guardado
        // false = crear archivo nuevo desde la plantilla
        public bool UsarArchivoExistente => rbExistente.Checked;

        public ExcelModeForm()
        {
            this.Text = "Seleccionar modo de archivo Excel";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(420, 170);
            this.Font = new Drawing.Font("Segoe UI", 10f);

            rbNuevo = new WinForms.RadioButton()
            {
                Text = "1. Crear archivo nuevo a partir de la plantilla",
                Location = new Drawing.Point(20, 25),
                AutoSize = true,
                Checked = true          // opción por defecto
            };

            rbExistente = new WinForms.RadioButton()
            {
                Text = "2. Usar y actualizar un archivo ya guardado",
                Location = new Drawing.Point(20, 55),
                AutoSize = true
            };

            btnOK = new WinForms.Button()
            {
                Text = "Aceptar",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(220, 110),
                Size = new Drawing.Size(80, 28)
            };

            btnCancel = new WinForms.Button()
            {
                Text = "Cancelar",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(310, 110),
                Size = new Drawing.Size(80, 28)
            };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(rbNuevo);
            this.Controls.Add(rbExistente);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
        }
    }

    // ============================================================
    // FORM PARA ESCOGER NÚMERO DE SISTEMA (VS-05, VE-03, EI-07...)
    // ============================================================

    public class SeleccionarNumeroSistemaForm : WinForms.Form
    {
        private WinForms.NumericUpDown nudNumero;
        private WinForms.Button btnOK;
        private WinForms.Button btnCancel;

        public int NumeroSistema => (int)nudNumero.Value;

        public SeleccionarNumeroSistemaForm(string tipoSistema)
        {
            this.Text = "Número de sistema";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(420, 150);
            this.Font = new Drawing.Font("Segoe UI", 10f);

            string etiquetaTipo = tipoSistema ?? "sistema";

            var lbl = new WinForms.Label()
            {
                Text = $"Ingrese el número de {etiquetaTipo} (ej. 1, 2, 5):",
                AutoSize = true,
                Location = new Drawing.Point(20, 20)
            };

            nudNumero = new WinForms.NumericUpDown()
            {
                Location = new Drawing.Point(20, 50),
                Size = new Drawing.Size(100, 24),
                Minimum = 1,
                Maximum = 99,
                Value = 1
            };

            btnOK = new WinForms.Button()
            {
                Text = "Aceptar",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(230, 100),
                Size = new Drawing.Size(80, 25)
            };

            btnCancel = new WinForms.Button()
            {
                Text = "Cancelar",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(320, 100),
                Size = new Drawing.Size(80, 25)
            };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
            this.Controls.Add(lbl);
            this.Controls.Add(nudNumero);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
        }
    }

    public class SeleccionarTipoRutaForm : WinForms.Form
    {
        private WinForms.RadioButton rbEquipoRejilla;
        private WinForms.RadioButton rbRejillaRejilla;
        private WinForms.Button btnOK;
        private WinForms.Button btnCancel;

        // true = Equipo → Rejilla, false = Rejilla → Rejilla
        public bool EsRutaEquipoRejilla
        {
            get { return rbEquipoRejilla.Checked; }
        }

        public SeleccionarTipoRutaForm()
        {
            this.Text = "Seleccionar tipo de ruta";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Drawing.Size(400, 180);

            rbEquipoRejilla = new WinForms.RadioButton()
            {
                Text = "1. Equipo → Rejilla",
                Location = new Drawing.Point(20, 20),
                AutoSize = true,
                Checked = true
            };

            rbRejillaRejilla = new WinForms.RadioButton()
            {
                Text = "2. Rejilla → Rejilla",
                Location = new Drawing.Point(20, 50),
                AutoSize = true
            };

            btnOK = new WinForms.Button()
            {
                Text = "Aceptar",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(210, 110),
                Size = new Drawing.Size(80, 25)
            };

            btnCancel = new WinForms.Button()
            {
                Text = "Cancelar",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(300, 110),
                Size = new Drawing.Size(80, 25)
            };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            this.Controls.Add(rbEquipoRejilla);
            this.Controls.Add(rbRejillaRejilla);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
        }
    }

    // ============================================================
    // FORM VALORES VS/VE/EI + DINÁMICOS (VERSIÓN XL)
    // ============================================================

    public class DatosVS01Form : WinForms.Form
    {
        private WinForms.TextBox txtAreaAtendida;
        private WinForms.TextBox txtCaudalCfm;
        private WinForms.TextBox txtCodos90;
        private WinForms.TextBox txtCodos45;
        private WinForms.TextBox txtC36;
        private WinForms.TextBox txtC37;

        private WinForms.TextBox[] txtDescs = new WinForms.TextBox[10];
        private WinForms.TextBox[] txtUnits = new WinForms.TextBox[10];
        private WinForms.TextBox[] txtDP = new WinForms.TextBox[10];

        private WinForms.Button btnOK;
        private WinForms.Button btnCancel;

        public string ValorAreaAtendida => txtAreaAtendida.Text.Trim();
        public string ValorCaudalCfm => txtCaudalCfm.Text.Trim();
        public string ValorCodos90 => txtCodos90.Text.Trim();
        public string ValorCodos45 => txtCodos45.Text.Trim();
        public string ValorC36 => txtC36.Text.Trim();
        public string ValorC37 => txtC37.Text.Trim();

        public string[] Descripciones => txtDescs.Select(t => t.Text.Trim()).ToArray();
        public string[] Cantidades => txtUnits.Select(t => t.Text.Trim()).ToArray();
        public string[] CaidasPresion => txtDP.Select(t => t.Text.Trim()).ToArray();

        public DatosVS01Form(string nombreRuta, int codos90MasT, int codos45Auto, bool codosAutomaticos, string caudalInicial)
        {
            this.Text = "Valores para hoja de sistema (VS-RC / VE-RC / EI-RC)";
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            this.Font = new Drawing.Font("Segoe UI", 11f);

            WinForms.Label lblRutaTitulo = new WinForms.Label()
            {
                Text = $"Datos para {nombreRuta}",
                AutoSize = true,
                Font = new Drawing.Font("Segoe UI", 11f, Drawing.FontStyle.Bold),
                Location = new Drawing.Point(40, 10)
            };

            int xLabel = 40;
            int xText = 330;
            int y = 50;
            int dy = 40;

            string textoC41 = codosAutomaticos
                ? "Codos 90° + Tees [C41] (auto):"
                : "Codos 90° [C41] (manual):";

            string textoC42 = codosAutomaticos
                ? "Codos 45° [C42] (auto):"
                : "Codos 45° [C42] (manual):";

            WinForms.Label lblAreaAtendida = new WinForms.Label()
            {
                Text = "Area atendida [B10]:",
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };
            y += dy;

            WinForms.Label lblCaudal = new WinForms.Label()
            {
                Text = "Caudal (CFM):",
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };
            y += dy;

            WinForms.Label lblC36 = new WinForms.Label()
            {
                Text = "Velocidad inicial (fpm):",
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };
            y += dy;

            WinForms.Label lblC37 = new WinForms.Label()
            {
                Text = "Caída de presión por cada 100 ft:",
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };
            y += dy;

            WinForms.Label lblCodos90 = new WinForms.Label()
            {
                Text = textoC41,
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };
            y += dy;

            WinForms.Label lblCodos45 = new WinForms.Label()
            {
                Text = textoC42,
                AutoSize = true,
                Location = new Drawing.Point(xLabel, y)
            };

            int yTxt = 47;

            txtAreaAtendida = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(300, 28)
            };

            yTxt += dy;
            txtCaudalCfm = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(300, 28)
            };

            if (!string.IsNullOrWhiteSpace(caudalInicial))
            {
                txtCaudalCfm.Text = caudalInicial;
                txtCaudalCfm.ReadOnly = true;
            }
            else
            {
                txtCaudalCfm.ReadOnly = true;
            }

            yTxt += dy;
            txtC36 = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(300, 28)
            };

            yTxt += dy;
            txtC37 = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(300, 28)
            };

            yTxt += dy;
            txtCodos90 = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(150, 28),
                ReadOnly = false
            };

            yTxt += dy;
            txtCodos45 = new WinForms.TextBox()
            {
                Location = new Drawing.Point(xText, yTxt),
                Size = new Drawing.Size(150, 28),
                ReadOnly = false
            };

            txtCodos90.Text = codos90MasT.ToString();
            txtCodos45.Text = codos45Auto.ToString();

            // --- GroupBox dinámico debajo de los inputs superiores ---
            int topInputsBottom = txtCodos45.Bottom;
            int gap = 20;
            int grpTop = topInputsBottom + gap;

            WinForms.GroupBox grp = new WinForms.GroupBox()
            {
                Text = "Elementos dinámicos",
                Location = new Drawing.Point(20, grpTop),
                Size = new Drawing.Size(955, 420)
            };

            grp.Controls.Add(new WinForms.Label()
            {
                Text = "Fila",
                Location = new Drawing.Point(20, 35),
                AutoSize = true
            });

            grp.Controls.Add(new WinForms.Label()
            {
                Text = "Descripción",
                Location = new Drawing.Point(90, 35),
                AutoSize = true
            });

            grp.Controls.Add(new WinForms.Label()
            {
                Text = "Cantidad",
                Location = new Drawing.Point(450, 35),
                AutoSize = true
            });

            grp.Controls.Add(new WinForms.Label()
            {
                Text = "Caída de presión [pulg c.a]",
                Location = new Drawing.Point(580, 35),
                AutoSize = true
            });

            int rowY = 70;
            for (int i = 0; i < 10; i++)
            {
                int filaExcel = 43 + i;

                grp.Controls.Add(new WinForms.Label()
                {
                    Text = filaExcel.ToString(),
                    Location = new Drawing.Point(20, rowY + 5),
                    AutoSize = true
                });

                txtDescs[i] = new WinForms.TextBox()
                {
                    Location = new Drawing.Point(90, rowY),
                    Size = new Drawing.Size(330, 28)
                };

                txtUnits[i] = new WinForms.TextBox()
                {
                    Location = new Drawing.Point(450, rowY),
                    Size = new Drawing.Size(90, 28)
                };

                txtDP[i] = new WinForms.TextBox()
                {
                    Location = new Drawing.Point(580, rowY),
                    Size = new Drawing.Size(120, 28)
                };

                grp.Controls.Add(txtDescs[i]);
                grp.Controls.Add(txtUnits[i]);
                grp.Controls.Add(txtDP[i]);

                rowY += 32;
            }

            // --- Botones debajo del GroupBox ---
            int buttonsTop = grp.Bottom + 15;

            btnOK = new WinForms.Button()
            {
                Text = "Aceptar",
                DialogResult = WinForms.DialogResult.OK,
                Location = new Drawing.Point(650, buttonsTop),
                Size = new Drawing.Size(120, 35)
            };

            btnCancel = new WinForms.Button()
            {
                Text = "Cancelar",
                DialogResult = WinForms.DialogResult.Cancel,
                Location = new Drawing.Point(780, buttonsTop),
                Size = new Drawing.Size(120, 35)
            };

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            // Ajustar tamaño del formulario para que quepa todo
            this.ClientSize = new Drawing.Size(1000, buttonsTop + 80);

            this.Controls.Add(lblRutaTitulo);
            this.Controls.Add(lblAreaAtendida);
            this.Controls.Add(lblCaudal);
            this.Controls.Add(lblC36);
            this.Controls.Add(lblC37);
            this.Controls.Add(lblCodos90);
            this.Controls.Add(lblCodos45);

            this.Controls.Add(txtAreaAtendida);
            this.Controls.Add(txtCaudalCfm);
            this.Controls.Add(txtC36);
            this.Controls.Add(txtC37);
            this.Controls.Add(txtCodos90);
            this.Controls.Add(txtCodos45);

            this.Controls.Add(grp);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
        }
    }
}
