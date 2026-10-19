using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    /// Clase encargada de la lógica de colocación de Spot Elevations (Niveles de Elevación)
    /// utilizando la familia 'DC - Elevacion relativa' referenciada al techo/losa.
    /// Mantiene compatibilidad con ejecuciones directas.

    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_Cotas_RedesSecas : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            Autodesk.Revit.DB.View view = doc?.ActiveView;

            if (view == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "No hay una vista activa.");
                return Result.Cancelled;
            }

            OpcionNivelReferencia opcionNivel = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
            if (opcionNivel == null) return Result.Cancelled;

            // Ejecuta directamente el flujo de colocación interactiva por clic
            int colocados = TaguearNivelesPorClic(uidoc, doc, view, opcionNivel);
            if (colocados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Cotas de Nivel", $"Se colocaron {colocados} cotas de nivel ({opcionNivel.Descripcion}).");
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Flujo por selección múltiple para colocar Tags "C.N" en cambios de nivel en redes secas.
        /// </summary>
        public static int TaguearCambioDeNivelPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, string nivelManual = null)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Reference> refs = new List<Reference>();
            var filter = new CodoCambioDeNivelSelectionFilter();
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        refs.Add(new Reference(el));
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (refs.Count == 0)
            {
                try
                {
                    string promptSel = string.IsNullOrWhiteSpace(nivelManual)
                        ? "Selecciona los elementos de cambio de nivel (codos, bandejas o tramos verticales) y haz clic en 'Finish':"
                        : $"Selecciona los elementos de cambio de nivel para '{nivelManual}' y haz clic en 'Finish':";

                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        promptSel
                    );

                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !refs.Any(x => x.ElementId == r.ElementId))
                            {
                                refs.Add(r);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (refs.Count == 0) return 0;

            int colocados = 0;
            int omitidos = 0;
            List<string> motivosOmitidos = new List<string>();

            string nombreTx = string.IsNullOrWhiteSpace(nivelManual)
                ? "Tags C.N por Selección (Redes Secas)"
                : $"Tags C.N ({nivelManual}) por Selección (Redes Secas)";

            using (Transaction tx = new Transaction(doc, nombreTx))
            {
                tx.Start();

                foreach (var r in refs)
                {
                    try
                    {
                        Element el = doc.GetElement(r.ElementId);
                        if (el == null) continue;

                        if (!EsCodoCambioDeNivelValido(el, out string motivo))
                        {
                            omitidos++;
                            if (!string.IsNullOrEmpty(motivo) && !motivosOmitidos.Contains(motivo))
                            {
                                motivosOmitidos.Add(motivo);
                            }
                            continue;
                        }

                        FamilySymbol tagSymbol = ObtenerTagCambioDeNivelParaElemento(doc, el);
                        if (tagSymbol == null)
                        {
                            omitidos++;
                            if (!motivosOmitidos.Contains("No se encontró tipo de tag para la categoría"))
                                motivosOmitidos.Add("No se encontró tipo de tag para la categoría");
                            continue;
                        }

                        Reference refToTag = r;
                        Element elToTag = el;
                        AjustarElementoYReferenciaParaTag(doc, tagSymbol, ref elToTag, ref refToTag);

                        if (!tagSymbol.IsActive)
                        {
                            tagSymbol.Activate();
                            doc.Regenerate();
                        }

                        XYZ puntoEje = ObtenerPuntoEjeDesdeClic(elToTag, r.GlobalPoint) ?? ObtenerPuntoEjeDesdeClic(el, r.GlobalPoint);
                        if (puntoEje == null) continue;

                        XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSymbol.Id,
                            view.Id,
                            refToTag,
                            true,
                            TagOrientation.Horizontal,
                            tagHeadPos
                        );

                        if (tag != null)
                        {
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = tagHeadPos;

                            AsignarParametrosCambioDeNivel(elToTag, nivelManual);
                            if (elToTag.Id != el.Id) AsignarParametrosCambioDeNivel(el, nivelManual);

                            colocados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (omitidos > 0)
            {
                string detalle = string.Join("\n• ", motivosOmitidos);
                Autodesk.Revit.UI.TaskDialog.Show("Tags C.N - Resumen",
                    $"Se colocaron {colocados} tags 'C.N'.\n\nSe omitieron {omitidos} elementos no aplicables:\n• {detalle}");
            }

            return colocados;
        }

        /// <summary>
        /// Flujo interactivo por clic para colocar Tags "C.N" en cambios de nivel (codos, bandejas o tramos con diferencia de altura Z).
        /// </summary>
        public static int TaguearCambioDeNivel(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            int colocados = 0;

            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new CodoCambioDeNivelSelectionFilter(),
                        "Clic en el cambio de nivel (codo, bandeja o tramo vertical - ESC para terminar)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                if (pickRef == null) break;

                Element el = doc.GetElement(pickRef.ElementId);
                if (el == null) continue;

                if (!EsCodoCambioDeNivelValido(el, out string motivoRechazo))
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Tag C.N - Elemento no válido",
                        $"No se puede colocar el Tag C.N en el elemento seleccionado:\n{motivoRechazo}");
                    continue;
                }

                FamilySymbol tagSymbol = ObtenerTagCambioDeNivelParaElemento(doc, el);
                if (tagSymbol == null)
                {
                    // Fallback a SpotElevation si no existe la familia de tag en el proyecto
                    ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
                    return TaguearCambioDeNivelSpotElevationFallback(uidoc, doc, view, spotTypeId);
                }

                Reference refToTag = pickRef;
                Element elToTag = el;
                AjustarElementoYReferenciaParaTag(doc, tagSymbol, ref elToTag, ref refToTag);

                XYZ puntoEje = ObtenerPuntoEjeDesdeClic(elToTag, pickRef.GlobalPoint) ?? ObtenerPuntoEjeDesdeClic(el, pickRef.GlobalPoint);
                XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

                using (Transaction tx = new Transaction(doc, "Colocar Tag C.N"))
                {
                    tx.Start();

                    if (!tagSymbol.IsActive)
                    {
                        tagSymbol.Activate();
                        doc.Regenerate();
                    }

                    IndependentTag tag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        refToTag,
                        true,
                        TagOrientation.Horizontal,
                        tagHeadPos
                    );

                    if (tag != null)
                    {
                        tag.HasLeader = true;
                        tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        tag.TagHeadPosition = tagHeadPos;

                        AsignarParametrosCambioDeNivel(elToTag);
                        if (elToTag.Id != el.Id) AsignarParametrosCambioDeNivel(el);

                        colocados++;
                    }

                    tx.Commit();
                }
            }

            return colocados;
        }

        /// <summary>
        /// Flujo automático para taguear TODOS los cambios de nivel válidos en la vista activa (conduits, bandejas, tuberías, etc.).
        /// </summary>
        public static int TaguearCambioDeNivelTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            var categorias = new[]
            {
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctCurves
            };

            List<Element> candidatos = new List<Element>();
            foreach (var cat in categorias)
            {
                var elems = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();
                candidatos.AddRange(elems);
            }

            if (candidatos.Count == 0) return 0;

            // 1. Filtrar solo elementos válidos de cambio de nivel (descartando giros puramente horizontales y tomas)
            List<Element> elementosValidos = candidatos
                .Where(el => EsCodoCambioDeNivelValido(el, out _))
                .ToList();

            if (elementosValidos.Count == 0) return 0;

            // 2. Identificar qué elementos ya tienen específicamente un Tag C.N asignado en la vista
            var tagsExistentes = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            HashSet<ElementId> elementosYaTagueados = new HashSet<ElementId>();
            foreach (var t in tagsExistentes)
            {
                try
                {
                    ElementId tagTypeId = t.GetTypeId();
                    string tagFamName = (doc.GetElement(tagTypeId) as ElementType)?.FamilyName?.ToLower() ?? "";
                    string tagSymName = (doc.GetElement(tagTypeId) as ElementType)?.Name?.ToLower() ?? "";
                    string tagText = "";
                    try { tagText = t.TagText?.ToLower() ?? ""; } catch { }

                    bool esTagCN = tagFamName.Contains("cambio de nivel") ||
                                   tagFamName.Contains("c.n") ||
                                   tagSymName.Contains("cambio de nivel") ||
                                   tagSymName.Contains("c.n") ||
                                   tagText.Contains("c.n") ||
                                   tagText.Contains("cambio");

                    if (!esTagCN) continue;

                    dynamic dyn = t;
                    try
                    {
                        ElementId locId = dyn.GetTaggedLocalElementId();
                        if (locId != null && locId != ElementId.InvalidElementId)
                        {
                            elementosYaTagueados.Add(locId);
                        }
                    }
                    catch
                    {
                        var refs = dyn.GetTaggedElementIds();
                        if (refs != null)
                        {
                            foreach (dynamic r in refs)
                            {
                                ElementId hostId = r.HostElementId;
                                if (hostId != null && hostId != ElementId.InvalidElementId)
                                {
                                    elementosYaTagueados.Add(hostId);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Agrupar codos/elementos por cada cambio de nivel individual (pares o transiciones a <= 25.0 ft)
            List<List<Element>> gruposCambios = AgruparCodosEnCambiosDeNivel(elementosValidos);
            List<Element> elementosATaguear = new List<Element>();

            foreach (var grupo in gruposCambios)
            {
                if (grupo.Any(c => elementosYaTagueados.Contains(c.Id)))
                {
                    continue;
                }

                Element elSeleccionado = grupo
                    .OrderBy(c =>
                    {
                        XYZ pt = (c.Location as LocationPoint)?.Point ??
                                 (c.Location is LocationCurve lc ? lc.Curve.Evaluate(0.5, true) : XYZ.Zero);
                        return pt.X * 1000.0 + pt.Y * 10.0 + pt.Z;
                    })
                    .FirstOrDefault();

                if (elSeleccionado != null)
                {
                    elementosATaguear.Add(elSeleccionado);
                }
            }

            if (elementosATaguear.Count == 0) return 0;

            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Tags C.N Automático"))
            {
                tx.Start();

                foreach (var el in elementosATaguear)
                {
                    try
                    {
                        FamilySymbol tagSymbol = ObtenerTagCambioDeNivelParaElemento(doc, el);
                        if (tagSymbol == null) continue;

                        if (!tagSymbol.IsActive)
                        {
                            tagSymbol.Activate();
                            doc.Regenerate();
                        }

                        Element elToTag = el;
                        Reference refToTag = new Reference(el);
                        AjustarElementoYReferenciaParaTag(doc, tagSymbol, ref elToTag, ref refToTag);

                        XYZ puntoEje = ObtenerPuntoCentro(elToTag, out _) ??
                                       ObtenerPuntoCentro(el, out _) ??
                                       (elToTag.Location as LocationPoint)?.Point ??
                                       (el.Location as LocationPoint)?.Point ??
                                       (elToTag.Location is LocationCurve lcTo ? lcTo.Curve.Evaluate(0.5, true) : null) ??
                                       (el.Location is LocationCurve lc ? lc.Curve.Evaluate(0.5, true) : null);
                        if (puntoEje == null) continue;

                        XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSymbol.Id,
                            view.Id,
                            refToTag,
                            true,
                            TagOrientation.Horizontal,
                            tagHeadPos
                        );

                        if (tag != null)
                        {
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = tagHeadPos;

                            AsignarParametrosCambioDeNivel(elToTag);
                            if (elToTag.Id != el.Id) AsignarParametrosCambioDeNivel(el);

                            creados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return creados;
        }

        /// <summary>
        /// Agrupa los codos que pertenecen al MISMO cambio de nivel (unidos directamente o por un tramo de transición <= 25.0 pies).
        /// Cada grupo representa un único cambio de nivel (bayoneta / salto).
        /// </summary>
        private static List<List<Element>> AgruparCodosEnCambiosDeNivel(List<Element> codosValidos)
        {
            List<List<Element>> grupos = new List<List<Element>>();
            HashSet<ElementId> visitados = new HashSet<ElementId>();
            HashSet<ElementId> idsValidos = new HashSet<ElementId>(codosValidos.Select(c => c.Id));

            foreach (var codo in codosValidos)
            {
                if (visitados.Contains(codo.Id)) continue;

                List<Element> grupoActual = new List<Element>();
                Queue<Element> cola = new Queue<Element>();

                cola.Enqueue(codo);
                visitados.Add(codo.Id);

                while (cola.Count > 0)
                {
                    Element actual = cola.Dequeue();
                    grupoActual.Add(actual);

                    XYZ pActual = ObtenerPuntoCentro(actual, out _) ?? (actual.Location as LocationPoint)?.Point;

                    ConnectorSet conectores = null;
                    if (actual is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                    {
                        conectores = fi.MEPModel.ConnectorManager.Connectors;
                    }

                    if (conectores != null)
                    {
                        foreach (Connector conn in conectores)
                        {
                            foreach (Connector refConn in conn.AllRefs)
                            {
                                Element owner = refConn.Owner;
                                if (owner == null || owner.Id == actual.Id) continue;

                                // Caso 1: Conexión directa a otro codo válido del mismo cambio de nivel
                                if (idsValidos.Contains(owner.Id) && !visitados.Contains(owner.Id))
                                {
                                    XYZ pOwner = ObtenerPuntoCentro(owner, out _) ?? (owner.Location as LocationPoint)?.Point;
                                    if (pActual == null || pOwner == null || pActual.DistanceTo(pOwner) <= 25.0)
                                    {
                                        visitados.Add(owner.Id);
                                        cola.Enqueue(owner);
                                    }
                                }
                                // Caso 2: Conexión a través de un tramo de tubería/conduit inclinado o vertical (longitud <= 25.0 pies)
                                else if (owner.Location is LocationCurve lc && lc.Curve != null && lc.Curve.Length <= 25.0)
                                {
                                    if (owner is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
                                    {
                                        foreach (Connector pipeConn in mepCurve.ConnectorManager.Connectors)
                                        {
                                            foreach (Connector refPipeConn in pipeConn.AllRefs)
                                            {
                                                Element vecino = refPipeConn.Owner;
                                                if (vecino != null && vecino.Id != actual.Id && idsValidos.Contains(vecino.Id) && !visitados.Contains(vecino.Id))
                                                {
                                                    XYZ pVecino = ObtenerPuntoCentro(vecino, out _) ?? (vecino.Location as LocationPoint)?.Point;
                                                    if (pActual == null || pVecino == null || pActual.DistanceTo(pVecino) <= 25.0)
                                                    {
                                                        visitados.Add(vecino.Id);
                                                        cola.Enqueue(vecino);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                grupos.Add(grupoActual);
            }

            return grupos;
        }

        private static int TaguearCambioDeNivelSpotElevationFallback(
            UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, ElementId spotTypeId)
        {
            int colocados = 0;
            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new CodoCambioDeNivelSelectionFilter(),
                        "Clic en codo de cambio de nivel (ESC para terminar)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                if (pickRef == null) break;

                Element el = doc.GetElement(pickRef.ElementId);
                if (el == null) continue;

                if (!EsCodoCambioDeNivelValido(el, out string motivoRechazo))
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Tag C.N - Elemento no válido",
                        $"No se puede colocar el Tag C.N:\n{motivoRechazo}");
                    continue;
                }

                XYZ puntoEje = ObtenerPuntoEjeDesdeClic(el, pickRef.GlobalPoint);
                Level nivelTecho = ObtenerNivelTecho(doc, view, el, puntoEje);

                XYZ puntoUbicacion = null;
                try
                {
                    puntoUbicacion = uidoc.Selection.PickPoint(
                        ObjectSnapTypes.None,
                        "Clic para ubicar la directriz de Tag C.N (o ESC para ubicación automática)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    puntoUbicacion = null;
                }

                using (Transaction tx = new Transaction(doc, "Colocar Tags C.N"))
                {
                    tx.Start();

                    SpotDimension spot = ColocarSpotElevation(
                        doc, view, el, pickRef, puntoEje, puntoUbicacion, spotTypeId, nivelTecho
                    );

                    if (spot != null)
                    {
                        colocados++;
                    }

                    tx.Commit();
                }
            }

            return colocados;
        }

        /// <summary>
        /// Determina si un elemento es una toma o tomacorriente (receptáculo/enchufe).
        /// </summary>
        public static bool EsTomaOTomacorriente(Element e)
        {
            if (e == null || e.Category == null) return false;

            int catId = e.Category.Id.IntegerValue;
            if (catId == (int)BuiltInCategory.OST_ElectricalFixtures)
            {
                string famName = (e as FamilyInstance)?.Symbol?.Family?.Name?.ToLower() ?? "";
                string typeName = (e as FamilyInstance)?.Symbol?.Name?.ToLower() ?? "";
                string elemName = e.Name?.ToLower() ?? "";
                string comb = famName + " " + typeName + " " + elemName;

                return comb.Contains("toma") ||
                       comb.Contains("tomacorriente") ||
                       comb.Contains("receptacle") ||
                       comb.Contains("outlet") ||
                       comb.Contains("gfci") ||
                       comb.Contains("enchufe") ||
                       comb.Contains("salida");
            }

            if (catId == (int)BuiltInCategory.OST_LightingDevices)
            {
                return true; // Interruptor
            }

            return false;
        }

        public static bool EsCajaODispositivoElemento(Element e)
        {
            return EsTomaOTomacorriente(e);
        }

        /// <summary>
        /// Verifica si el elemento (codo o extremo de conduit de bajada) está físicamente pegado
        /// a una toma o tomacorriente (radio menor a 0.8 pies / ~25 cm).
        /// </summary>
        public static bool EstaProximoATomaOTomacorriente(Element elem)
        {
            if (elem == null || elem.Document == null) return false;
            Document doc = elem.Document;

            XYZ ptCheck = null;
            if (elem.Location is LocationPoint lp)
            {
                ptCheck = lp.Point;
            }
            else if (elem.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                // Probar el punto inferior del conduit (bajada a toma)
                ptCheck = p0.Z < p1.Z ? p0 : p1;
            }

            if (ptCheck == null) return false;

            double radioXY = 0.8; // ~25 cm en planta
            double rangoZ = 0.8;  // ~25 cm en altura

            try
            {
                Outline outline = new Outline(
                    new XYZ(ptCheck.X - radioXY, ptCheck.Y - radioXY, ptCheck.Z - rangoZ),
                    new XYZ(ptCheck.X + radioXY, ptCheck.Y + radioXY, ptCheck.Z + rangoZ)
                );

                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                var tomasCercanas = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                    .WherePasses(bbFilter)
                    .WhereElementIsNotElementType()
                    .ToElements();

                if (tomasCercanas.Any(t => t.Id != elem.Id && EsTomaOTomacorriente(t)))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Verifica si el accesorio o tramo conecta directamente a una toma o tomacorriente.
        /// </summary>
        public static bool EstaConectadoACajaODispositivo(Element elem)
        {
            if (elem == null) return false;
            if (EsTomaOTomacorriente(elem)) return true;
            if (EstaProximoATomaOTomacorriente(elem)) return true;

            if (elem is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner == null || owner.Id == elem.Id) continue;
                        if (EsTomaOTomacorriente(owner) || EstaProximoATomaOTomacorriente(owner))
                        {
                            return true;
                        }
                    }
                }
            }
            else if (elem is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
            {
                foreach (Connector conn in mepCurve.ConnectorManager.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner != null && owner.Id != elem.Id)
                        {
                            if (EsTomaOTomacorriente(owner) || EstaProximoATomaOTomacorriente(owner))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Valida si el elemento seleccionado es un accesorio MEP (Fitting / Codo) o un tramo vertical/inclinado (Bandeja, Conduit, Tubería) que representa un cambio de nivel real.
        /// Descarta:
        /// 1. Tomas y tomacorrientes (receptáculos).
        /// 2. Conexiones inmediatas tocando una toma.
        /// 3. Giros puramente horizontales en planta sin desnivel vertical.
        /// Admite:
        /// - Codos de 15°, 30°, 45°, 60° y 90° que cambien de nivel Z o pasen de horizontal a vertical/inclinado.
        /// - Bayonetas, quiebres y cambios de nivel en buitrones/shafts y plantas.
        /// - Tramos inclinados o verticales con desnivel Z >= 0.15 ft (~4.5 cm).
        /// </summary>
        public static bool EsCodoCambioDeNivelValido(Element el, out string motivoRechazo)
        {
            motivoRechazo = string.Empty;
            if (el == null)
            {
                motivoRechazo = "Elemento nulo";
                return false;
            }

            if (el.Category == null)
            {
                motivoRechazo = "El elemento no tiene categoría válida.";
                return false;
            }

            if (EsTomaOTomacorriente(el))
            {
                motivoRechazo = "Las tomas y tomacorrientes no se taguean como cambio de nivel.";
                return false;
            }

            if (EstaConectadoACajaODispositivo(el))
            {
                motivoRechazo = "Las conexiones inmediatas a tomas no se taguean como cambio de nivel.";
                return false;
            }

            int catId = el.Category.Id.IntegerValue;

            // =========================================================================
            // CASO A: TRAMOS DE BANDEJAS, CONDUITS, TUBERÍAS O DUCTOS (MEPCurve)
            // =========================================================================
            if (catId == (int)BuiltInCategory.OST_CableTray ||
                catId == (int)BuiltInCategory.OST_Conduit ||
                catId == (int)BuiltInCategory.OST_PipeCurves ||
                catId == (int)BuiltInCategory.OST_DuctCurves ||
                el is MEPCurve)
            {
                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    double dz = Math.Abs(p1.Z - p0.Z);
                    if (dz >= 0.15)
                    {
                        return true;
                    }
                }

                double zInicio = ObtenerParametroElevacionDoble(el, BuiltInParameter.RBS_START_OFFSET_PARAM, "Start Offset", "Elevación Inicial");
                double zFin = ObtenerParametroElevacionDoble(el, BuiltInParameter.RBS_END_OFFSET_PARAM, "End Offset", "Elevación Final");
                if (!double.IsNaN(zInicio) && !double.IsNaN(zFin) && Math.Abs(zFin - zInicio) >= 0.15)
                {
                    return true;
                }

                double zUpper = ObtenerParametroElevacionDoble(el, "Upper End Centerline Elevation", "Upper End Top Elevation", "Upper End Elevation", "Elevación Superior");
                double zLower = ObtenerParametroElevacionDoble(el, "Lower End Centerline Elevation", "Lower End Bottom Elevation", "Lower End Elevation", "Elevación Inferior");
                if (!double.IsNaN(zUpper) && !double.IsNaN(zLower) && Math.Abs(zUpper - zLower) >= 0.15)
                {
                    return true;
                }

                motivoRechazo = "El tramo seleccionado es horizontal en planta y no presenta cambio de nivel en elevación Z.";
                return false;
            }

            // =========================================================================
            // CASO B: ACCESORIOS MEP (FITTINGS / CODOS / TRANSICIONES)
            // =========================================================================
            bool esFitting = catId == (int)BuiltInCategory.OST_ConduitFitting ||
                             catId == (int)BuiltInCategory.OST_PipeFitting ||
                             catId == (int)BuiltInCategory.OST_CableTrayFitting ||
                             catId == (int)BuiltInCategory.OST_DuctFitting;

            if (!esFitting || !(el is FamilyInstance fiElem))
            {
                motivoRechazo = "Debes seleccionar un accesorio (Fitting) o un tramo vertical/inclinado de cambio de nivel.";
                return false;
            }

            if (EsGiroHorizontalSinDesnivel(fiElem))
            {
                motivoRechazo = "No aplica: Es un giro horizontal en planta sin desnivel vertical (ambos extremos al mismo nivel).";
                return false;
            }

            if (TieneDesnivelVertical(fiElem))
            {
                return true;
            }

            motivoRechazo = "No presenta cambio de nivel ni desnivel vertical en Z (giro horizontal en planta o pendiente mínima).";
            return false;
        }

        /// <summary>
        /// Determina si un codo es un giro puramente horizontal en planta (sin ningún cambio de elevación Z).
        /// </summary>
        public static bool EsGiroHorizontalSinDesnivel(FamilyInstance fi)
        {
            if (fi?.MEPModel?.ConnectorManager == null) return false;
            var conns = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
            if (conns.Count < 2) return false;

            // Si algún conector tiene dirección con inclinación vertical (|BasisZ.Z| >= 0.15),
            // el codo gira subiendo o bajando en Z -> NO es puramente horizontal
            foreach (var c in conns)
            {
                if (c.CoordinateSystem != null && Math.Abs(c.CoordinateSystem.BasisZ.Z) >= 0.15)
                {
                    return false;
                }
            }

            // Si hay diferencia entre los puntos de conexión del accesorio >= 0.15 ft (~4.5 cm)
            double minZ = conns.Min(c => c.Origin.Z);
            double maxZ = conns.Max(c => c.Origin.Z);
            if (maxZ - minZ >= 0.15)
            {
                return false;
            }

            // Verificar si alguno de los tramos conectados tiene desnivel vertical Z
            foreach (var c in conns)
            {
                if (!c.IsConnected) continue;
                foreach (Connector refC in c.AllRefs)
                {
                    if (refC.Owner is MEPCurve curve && curve.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        if (Math.Abs(p1.Z - p0.Z) >= 0.15)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determina si un accesorio tiene desnivel vertical propio o conecta directamente con un tramo inclinado o vertical.
        /// </summary>
        public static bool TieneDesnivelVertical(FamilyInstance fi)
        {
            if (fi?.MEPModel?.ConnectorManager == null) return false;
            var conns = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
            if (conns.Count == 0) return false;

            // 1. Inclinación del conector hacia arriba o abajo
            foreach (var c in conns)
            {
                if (c.CoordinateSystem != null && Math.Abs(c.CoordinateSystem.BasisZ.Z) >= 0.15)
                {
                    return true;
                }
            }

            // 2. Diferencia entre orígenes de conectores del fitting
            double minZ = conns.Min(c => c.Origin.Z);
            double maxZ = conns.Max(c => c.Origin.Z);
            if (maxZ - minZ >= 0.15)
            {
                return true;
            }

            // 3. Revisar tramos conectados (conduit / tubería) con pendiente o verticales
            foreach (var c in conns)
            {
                if (!c.IsConnected) continue;
                foreach (Connector refC in c.AllRefs)
                {
                    if (refC.Owner != null && refC.Owner.Id != fi.Id)
                    {
                        if (refC.Owner is MEPCurve curve && curve.Location is LocationCurve lc && lc.Curve != null)
                        {
                            XYZ p0 = lc.Curve.GetEndPoint(0);
                            XYZ p1 = lc.Curve.GetEndPoint(1);
                            if (Math.Abs(p1.Z - p0.Z) >= 0.15)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Ajusta el elemento y la referencia para garantizar compatibilidad con la categoría de la etiqueta (Tag).
        /// Si el tag es para curvas (conduit/tubo) pero el elemento es un codo/accesorio, selecciona la curva conectada.
        /// Si el tag es para accesorios pero el elemento es una curva, selecciona el accesorio conectado.
        /// </summary>
        public static void AjustarElementoYReferenciaParaTag(
            Document doc, FamilySymbol tagSymbol, ref Element elToTag, ref Reference refToTag)
        {
            if (doc == null || tagSymbol?.Category == null || elToTag?.Category == null) return;

            int tagCatId = tagSymbol.Category.Id.IntegerValue;
            int elCatId = elToTag.Category.Id.IntegerValue;

            if (tagCatId == (int)BuiltInCategory.OST_MultiCategoryTags) return;

            bool tagEsCurva = tagCatId == (int)BuiltInCategory.OST_ConduitTags ||
                              tagCatId == (int)BuiltInCategory.OST_PipeTags ||
                              tagCatId == (int)BuiltInCategory.OST_CableTrayTags ||
                              tagCatId == (int)BuiltInCategory.OST_DuctTags;

            bool elEsFitting = elCatId == (int)BuiltInCategory.OST_ConduitFitting ||
                               elCatId == (int)BuiltInCategory.OST_PipeFitting ||
                               elCatId == (int)BuiltInCategory.OST_CableTrayFitting ||
                               elCatId == (int)BuiltInCategory.OST_DuctFitting;

            if (tagEsCurva && elEsFitting)
            {
                Element connectedCurve = BuscarCurvaConectada(elToTag);
                if (connectedCurve != null)
                {
                    elToTag = connectedCurve;
                    refToTag = new Reference(connectedCurve);
                    return;
                }
            }

            bool tagEsFitting = tagCatId == (int)BuiltInCategory.OST_ConduitFittingTags ||
                                tagCatId == (int)BuiltInCategory.OST_PipeFittingTags ||
                                tagCatId == (int)BuiltInCategory.OST_CableTrayFittingTags ||
                                tagCatId == (int)BuiltInCategory.OST_DuctFittingTags;

            bool elEsCurva = elCatId == (int)BuiltInCategory.OST_Conduit ||
                             elCatId == (int)BuiltInCategory.OST_PipeCurves ||
                             elCatId == (int)BuiltInCategory.OST_CableTray ||
                             elCatId == (int)BuiltInCategory.OST_DuctCurves;

            if (tagEsFitting && elEsCurva)
            {
                Element connectedFitting = BuscarFittingConectado(elToTag);
                if (connectedFitting != null)
                {
                    elToTag = connectedFitting;
                    refToTag = new Reference(connectedFitting);
                    return;
                }
            }
        }

        private static Element BuscarCurvaConectada(Element fitting)
        {
            if (fitting is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
            {
                Element bestCurve = null;
                double maxDeltaZ = -1;

                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner == null || owner.Id == fitting.Id) continue;
                        if (owner is MEPCurve mepCurve)
                        {
                            double dz = 0;
                            if (mepCurve.Location is LocationCurve lc && lc.Curve != null)
                            {
                                dz = Math.Abs(lc.Curve.GetEndPoint(1).Z - lc.Curve.GetEndPoint(0).Z);
                            }
                            if (dz > maxDeltaZ)
                            {
                                maxDeltaZ = dz;
                                bestCurve = mepCurve;
                            }
                        }
                    }
                }
                return bestCurve;
            }
            return null;
        }

        private static Element BuscarFittingConectado(Element curve)
        {
            if (curve is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
            {
                foreach (Connector conn in mepCurve.ConnectorManager.Connectors)
                {
                    if (conn == null || !conn.IsConnected) continue;
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner == null || owner.Id == curve.Id) continue;
                        if (owner is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                        {
                            return fi;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Busca el tipo de Tag de accesorio de conduit ("DC- Tag accesorio conduit_URB", "DC. TAG conduit accesorio", "DC tag conduit accesorio")
        /// correspondiente a Cambio de Nivel / C.N.
        /// </summary>
        public static FamilySymbol ObtenerTagConduitAccesorio(Document doc)
        {
            try
            {
                var tagSymbols = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                // 1. Buscar coincidencia por nombre de familia de tag de accesorio conduit
                var coincidenciaFamilia = tagSymbols.FirstOrDefault(s =>
                    (s.Family?.Name?.IndexOf("DC- Tag accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("DC. TAG conduit accesorio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("DC tag conduit accesorio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("Tag accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (s.Name.IndexOf("Cambio de nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Name.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0));

                if (coincidenciaFamilia != null) return coincidenciaFamilia;

                // 2. Coincidencia por nombre de símbolo "Cambio de nivel" o "C.N"
                var coincidenciaNombre = tagSymbols.FirstOrDefault(s =>
                    s.Category != null &&
                    (s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags ||
                     s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags ||
                     s.Category.Name.ToLower().Contains("tag")) &&
                    (s.Name.IndexOf("Cambio de nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Name.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0));

                if (coincidenciaNombre != null) return coincidenciaNombre;

                // 3. Fallback a cualquier tag de ConduitFitting o Conduit
                var fallbackConduitTag = tagSymbols.FirstOrDefault(s =>
                    s.Category != null &&
                    (s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags ||
                     s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags));

                return fallbackConduitTag ?? tagSymbols.FirstOrDefault(s => s.Category != null && s.Category.Name.ToLower().Contains("tag"));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Obtiene el símbolo de etiqueta correspondiente para un cambio de nivel (C.N) según la categoría del elemento
        /// (Bandejas, Conduits, Tuberías, Ductos o Accesorios).
        /// </summary>
        public static FamilySymbol ObtenerTagCambioDeNivelParaElemento(Document doc, Element el)
        {
            if (doc == null || el == null) return null;
            try
            {
                int catId = el.Category?.Id.IntegerValue ?? 0;

                BuiltInCategory targetTagCategory = BuiltInCategory.OST_ConduitFittingTags;
                BuiltInCategory secondaryTagCategory = BuiltInCategory.OST_ConduitTags;

                if (catId == (int)BuiltInCategory.OST_CableTray)
                {
                    targetTagCategory = BuiltInCategory.OST_CableTrayTags;
                    secondaryTagCategory = BuiltInCategory.OST_CableTrayFittingTags;
                }
                else if (catId == (int)BuiltInCategory.OST_CableTrayFitting)
                {
                    targetTagCategory = BuiltInCategory.OST_CableTrayFittingTags;
                    secondaryTagCategory = BuiltInCategory.OST_CableTrayTags;
                }
                else if (catId == (int)BuiltInCategory.OST_Conduit)
                {
                    targetTagCategory = BuiltInCategory.OST_ConduitTags;
                    secondaryTagCategory = BuiltInCategory.OST_ConduitFittingTags;
                }
                else if (catId == (int)BuiltInCategory.OST_ConduitFitting)
                {
                    targetTagCategory = BuiltInCategory.OST_ConduitFittingTags;
                    secondaryTagCategory = BuiltInCategory.OST_ConduitTags;
                }
                else if (catId == (int)BuiltInCategory.OST_PipeCurves)
                {
                    targetTagCategory = BuiltInCategory.OST_PipeTags;
                    secondaryTagCategory = BuiltInCategory.OST_PipeFittingTags;
                }
                else if (catId == (int)BuiltInCategory.OST_PipeFitting)
                {
                    targetTagCategory = BuiltInCategory.OST_PipeFittingTags;
                    secondaryTagCategory = BuiltInCategory.OST_PipeTags;
                }
                else if (catId == (int)BuiltInCategory.OST_DuctCurves)
                {
                    targetTagCategory = BuiltInCategory.OST_DuctTags;
                    secondaryTagCategory = BuiltInCategory.OST_DuctFittingTags;
                }
                else if (catId == (int)BuiltInCategory.OST_DuctFitting)
                {
                    targetTagCategory = BuiltInCategory.OST_DuctFittingTags;
                    secondaryTagCategory = BuiltInCategory.OST_DuctTags;
                }

                var tagSymbols = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Category != null &&
                                (s.Category.Id.IntegerValue == (int)targetTagCategory ||
                                 (secondaryTagCategory != BuiltInCategory.INVALID && s.Category.Id.IntegerValue == (int)secondaryTagCategory) ||
                                 s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                    .ToList();

                if (tagSymbols.Count > 0)
                {
                    // 1. Prioridad: Símbolo o Familia que contenga "Cambio de nivel" o "C.N"
                    var coincidenciaCN = tagSymbols.FirstOrDefault(s =>
                    {
                        string fam = s.Family?.Name ?? "";
                        string nom = s.Name ?? "";
                        string comb = (fam + " " + nom).ToLower();
                        return comb.Contains("cambio de nivel") || comb.Contains("c.n") || comb.Contains("cambio");
                    });
                    if (coincidenciaCN != null) return coincidenciaCN;

                    // 2. Prioridad: Familia con prefijo "DC"
                    var coincidenciaDC = tagSymbols.FirstOrDefault(s =>
                    {
                        string fam = s.Family?.Name ?? "";
                        string nom = s.Name ?? "";
                        return fam.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               nom.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (coincidenciaDC != null) return coincidenciaDC;

                    // 3. Fallback: Primer tag disponible de la categoría objetivo
                    var primerTarget = tagSymbols.FirstOrDefault(s => s.Category.Id.IntegerValue == (int)targetTagCategory);
                    if (primerTarget != null) return primerTarget;

                    return tagSymbols.First();
                }
            }
            catch { }

            return ObtenerTagConduitAccesorio(doc);
        }

        /// <summary>
        /// Asigna los parámetros de cambio de nivel ("Cambio de nivel") en el elemento si están disponibles en su tipo o ejemplar.
        /// Si se indica un nivel manual, concatena o asigna el nombre del nivel para que el tag muestre el nivel correspondiente.
        /// </summary>
        private static void AsignarParametrosCambioDeNivel(Element el, string nivelManual = null)
        {
            if (el == null) return;
            try
            {
                string valorAsignar = string.IsNullOrWhiteSpace(nivelManual)
                    ? "Cambio de nivel"
                    : "Cambio de nivel " + nivelManual;

                var nombresParam = new[]
                {
                    "DC. TAG conduit accesorio",
                    "Cambio de Nivel",
                    "cambio de nivel",
                    "C.N",
                    "Cambio de Nivel Comunicaciones",
                    "Cambio de Nivel CCTV",
                    "Cambio de Nivel Control",
                    "Cambio de Nivel Automatización",
                    "Cambio de Nivel Detección",
                    "Cambio de Nivel Iluminación",
                    "Cambio de Nivel Eléctrico"
                };

                foreach (var nom in nombresParam)
                {
                    Parameter p = el.LookupParameter(nom);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        p.Set(valorAsignar);
                    }
                }

                foreach (Parameter p in el.Parameters)
                {
                    if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
                    string pName = p.Definition?.Name ?? "";
                    if (pName.IndexOf("Cambio de Nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pName.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { p.Set(valorAsignar); } catch { }
                    }
                }

                // Si se especificó un nivel manual, guardar también en Comments por si el tag lee comentarios
                if (!string.IsNullOrWhiteSpace(nivelManual))
                {
                    try
                    {
                        Parameter pCom = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                      ?? el.LookupParameter("Comments")
                                      ?? el.LookupParameter("Comentarios");
                        if (pCom != null && !pCom.IsReadOnly && pCom.StorageType == StorageType.String)
                        {
                            pCom.Set(valorAsignar);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Obtiene un valor de elevación en double desde un BuiltInParameter o una lista de nombres alternativos.
        /// </summary>
        private static double ObtenerParametroElevacionDoble(Element el, BuiltInParameter bip, params string[] paramNames)
        {
            try
            {
                Parameter p = el.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    return p.AsDouble();
                }
                foreach (string nom in paramNames)
                {
                    Parameter pNom = el.LookupParameter(nom);
                    if (pNom != null && pNom.HasValue && pNom.StorageType == StorageType.Double)
                    {
                        return pNom.AsDouble();
                    }
                }
            }
            catch { }
            return double.NaN;
        }

        private static double ObtenerParametroElevacionDoble(Element el, params string[] paramNames)
        {
            if (el == null) return double.NaN;
            try
            {
                foreach (string nom in paramNames)
                {
                    Parameter pNom = el.LookupParameter(nom);
                    if (pNom != null && pNom.HasValue && pNom.StorageType == StorageType.Double)
                    {
                        return pNom.AsDouble();
                    }
                }
            }
            catch { }
            return double.NaN;
        }

        private class CodoCambioDeNivelSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                if (EsTomaOTomacorriente(e)) return false;

                int catId = e.Category.Id.IntegerValue;
                bool esValido = catId == (int)BuiltInCategory.OST_ConduitFitting ||
                                catId == (int)BuiltInCategory.OST_PipeFitting ||
                                catId == (int)BuiltInCategory.OST_CableTrayFitting ||
                                catId == (int)BuiltInCategory.OST_DuctFitting ||
                                catId == (int)BuiltInCategory.OST_CableTray ||
                                catId == (int)BuiltInCategory.OST_Conduit ||
                                catId == (int)BuiltInCategory.OST_PipeCurves ||
                                catId == (int)BuiltInCategory.OST_DuctCurves;

                if (!esValido) return false;

                if (EstaConectadoACajaODispositivo(e)) return false;

                return true;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        /// <summary>
        /// Filtro de selección para elementos acotables en Cotas Alineadas:
        /// Tramos MEP (Conduits, Tuberías, Bandejas, Ductos), Cajas y Tomas (ElectricalFixtures, etc.), Muros, Grids/Ejes.
        /// </summary>
        public class CotasAlineadasSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_Conduit ||
                       catId == (int)BuiltInCategory.OST_ConduitFitting ||
                       catId == (int)BuiltInCategory.OST_ElectricalFixtures ||
                       catId == (int)BuiltInCategory.OST_LightingDevices ||
                       catId == (int)BuiltInCategory.OST_LightingFixtures ||
                       catId == (int)BuiltInCategory.OST_ElectricalEquipment ||
                       catId == (int)BuiltInCategory.OST_CommunicationDevices ||
                       catId == (int)BuiltInCategory.OST_DataDevices ||
                       catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_PipeFitting ||
                       catId == (int)BuiltInCategory.OST_CableTray ||
                       catId == (int)BuiltInCategory.OST_DuctCurves ||
                       catId == (int)BuiltInCategory.OST_Walls ||
                       catId == (int)BuiltInCategory.OST_Grids ||
                       catId == (int)BuiltInCategory.OST_StructuralColumns ||
                       catId == (int)BuiltInCategory.OST_StructuralFraming;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        /// <summary>
        /// Flujo por selección múltiple para colocar Cotas Alineadas:
        /// 1. Cotas de retranqueo/distancia de cada elemento a la cara del muro (e.g. 0.79, 0.79, 0.89).
        /// 2. Cota en cadena continua entre los ejes de los elementos a lo largo del muro (e.g. 1.63, 1.16).
        /// Soporta preselección, selección por recuadro y clics continuos finalizando con ESC.
        /// </summary>
        public static int TaguearCotasAlineadasPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> elementos = new List<Element>();
            var filter = new CotasAlineadasSelectionFilter();

            // 1. Preselección
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element e = doc.GetElement(id);
                    if (e != null && filter.AllowElement(e))
                    {
                        elementos.Add(e);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (elementos.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tomas/conduits y muros a acotar y haz clic en 'Finish':"
                    );

                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element e = doc.GetElement(r.ElementId);
                            if (e != null && !elementos.Any(x => x.Id == e.Id))
                            {
                                elementos.Add(e);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (elementos.Count == 0) return 0;

            return ProcesarCreacionCotasAlineadas(doc, view, elementos);
        }

        /// <summary>
        /// Flujo automático que escanea la vista activa y coloca Cotas Alineadas (cadena intereje y distancia a muro).
        /// </summary>
        public static int TaguearCotasAlineadasTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (doc == null || view == null) return 0;

            var categorias = new[]
            {
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_DuctCurves
            };

            List<Element> mepElems = new List<Element>();
            foreach (var cat in categorias)
            {
                var elems = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();
                mepElems.AddRange(elems);
            }

            if (mepElems.Count == 0) return 0;

            return ProcesarCreacionCotasAlineadas(doc, view, mepElems);
        }

        /// <summary>
        /// Método de compatibilidad para NivelDeUbicacion que delega a Cotas Alineadas.
        /// </summary>
        public static int TaguearNivelDeUbicacion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            return TaguearCotasAlineadasPorSeleccion(uidoc, doc, view);
        }

        private class ElementoAcotableInfo
        {
            public Element Elemento;
            public XYZ Centro2D;
            public XYZ PuntoExtremo;
            public XYZ DirEje2D;
            public Reference RefGeom;
        }

        /// <summary>
        /// Procesa la creación de cotas alineadas:
        /// 1. Cotas de retranqueo/distancia de cada elemento a la cara del muro (perpendiculares).
        /// 2. Cadena horizontal continua entre los ejes de los elementos a lo largo del muro (intereje).
        /// </summary>
        private static int ProcesarCreacionCotasAlineadas(Document doc, Autodesk.Revit.DB.View view, List<Element> elementos)
        {
            if (doc == null || view == null || elementos == null || elementos.Count == 0) return 0;

            DimensionType dimType = ObtenerOCrearTipoCotaAlineada(doc);
            if (dimType == null) return 0;

            // 1. Obtener todos los muros visibles en la vista o seleccionados
            var murosEnVista = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();

            // 2. Extraer información geométrica y referencias de los elementos MEP / dispositivos seleccionados
            List<ElementoAcotableInfo> itemsMep = new List<ElementoAcotableInfo>();
            List<Wall> murosSeleccionados = new List<Wall>();
            List<Grid> gridsSeleccionados = new List<Grid>();

            foreach (var el in elementos)
            {
                if (el is Wall w)
                {
                    murosSeleccionados.Add(w);
                    continue;
                }
                if (el is Grid g)
                {
                    gridsSeleccionados.Add(g);
                    continue;
                }

                XYZ centro = null;
                XYZ extremo = null;
                XYZ dir = XYZ.BasisX;

                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    centro = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, 0);
                    extremo = new XYZ(p1.X, p1.Y, 0);
                    XYZ v = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    if (!v.IsZeroLength()) dir = v.Normalize();
                }
                else if (el.Location is LocationPoint lp)
                {
                    centro = new XYZ(lp.Point.X, lp.Point.Y, 0);
                    extremo = centro;
                }
                else if (el is FamilyInstance fi)
                {
                    BoundingBoxXYZ bb = fi.get_BoundingBox(view);
                    if (bb != null)
                    {
                        XYZ mid = (bb.Min + bb.Max) * 0.5;
                        centro = new XYZ(mid.X, mid.Y, 0);
                        extremo = centro;
                    }
                }

                if (centro != null)
                {
                    Reference rEje = ObtenerReferenciaEjeElemento(el, view);
                    if (rEje != null)
                    {
                        itemsMep.Add(new ElementoAcotableInfo
                        {
                            Elemento = el,
                            Centro2D = centro,
                            PuntoExtremo = extremo ?? centro,
                            DirEje2D = dir,
                            RefGeom = rEje
                        });
                    }
                }
            }

            if (itemsMep.Count == 0 && (murosSeleccionados.Count >= 2 || gridsSeleccionados.Count >= 2))
            {
                return AcotarElementosReferenciaDirectos(doc, view, elementos, dimType);
            }

            if (itemsMep.Count == 0) return 0;

            // 3. Agrupar elementos MEP por el muro de referencia más cercano
            var murosCandidatos = murosSeleccionados.Count > 0 ? murosSeleccionados : murosEnVista;

            var gruposPorMuro = new Dictionary<Wall, List<ElementoAcotableInfo>>();
            List<ElementoAcotableInfo> itemsSinMuro = new List<ElementoAcotableInfo>();

            foreach (var item in itemsMep)
            {
                Wall mejorMuro = null;
                double menorDist = double.MaxValue;

                foreach (var wall in murosCandidatos)
                {
                    if (wall.Location is LocationCurve wLc && wLc.Curve != null)
                    {
                        IntersectionResult proj = wLc.Curve.Project(item.Centro2D);
                        if (proj != null)
                        {
                            XYZ pProj = new XYZ(proj.XYZPoint.X, proj.XYZPoint.Y, 0);
                            double dist = item.Centro2D.DistanceTo(pProj);
                            if (dist < menorDist && dist <= 16.0) // Hasta 5m de distancia al muro
                            {
                                menorDist = dist;
                                mejorMuro = wall;
                            }
                        }
                    }
                }

                if (mejorMuro != null)
                {
                    if (!gruposPorMuro.ContainsKey(mejorMuro))
                    {
                        gruposPorMuro[mejorMuro] = new List<ElementoAcotableInfo>();
                    }
                    gruposPorMuro[mejorMuro].Add(item);
                }
                else
                {
                    itemsSinMuro.Add(item);
                }
            }

            int cotasCreadas = 0;

            using (Transaction tx = new Transaction(doc, "Colocar Cotas Alineadas"))
            {
                tx.Start();

                // 4. Procesar cada grupo asociado a un muro
                foreach (var kvp in gruposPorMuro)
                {
                    Wall wall = kvp.Key;
                    var grupoItems = kvp.Value;
                    if (!(wall.Location is LocationCurve wLc) || !(wLc.Curve is Line lineWall)) continue;

                    XYZ wp0 = new XYZ(lineWall.GetEndPoint(0).X, lineWall.GetEndPoint(0).Y, 0);
                    XYZ wp1 = new XYZ(lineWall.GetEndPoint(1).X, lineWall.GetEndPoint(1).Y, 0);
                    XYZ dirWall = (wp1 - wp0).Normalize();
                    XYZ normWall = new XYZ(-dirWall.Y, dirWall.X, 0).Normalize(); // Vector normal al muro

                    // Determinar hacia qué lado del muro están los elementos
                    double dotSign = grupoItems.Average(it => (it.Centro2D - wp0).DotProduct(normWall));
                    if (dotSign < 0) normWall = -normWall;

                    // Referencia de la cara acabada del muro en ese lado
                    Reference refCaraMuro = ObtenerReferenciaCaraAcabadaMuro(wall, view, normWall);

                    // Agrupar elementos en columnas/ramas a lo largo del muro (tolerancia de 0.25 ft ≈ 7.5 cm para no duplicar cotas)
                    var ordenadosPorX = grupoItems
                        .OrderBy(it => (it.Centro2D - wp0).DotProduct(dirWall))
                        .ToList();

                    List<List<ElementoAcotableInfo>> columnas = new List<List<ElementoAcotableInfo>>();
                    foreach (var it in ordenadosPorX)
                    {
                        double proyX = (it.Centro2D - wp0).DotProduct(dirWall);
                        bool agregada = false;
                        foreach (var col in columnas)
                        {
                            double colProyX = (col[0].Centro2D - wp0).DotProduct(dirWall);
                            if (Math.Abs(proyX - colProyX) < 0.25)
                            {
                                col.Add(it);
                                agregada = true;
                                break;
                            }
                        }
                        if (!agregada)
                        {
                            columnas.Add(new List<ElementoAcotableInfo> { it });
                        }
                    }

                    double viewZ = view.GenLevel?.ProjectElevation ?? (view.Origin != null ? view.Origin.Z : lineWall.GetEndPoint(0).Z);

                    // A) COTAS VERTICALES / PERPENDICULARES: Distancia de cada columna/rama a la cara del muro (e.g. 0.89, 0.79, 0.79, 0.89)
                    List<Reference> carasMuro = ObtenerReferenciasCarasMuro(wall, view, normWall);

                    foreach (var col in columnas)
                    {
                        try
                        {
                            // 1. Recopilar todas las referencias candidatas de todos los elementos de la columna
                            List<Reference> colRefs = new List<Reference>();
                            foreach (var it in col)
                            {
                                var refsElem = ObtenerReferenciasParalelasCandidatas(it.Elemento, view, normWall, dirWall);
                                foreach (var r in refsElem)
                                {
                                    if (r != null && !colRefs.Contains(r)) colRefs.Add(r);
                                }
                            }

                            // Si no se encontró ninguna referencia específica, añadir las RefGeom de los elementos
                            foreach (var it in col)
                            {
                                if (it.RefGeom != null && !colRefs.Contains(it.RefGeom)) colRefs.Add(it.RefGeom);
                            }

                            if (carasMuro.Count == 0 || colRefs.Count == 0) continue;

                            // 2. Calcular la posición geométrica de la línea de cota perpendicular
                            double proyXProm = col.Average(it => (it.Centro2D - wp0).DotProduct(dirWall));
                            double maxDistY = col.Max(it => (it.PuntoExtremo - wp0).DotProduct(normWall));

                            // Punto base en la cara del muro
                            XYZ pBaseCol = wp0 + dirWall * proyXProm;
                            XYZ offsetLateral = -dirWall * 0.40; // ~12 cm a la izquierda del tramo

                            XYZ dimStart = new XYZ(
                                (pBaseCol + offsetLateral - normWall * 2.0).X,
                                (pBaseCol + offsetLateral - normWall * 2.0).Y,
                                viewZ
                            );
                            XYZ dimEnd = new XYZ(
                                (pBaseCol + offsetLateral + normWall * (maxDistY + 5.0)).X,
                                (pBaseCol + offsetLateral + normWall * (maxDistY + 5.0)).Y,
                                viewZ
                            );

                            Line dimLinePerp = Line.CreateBound(dimStart, dimEnd);

                            // 3. Probar la creación de cota con las referencias disponibles hasta que Revit acepte una
                            bool cotaCreada = false;
                            foreach (var rMuro in carasMuro)
                            {
                                foreach (var rCol in colRefs)
                                {
                                    try
                                    {
                                        ReferenceArray refArrPerp = new ReferenceArray();
                                        refArrPerp.Append(rMuro);
                                        refArrPerp.Append(rCol);

                                        Dimension dimPerp = doc.Create.NewDimension(view, dimLinePerp, refArrPerp, dimType);
                                        if (dimPerp != null)
                                        {
                                            AjustarPosicionTextoDimension(dimPerp);
                                            cotasCreadas++;
                                            cotaCreada = true;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                                if (cotaCreada) break;
                            }
                        }
                        catch { }
                    }

                    // B) COTA EN CADENA HORIZONTAL: Intereje continuo entre columnas a lo largo del muro (e.g. 1.60, 1.63, 1.16)
                    if (columnas.Count >= 2)
                    {
                        try
                        {
                            ReferenceArray refArrChain = new ReferenceArray();
                            double maxDistMuro = 0;

                            foreach (var col in columnas)
                            {
                                // Tomar exactamente UNA referencia por columna (evitando segmentos 0.00)
                                Reference rColChain = null;

                                // Prioridad: Centerline de MEPCurve perpendicular al muro (corre a lo largo de normWall)
                                var repMep = col.FirstOrDefault(it => it.Elemento is MEPCurve);
                                if (repMep != null && repMep.RefGeom != null)
                                {
                                    rColChain = repMep.RefGeom;
                                }

                                // Si no hay MEPCurve o no tiene RefGeom, buscar CenterLeftRight / CenterFrontBack en FamilyInstance
                                if (rColChain == null)
                                {
                                    foreach (var it in col)
                                    {
                                        if (it.Elemento is FamilyInstance fi)
                                        {
                                            try
                                            {
                                                var refsLR = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight);
                                                if (refsLR != null && refsLR.Count > 0) { rColChain = refsLR[0]; break; }
                                                var refsFB = fi.GetReferences(FamilyInstanceReferenceType.CenterFrontBack);
                                                if (refsFB != null && refsFB.Count > 0) { rColChain = refsFB[0]; break; }
                                            }
                                            catch { }
                                        }
                                    }
                                }

                                if (rColChain == null)
                                {
                                    rColChain = col[0].RefGeom;
                                }

                                if (rColChain != null)
                                {
                                    refArrChain.Append(rColChain);
                                }

                                double distCol = col.Max(it => (it.PuntoExtremo - wp0).DotProduct(normWall));
                                if (distCol > maxDistMuro) maxDistMuro = distCol;
                            }

                            if (refArrChain.Size >= 2)
                            {
                                double distOffsetCota = maxDistMuro + 0.85; // 0.85 ft (~25 cm) por encima de las tomas/conduits

                                var colCentral = columnas[columnas.Count / 2];
                                double proyLong = (colCentral[0].Centro2D - wp0).DotProduct(dirWall);
                                XYZ posBaseCadena = new XYZ(
                                    wp0.X + dirWall.X * proyLong + normWall.X * distOffsetCota,
                                    wp0.Y + dirWall.Y * proyLong + normWall.Y * distOffsetCota,
                                    viewZ
                                );

                                XYZ chainStart = posBaseCadena - dirWall * 30.0;
                                XYZ chainEnd = posBaseCadena + dirWall * 30.0;

                                Line dimLineChain = Line.CreateBound(chainStart, chainEnd);
                                Dimension dimChain = doc.Create.NewDimension(view, dimLineChain, refArrChain, dimType);
                                if (dimChain != null)
                                {
                                    AjustarPosicionTextoDimension(dimChain);
                                    cotasCreadas++;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 5. Procesar elementos sin muro (cadenas entre elementos paralelos en espacio abierto)
                if (itemsSinMuro.Count >= 2)
                {
                    try
                    {
                        var dirPrincipal = itemsSinMuro[0].DirEje2D;
                        var dirPerp = new XYZ(-dirPrincipal.Y, dirPrincipal.X, 0).Normalize();

                        var ordenadosPorPerp = itemsSinMuro
                            .OrderBy(it => it.Centro2D.DotProduct(dirPerp))
                            .ToList();

                        // Agrupar elementos paralelos que compartan el mismo eje (evitar 0.00)
                        List<List<ElementoAcotableInfo>> lineasParalelas = new List<List<ElementoAcotableInfo>>();
                        foreach (var it in ordenadosPorPerp)
                        {
                            double proy = it.Centro2D.DotProduct(dirPerp);
                            bool agregada = false;
                            foreach (var lp in lineasParalelas)
                            {
                                double lpProy = lp[0].Centro2D.DotProduct(dirPerp);
                                if (Math.Abs(proy - lpProy) < 0.25)
                                {
                                    lp.Add(it);
                                    agregada = true;
                                    break;
                                }
                            }
                            if (!agregada)
                            {
                                lineasParalelas.Add(new List<ElementoAcotableInfo> { it });
                            }
                        }

                        if (lineasParalelas.Count >= 2)
                        {
                            ReferenceArray refArrOpen = new ReferenceArray();
                            foreach (var lp in lineasParalelas)
                            {
                                var rep = lp.FirstOrDefault(it => it.Elemento is MEPCurve) ?? lp.First();
                                if (rep.RefGeom != null)
                                {
                                    refArrOpen.Append(rep.RefGeom);
                                }
                            }

                            if (refArrOpen.Size >= 2)
                            {
                                XYZ centroProm = new XYZ(
                                    lineasParalelas.Average(lp => lp[0].Centro2D.X),
                                    lineasParalelas.Average(lp => lp[0].Centro2D.Y),
                                    0
                                );

                                XYZ pStart = (centroProm + dirPrincipal * 1.5) - dirPerp * 15.0;
                                XYZ pEnd = (centroProm + dirPrincipal * 1.5) + dirPerp * 15.0;

                                Line dimLineOpen = Line.CreateBound(pStart, pEnd);
                                Dimension dimOpen = doc.Create.NewDimension(view, dimLineOpen, refArrOpen, dimType);
                                if (dimOpen != null)
                                {
                                    AjustarPosicionTextoDimension(dimOpen);
                                    cotasCreadas++;
                                }
                            }
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return cotasCreadas;
        }

        private static int AcotarElementosReferenciaDirectos(Document doc, Autodesk.Revit.DB.View view, List<Element> refElements, DimensionType dimType)
        {
            if (refElements == null || refElements.Count < 2) return 0;
            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Colocar Cotas Alineadas"))
            {
                tx.Start();
                try
                {
                    ReferenceArray refArray = new ReferenceArray();
                    List<XYZ> centros = new List<XYZ>();

                    foreach (var e in refElements)
                    {
                        Reference r = null;
                        XYZ pt = null;
                        if (e is Wall w)
                        {
                            r = ObtenerReferenciaCaraAcabadaMuro(w, view, XYZ.BasisX) ?? ObtenerReferenciaCaraAcabadaMuro(w, view, XYZ.BasisY);
                            pt = (w.Location as LocationCurve)?.Curve?.Evaluate(0.5, true);
                        }
                        else if (e is Grid g)
                        {
                            r = ObtenerReferenciaGrid(g, view);
                            pt = (g.Curve as Line)?.Evaluate(0.5, true);
                        }
                        else
                        {
                            r = ObtenerReferenciaEjeElemento(e, view);
                            pt = (e.Location as LocationPoint)?.Point ?? (e.Location as LocationCurve)?.Curve?.Evaluate(0.5, true);
                        }

                        if (r != null && pt != null)
                        {
                            refArray.Append(r);
                            centros.Add(pt);
                        }
                    }

                    if (refArray.Size >= 2 && centros.Count >= 2)
                    {
                        XYZ c0 = centros[0];
                        XYZ c1 = centros[1];
                        XYZ dir = (c1 - c0).Normalize();
                        if (!dir.IsZeroLength())
                        {
                            XYZ mid = (c0 + c1) * 0.5;
                            XYZ pStart = mid - dir * 10.0;
                            XYZ pEnd = mid + dir * 10.0;
                            Line line = Line.CreateBound(pStart, pEnd);
                            Dimension dim = doc.Create.NewDimension(view, line, refArray, dimType);
                            if (dim != null)
                            {
                                AjustarPosicionTextoDimension(dim);
                                creados++;
                            }
                        }
                    }
                }
                catch { }
                tx.Commit();
            }
            return creados;
        }

        /// <summary>
        /// Ajusta la posición de texto en segmentos de cota pequeños para evitar solapamientos.
        /// </summary>
        private static void AjustarPosicionTextoDimension(Dimension dim)
        {
            try
            {
                if (dim == null) return;
                if (dim.NumberOfSegments > 0)
                {
                    for (int i = 0; i < dim.NumberOfSegments; i++)
                    {
                        DimensionSegment seg = dim.Segments.get_Item(i);
                        if (seg != null && seg.Value.HasValue && seg.Value.Value < 0.6) // Menos de 18 cm
                        {
                            XYZ currPos = seg.TextPosition;
                            XYZ lineDir = (dim.Curve as Line)?.Direction ?? XYZ.BasisY;
                            seg.TextPosition = currPos + lineDir * (i % 2 == 0 ? 0.35 : -0.35);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Obtiene o configura automáticamente un tipo de cota lineal limpio con altura de texto estándar 2.5 mm.
        /// </summary>
        public static DimensionType ObtenerOCrearTipoCotaAlineada(Document doc)
        {
            try
            {
                var dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => t.StyleType == DimensionStyleType.Linear || t.StyleType == DimensionStyleType.LinearFixed)
                    .ToList();

                // 1. Buscar si ya existe "DC - Cotas Alineadas" o "Diagonal - 2.5mm" o "Diagonal - 2mm"
                var exacto = dimTypes.FirstOrDefault(t =>
                    string.Equals(t.Name.Trim(), "DC - Cotas Alineadas", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name.Trim(), "DC - Cota Alineada", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name.Trim(), "Diagonal - 2.5mm", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name.Trim(), "Diagonal - 2.0mm", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name.Trim(), "Diagonal - 2mm", StringComparison.OrdinalIgnoreCase));

                if (exacto != null) return exacto;

                // 2. Buscar cualquier tipo limpio con "2.5mm" o "2mm" o "Diagonal"
                var pref = dimTypes.FirstOrDefault(t =>
                    (t.Name.IndexOf("2.5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     t.Name.IndexOf("2mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     t.Name.IndexOf("Diagonal", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    t.Name.IndexOf("Grande", StringComparison.OrdinalIgnoreCase) < 0 &&
                    t.Name.IndexOf("3.5", StringComparison.OrdinalIgnoreCase) < 0 &&
                    t.Name.IndexOf("5mm", StringComparison.OrdinalIgnoreCase) < 0);

                if (pref != null) return pref;

                var alt = dimTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("Linear", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Cota", StringComparison.OrdinalIgnoreCase) >= 0);

                return alt ?? dimTypes.FirstOrDefault();
            }
            catch { return null; }
        }

        public static DimensionType ObtenerTipoCotaAlineada(Document doc)
        {
            return ObtenerOCrearTipoCotaAlineada(doc);
        }

        public static Reference ObtenerReferenciaEjeElemento(Element elem, Autodesk.Revit.DB.View view)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom != null)
                {
                    Reference r = BuscarReferenciaEnGeometria(geom);
                    if (r != null) return r;
                }

                if (elem is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                {
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        foreach (Connector refConn in conn.AllRefs)
                        {
                            if (refConn.Owner is MEPCurve mep && mep.Id != elem.Id)
                            {
                                Reference rMep = ObtenerReferenciaEjeElemento(mep, view);
                                if (rMep != null) return rMep;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Reference BuscarReferenciaEnGeometria(GeometryElement geom)
        {
            foreach (GeometryObject obj in geom)
            {
                if (obj is Line line && line.Reference != null)
                {
                    return line.Reference;
                }
                if (obj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face.Reference != null) return face.Reference;
                    }
                }
                if (obj is GeometryInstance gi)
                {
                    GeometryElement instGeom = gi.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        Reference r = BuscarReferenciaEnGeometria(instGeom);
                        if (r != null) return r;
                    }
                    GeometryElement symGeom = gi.GetSymbolGeometry();
                    if (symGeom != null)
                    {
                        Reference r = BuscarReferenciaEnGeometria(symGeom);
                        if (r != null) return r;
                    }
                }
            }
            return null;
        }

        public static List<Reference> ObtenerReferenciasCarasMuro(Wall wall, Autodesk.Revit.DB.View view, XYZ normWall)
        {
            List<Reference> res = new List<Reference>();
            if (wall == null) return res;

            try
            {
                var extFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior);
                var intFaces = HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior);
                var allFaces = new List<Reference>();
                if (extFaces != null) allFaces.AddRange(extFaces);
                if (intFaces != null) allFaces.AddRange(intFaces);

                List<Tuple<Reference, double>> valoradas = new List<Tuple<Reference, double>>();
                foreach (var rFace in allFaces)
                {
                    try
                    {
                        GeometryObject geomObj = wall.GetGeometryObjectFromReference(rFace);
                        if (geomObj is PlanarFace pf)
                        {
                            double dot = pf.FaceNormal.DotProduct(normWall);
                            valoradas.Add(Tuple.Create(rFace, dot));
                        }
                    }
                    catch { }
                }

                // Ordenar: primero las caras con normal apuntando hacia los elementos (dot > 0), luego las demás
                foreach (var item in valoradas.OrderByDescending(x => x.Item2))
                {
                    if (!res.Contains(item.Item1)) res.Add(item.Item1);
                }

                if (res.Count == 0 && allFaces.Count > 0)
                {
                    res.AddRange(allFaces);
                }

                // Fallback a geometría directa
                Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
                GeometryElement geom = wall.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf && pf.Reference != null)
                                {
                                    if (Math.Abs(pf.FaceNormal.DotProduct(normWall)) > 0.40)
                                    {
                                        if (!res.Contains(pf.Reference)) res.Add(pf.Reference);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return res;
        }

        public static List<Reference> ObtenerReferenciasParalelasCandidatas(Element elem, Autodesk.Revit.DB.View view, XYZ normWall, XYZ dirWall)
        {
            List<Reference> candidatas = new List<Reference>();
            if (elem == null) return candidatas;

            try
            {
                // 1. Si es FamilyInstance (caja, dispositivo, accesorio/fitting)
                if (elem is FamilyInstance fi)
                {
                    var refTypes = new[]
                    {
                        FamilyInstanceReferenceType.CenterFrontBack,
                        FamilyInstanceReferenceType.CenterLeftRight,
                        FamilyInstanceReferenceType.Front,
                        FamilyInstanceReferenceType.Back,
                        FamilyInstanceReferenceType.Left,
                        FamilyInstanceReferenceType.Right,
                        FamilyInstanceReferenceType.StrongReference,
                        FamilyInstanceReferenceType.WeakReference
                    };

                    foreach (var rt in refTypes)
                    {
                        try
                        {
                            var refs = fi.GetReferences(rt);
                            if (refs != null)
                            {
                                foreach (var r in refs)
                                {
                                    if (r != null && !candidatas.Contains(r)) candidatas.Add(r);
                                }
                            }
                        }
                        catch { }
                    }

                    Options optFine = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
                    GeometryElement geom = fi.get_Geometry(optFine);
                    if (geom != null)
                    {
                        BuscarReferenciasParalelasEnGeometria(geom, normWall, dirWall, candidatas);
                    }
                }
                // 2. Si es MEPCurve (Conduit, Tubo, etc.)
                else if (elem is MEPCurve mep)
                {
                    Options optFine = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
                    GeometryElement geom = mep.get_Geometry(optFine);
                    if (geom != null)
                    {
                        BuscarReferenciasParalelasEnGeometria(geom, normWall, dirWall, candidatas);
                    }

                    if (mep.ConnectorManager != null)
                    {
                        foreach (Connector conn in mep.ConnectorManager.Connectors)
                        {
                            foreach (Connector refConn in conn.AllRefs)
                            {
                                if (refConn.Owner != null && refConn.Owner.Id != elem.Id)
                                {
                                    var subRefs = ObtenerReferenciasParalelasCandidatas(refConn.Owner, view, normWall, dirWall);
                                    foreach (var sr in subRefs)
                                    {
                                        if (sr != null && !candidatas.Contains(sr)) candidatas.Add(sr);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Options optFine = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
                    GeometryElement geom = elem.get_Geometry(optFine);
                    if (geom != null)
                    {
                        BuscarReferenciasParalelasEnGeometria(geom, normWall, dirWall, candidatas);
                    }
                }
            }
            catch { }

            return candidatas;
        }

        private static void BuscarReferenciasParalelasEnGeometria(GeometryElement geom, XYZ normWall, XYZ dirWall, List<Reference> candidatas)
        {
            if (geom == null) return;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && pf.Reference != null)
                        {
                            if (Math.Abs(pf.FaceNormal.DotProduct(normWall)) > 0.50)
                            {
                                if (!candidatas.Contains(pf.Reference)) candidatas.Add(pf.Reference);
                            }
                        }
                    }
                }
                else if (obj is Line line && line.Reference != null)
                {
                    if (Math.Abs(line.Direction.DotProduct(dirWall)) > 0.50)
                    {
                        if (!candidatas.Contains(line.Reference)) candidatas.Add(line.Reference);
                    }
                }
                else if (obj is GeometryInstance gi)
                {
                    try
                    {
                        GeometryElement symGeom = gi.GetSymbolGeometry();
                        if (symGeom != null) BuscarReferenciasParalelasEnGeometria(symGeom, normWall, dirWall, candidatas);
                        GeometryElement instGeom = gi.GetInstanceGeometry();
                        if (instGeom != null) BuscarReferenciasParalelasEnGeometria(instGeom, normWall, dirWall, candidatas);
                    }
                    catch { }
                }
            }
        }

        public static Reference ObtenerReferenciaExtremoParaCotaMuro(Element elem, Autodesk.Revit.DB.View view, XYZ normWall)
        {
            try
            {
                XYZ dirWall = new XYZ(-normWall.Y, normWall.X, 0).Normalize();
                var cands = ObtenerReferenciasParalelasCandidatas(elem, view, normWall, dirWall);
                if (cands.Count > 0) return cands[0];
            }
            catch { }
            return null;
        }

        public static Reference ObtenerReferenciaCaraAcabadaMuro(Wall wall, Autodesk.Revit.DB.View view, XYZ dirNormalCota)
        {
            try
            {
                var caras = ObtenerReferenciasCarasMuro(wall, view, dirNormalCota);
                if (caras.Count > 0) return caras[0];
            }
            catch { }
            return null;
        }

        public static Reference ObtenerReferenciaGrid(Grid grid, Autodesk.Revit.DB.View view)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = grid.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Line line && line.Reference != null)
                        {
                            return line.Reference;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Flujo interactivo por clic para colocar Cotas de Nivel (SpotDimension).
        /// Clic 1: Selecciona la tubería/red.
        /// Clic 2 (opcional): Posición personalizada de la cota con directriz (ESC para posición automática).
        /// </summary>
        public static int TaguearNivelesPorClic(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, OpcionNivelReferencia opcionNivel = null)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            if (opcionNivel == null)
            {
                opcionNivel = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
                if (opcionNivel == null) return 0;
            }

            int colocados = 0;
            var filter = new MepElementSelectionFilter();

            // Buscar tipo SpotDimension "DC - Elevacion relativa"
            ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
            SpotCollisionContext collisionCtx = SpotCollisionContext.BuildFromView(doc, view);

            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.PointOnElement,
                        filter,
                        "Clic en tubería o conducto para colocar Cota de Nivel (ESC para terminar)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                if (pickRef == null) break;

                Element el = doc.GetElement(pickRef.ElementId);
                if (el == null) continue;

                if (!EsTuberiaHorizontalSinPendiente(el, out _, out _))
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Cotas de Nivel", "Solo se pueden colocar cotas de nivel en tuberías o conduits horizontales sin pendiente.");
                    continue;
                }

                // 1. Obtener punto en el eje/centro 3D del elemento
                XYZ puntoEje = ObtenerPuntoEjeDesdeClic(el, pickRef.GlobalPoint);

                // 2. Determinar el nivel de referencia general elegido
                Level nivelRef = ObtenerNivelReferencia(doc, view, el, puntoEje, opcionNivel);

                // 3. Ubicación del texto / directriz (opcional: el usuario puede dar un 2do clic o presionar ESC para auto)
                XYZ puntoUbicacion = null;
                try
                {
                    puntoUbicacion = uidoc.Selection.PickPoint(
                        ObjectSnapTypes.None,
                        "Clic para ubicar la directriz de la cota (o ESC para ubicación automática)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    puntoUbicacion = null;
                }

                // 4. Crear Spot Dimension en transacción
                using (Transaction tx = new Transaction(doc, "Colocar Cota de Nivel"))
                {
                    tx.Start();

                    SpotDimension spot = ColocarSpotElevation(
                        doc,
                        view,
                        el,
                        pickRef,
                        puntoEje,
                        puntoUbicacion,
                        spotTypeId,
                        nivelRef,
                        collisionCtx,
                        null,
                        opcionNivel
                    );

                    if (spot != null)
                    {
                        colocados++;
                    }

                    tx.Commit();
                }
            }

            return colocados;
        }

        private class TramoMEPInfo
        {
            public Element Elemento;
            public XYZ P0;
            public XYZ P1;
            public XYZ DirCurva;
            public double Longitud;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double CentroX;
            public double CentroY;
            public double Z;
            public bool EsHorizontalEnPlanta;
        }

        private static double DistanciaEntreIntervalos(double minA, double maxA, double minB, double maxB)
        {
            if (maxA < minB) return minB - maxA;
            if (maxB < minA) return minA - maxB;
            return 0.0;
        }

        public static int TaguearNivelesTodoEnVista(Document doc, Autodesk.Revit.DB.View view, OpcionNivelReferencia opcionNivel = null)
        {
            return TaguearNivelesTodoEnVista(null, doc, view, opcionNivel);
        }

        /// <summary>
        /// Permite al usuario seleccionar múltiples elementos MEP (conduits, tuberías, bandejas, ductos)
        /// con selección múltiple interactiva (cursor +/- y botón Finish) y coloca cotas de nivel calculadas automáticamente.
        /// </summary>
        public static int TaguearNivelesPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, OpcionNivelReferencia opcionNivel = null)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            if (opcionNivel == null)
            {
                opcionNivel = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
                if (opcionNivel == null) return 0;
            }

            List<Element> elementos = new List<Element>();
            var filter = new MepElementSelectionFilter();

            // 1. Revisar si el usuario ya tenía elementos seleccionados previamente en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        elementos.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (elementos.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los elementos MEP a acotar con nivel y haz clic en 'Finish':"
                    );

                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !elementos.Any(x => x.Id == el.Id))
                            {
                                elementos.Add(el);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (elementos.Count == 0) return 0;

            return ProcesarYColocarNivelesParaElementos(uidoc, doc, view, elementos, "Cotas de Nivel (Por Selección)", opcionNivel);
        }

        /// <summary>
        /// Procesa y coloca Spot Elevations automáticamente en las redes MEP de la vista.
        /// Agrupa los tramos continuos que comparten la misma elevación Z y la misma línea en planta
        /// (incluso si tienen cajas de paso intermedias) para colocar una única cota en la mitad de toda la tubería,
        /// omitiendo las cotas a la derecha y a la izquierda si tienen el mismo valor.
        /// </summary>
        public static int TaguearNivelesTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, OpcionNivelReferencia opcionNivel = null)
        {
            if (opcionNivel == null)
            {
                opcionNivel = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
                if (opcionNivel == null) return 0;
            }

            var categorias = new[]
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_DuctCurves
            };

            List<Element> elementos = new List<Element>();
            foreach (var cat in categorias)
            {
                var elems = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();
                elementos.AddRange(elems);
            }

            return ProcesarYColocarNivelesParaElementos(uidoc, doc, view, elementos, "Cotas de Nivel MEP (Automático)", opcionNivel);
        }

        /// <summary>
        /// Procesa una lista de elementos MEP para agrupar tramos continuos y colocar Spot Elevations evitando duplicados y colisiones.
        /// </summary>
        public static int ProcesarYColocarNivelesParaElementos(
            UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, List<Element> elementos, string nombreTransaccion = "Cotas de Nivel MEP", OpcionNivelReferencia opcionNivel = null)
        {
            if (elementos == null || elementos.Count == 0) return 0;

            // Recolectar Spot Elevations ya existentes en la vista para evitar duplicados
            var spotsExistentes = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(SpotDimension))
                .Cast<SpotDimension>()
                .ToList();

            List<XYZ> puntosSpotsExistentes = new List<XYZ>();
            foreach (var s in spotsExistentes)
            {
                try
                {
                    if (s.Origin != null)
                    {
                        puntosSpotsExistentes.Add(s.Origin);
                    }
                }
                catch { }
            }

            // 1. Recolectar tramos MEP válidos
            List<TramoMEPInfo> tramosValidos = new List<TramoMEPInfo>();
            foreach (var el in elementos)
            {
                if (!EsTuberiaHorizontalSinPendiente(el, out XYZ dirCurva, out XYZ puntoCentro))
                {
                    continue;
                }

                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    double len = lc.Curve.Length;

                    bool esHoriz = Math.Abs(dirCurva.X) >= Math.Abs(dirCurva.Y);

                    tramosValidos.Add(new TramoMEPInfo
                    {
                        Elemento = el,
                        P0 = p0,
                        P1 = p1,
                        DirCurva = dirCurva,
                        Longitud = len,
                        MinX = Math.Min(p0.X, p1.X),
                        MaxX = Math.Max(p0.X, p1.X),
                        MinY = Math.Min(p0.Y, p1.Y),
                        MaxY = Math.Max(p0.Y, p1.Y),
                        CentroX = (p0.X + p1.X) / 2.0,
                        CentroY = (p0.Y + p1.Y) / 2.0,
                        Z = (p0.Z + p1.Z) / 2.0,
                        EsHorizontalEnPlanta = esHoriz
                    });
                }
            }

            if (tramosValidos.Count == 0) return 0;

            // 2. Agrupar tramos continuos que comparten la misma elevación Z y la misma línea en planta
            List<List<TramoMEPInfo>> corridas = new List<List<TramoMEPInfo>>();

            foreach (var tramo in tramosValidos)
            {
                bool agregado = false;
                foreach (var grupo in corridas)
                {
                    bool conecta = grupo.Any(otro =>
                    {
                        if (Math.Abs(otro.Z - tramo.Z) >= 0.05) return false;
                        if (otro.EsHorizontalEnPlanta != tramo.EsHorizontalEnPlanta) return false;

                        if (tramo.EsHorizontalEnPlanta)
                        {
                            if (Math.Abs(otro.CentroY - tramo.CentroY) > 0.35) return false;
                            return DistanciaEntreIntervalos(otro.MinX, otro.MaxX, tramo.MinX, tramo.MaxX) <= 4.0;
                        }
                        else
                        {
                            if (Math.Abs(otro.CentroX - tramo.CentroX) > 0.35) return false;
                            return DistanciaEntreIntervalos(otro.MinY, otro.MaxY, tramo.MinY, tramo.MaxY) <= 4.0;
                        }
                    });

                    if (conecta)
                    {
                        grupo.Add(tramo);
                        agregado = true;
                        break;
                    }
                }

                if (!agregado)
                {
                    corridas.Add(new List<TramoMEPInfo> { tramo });
                }
            }

            // Fusión transitiva de grupos conectados
            bool huboFusion = true;
            while (huboFusion)
            {
                huboFusion = false;
                for (int i = 0; i < corridas.Count; i++)
                {
                    for (int j = i + 1; j < corridas.Count; j++)
                    {
                        var g1 = corridas[i];
                        var g2 = corridas[j];

                        bool seUnen = g1.Any(t1 => g2.Any(t2 =>
                        {
                            if (Math.Abs(t1.Z - t2.Z) >= 0.05) return false;
                            if (t1.EsHorizontalEnPlanta != t2.EsHorizontalEnPlanta) return false;

                            if (t1.EsHorizontalEnPlanta)
                            {
                                if (Math.Abs(t1.CentroY - t2.CentroY) > 0.35) return false;
                                return DistanciaEntreIntervalos(t1.MinX, t1.MaxX, t2.MinX, t2.MaxX) <= 4.0;
                            }
                            else
                            {
                                if (Math.Abs(t1.CentroX - t2.CentroX) > 0.35) return false;
                                return DistanciaEntreIntervalos(t1.MinY, t1.MaxY, t2.MinY, t2.MaxY) <= 4.0;
                            }
                        }));

                        if (seUnen)
                        {
                            g1.AddRange(g2);
                            corridas.RemoveAt(j);
                            huboFusion = true;
                            break;
                        }
                    }
                    if (huboFusion) break;
                }
            }

            ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
            int creados = 0;
            var puntosRecienCreados = new List<XYZ>();
            var logFallosSpot = new List<string>();

            SpotCollisionContext collisionCtx = SpotCollisionContext.BuildFromView(doc, view, tramosValidos.Select(t => t.Elemento));

            using (Transaction tx = new Transaction(doc, nombreTransaccion))
            {
                tx.Start();

                if (view.DetailLevel != ViewDetailLevel.Fine)
                {
                    try
                    {
                        view.DetailLevel = ViewDetailLevel.Fine;
                    }
                    catch { }
                }

                foreach (var grupo in corridas)
                {
                    try
                    {
                        bool esHoriz = grupo.First().EsHorizontalEnPlanta;
                        XYZ puntoMedioCorrida;
                        TramoMEPInfo tramoSeleccionado;

                        if (esHoriz)
                        {
                            double globalMinX = grupo.Min(t => t.MinX);
                            double globalMaxX = grupo.Max(t => t.MaxX);
                            double midX = (globalMinX + globalMaxX) / 2.0;

                            tramoSeleccionado = grupo.FirstOrDefault(t => midX >= t.MinX && midX <= t.MaxX);
                            if (tramoSeleccionado == null)
                            {
                                tramoSeleccionado = grupo.OrderBy(t => Math.Min(Math.Abs(midX - t.MinX), Math.Abs(midX - t.MaxX))).First();
                            }

                            double posX = Math.Max(tramoSeleccionado.MinX + 0.3, Math.Min(tramoSeleccionado.MaxX - 0.3, midX));
                            puntoMedioCorrida = new XYZ(posX, tramoSeleccionado.CentroY, tramoSeleccionado.Z);
                        }
                        else
                        {
                            double globalMinY = grupo.Min(t => t.MinY);
                            double globalMaxY = grupo.Max(t => t.MaxY);
                            double midY = (globalMinY + globalMaxY) / 2.0;

                            tramoSeleccionado = grupo.FirstOrDefault(t => midY >= t.MinY && midY <= t.MaxY);
                            if (tramoSeleccionado == null)
                            {
                                tramoSeleccionado = grupo.OrderBy(t => Math.Min(Math.Abs(midY - t.MinY), Math.Abs(midY - t.MaxY))).First();
                            }

                            double posY = Math.Max(tramoSeleccionado.MinY + 0.3, Math.Min(tramoSeleccionado.MaxY - 0.3, midY));
                            puntoMedioCorrida = new XYZ(tramoSeleccionado.CentroX, posY, tramoSeleccionado.Z);
                        }

                        bool yaExiste = puntosSpotsExistentes.Any(p =>
                            Math.Sqrt(Math.Pow(p.X - puntoMedioCorrida.X, 2) + Math.Pow(p.Y - puntoMedioCorrida.Y, 2)) < 1.0);

                        if (yaExiste)
                        {
                            continue;
                        }

                        Level nivelRef = ObtenerNivelReferencia(doc, view, tramoSeleccionado.Elemento, puntoMedioCorrida, opcionNivel);

                        SpotDimension spot = ColocarSpotElevationAutomatico(
                            doc, view, tramoSeleccionado.Elemento, puntoMedioCorrida, tramoSeleccionado.DirCurva, spotTypeId, nivelRef, collisionCtx, logFallosSpot, opcionNivel);

                        if (spot != null)
                        {
                            creados++;
                            puntosSpotsExistentes.Add(puntoMedioCorrida);
                            puntosRecienCreados.Add(puntoMedioCorrida);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (creados == 0 && logFallosSpot.Count > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Debug - Fallos al crear Spot",
                    string.Join("\n\n", logFallosSpot.Take(10)));
            }

            if (uidoc != null && puntosRecienCreados.Count > 0)
            {
                try
                {
                    UIView uiView = uidoc.GetOpenUIViews()?.FirstOrDefault(uv => uv.ViewId == view.Id);
                    if (uiView != null)
                    {
                        IList<XYZ> zoomCorners = uiView.GetZoomCorners();
                        if (zoomCorners != null && zoomCorners.Count >= 2)
                        {
                            double minX = Math.Min(zoomCorners[0].X, zoomCorners[1].X) - 1.0;
                            double maxX = Math.Max(zoomCorners[0].X, zoomCorners[1].X) + 1.0;
                            double minY = Math.Min(zoomCorners[0].Y, zoomCorners[1].Y) - 1.0;
                            double maxY = Math.Max(zoomCorners[0].Y, zoomCorners[1].Y) + 1.0;

                            int enPantalla = puntosRecienCreados.Count(p =>
                                p.X >= minX && p.X <= maxX &&
                                p.Y >= minY && p.Y <= maxY);

                            if (enPantalla > 0)
                            {
                                return enPantalla;
                            }
                        }
                    }
                }
                catch { }
            }

            return creados;
        }

        /// <summary>
        /// Coloca una SpotDimension automáticamente sobre la cara superior en la mitad del tramo,
        /// calculando dinámicamente la directriz para no sobreponer la cota sobre otros elementos o cotas del plano.
        /// </summary>
        private static SpotDimension ColocarSpotElevationAutomatico(
            Document doc,
            Autodesk.Revit.DB.View view,
            Element el,
            XYZ puntoCentro,
            XYZ dirCurva,
            ElementId spotTypeId,
            Level nivelTecho,
            SpotCollisionContext collisionCtx = null,
            List<string> logDebug = null,
            OpcionNivelReferencia opcionNivel = null)
        {
            // 1. Obtener la referencia de la cara superior y el punto geométrico exacto sobre la cara
            if (!ObtenerReferenciaCaraSuperiorYPoint(el, view, puntoCentro, out Reference faceRef, out XYZ puntoEnCara))
            {
                logDebug?.Add($"Id {el?.Id}: No se encontró cara superior con referencia válida.");
                return null;
            }

            // 2. Calcular la posición limpia de la directriz (Leader) y hombro evitando sobreponerse en el plano
            CalcularPuntosDirectrizSinColision(el, puntoEnCara, dirCurva, collisionCtx, out XYZ bendPt, out XYZ endPt);

            // 3. Crear la SpotDimension pasando puntoEnCara como origin y como refPt
            SpotDimension spot = null;
            try
            {
                spot = doc.Create.NewSpotElevation(view, faceRef, puntoEnCara, bendPt, endPt, puntoEnCara, true);
            }
            catch (Exception ex)
            {
                logDebug?.Add($"Id {el?.Id}: NewSpotElevation falló: {ex.Message}");
                return null;
            }

            if (spot != null)
            {
                if (spotTypeId != null && spotTypeId != ElementId.InvalidElementId)
                {
                    try { spot.ChangeTypeId(spotTypeId); } catch { }
                }
                ConfigurarSpotElevation(spot, nivelTecho, doc, opcionNivel);

                try
                {
                    double dirX = (endPt.X >= bendPt.X) ? 1.0 : -1.0;
                    spot.TextPosition = new XYZ(bendPt.X + dirX * 0.45, bendPt.Y, bendPt.Z);
                }
                catch { }

                if (collisionCtx != null)
                {
                    collisionCtx.AddPlacedTag(bendPt, endPt);
                }
            }

            return spot;
        }

        private static SpotDimension ColocarSpotElevation(
            Document doc,
            Autodesk.Revit.DB.View view,
            Element el,
            Reference pickRef,
            XYZ puntoEje,
            XYZ puntoUbicacion,
            ElementId spotTypeId,
            Level nivelTecho,
            SpotCollisionContext collisionCtx = null,
            List<string> logDebug = null,
            OpcionNivelReferencia opcionNivel = null)
        {
            XYZ refPt = puntoEje;
            XYZ bendPt;
            XYZ endPt;

            if (puntoUbicacion != null)
            {
                double shoulderLen = 1.2;
                // El codo se mantiene estrictamente alineado con la coordenada X del eje para asegurar flecha vertical
                bendPt = new XYZ(puntoEje.X, puntoUbicacion.Y, puntoEje.Z);
                double dirX = (puntoUbicacion.X < puntoEje.X) ? -1.0 : 1.0;
                endPt = new XYZ(bendPt.X + dirX * shoulderLen, bendPt.Y, bendPt.Z);
            }
            else
            {
                XYZ dirCurva = XYZ.BasisX;
                if (el?.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v = p1 - p0;
                    if (!v.IsZeroLength()) dirCurva = v.Normalize();
                }

                CalcularPuntosDirectrizSinColision(el, puntoEje, dirCurva, collisionCtx, out bendPt, out endPt);
            }

            SpotDimension spot = null;
            string motivo = "";

            if (pickRef != null)
            {
                try
                {
                    spot = doc.Create.NewSpotElevation(view, pickRef, refPt, bendPt, endPt, refPt, true);
                }
                catch (Exception ex) { motivo += $"pickRef falló: {ex.Message}. "; }
            }

            if (spot == null && el != null)
            {
                if (ObtenerReferenciaCaraSuperiorYPoint(el, view, puntoEje, out Reference faceRef, out XYZ puntoCara))
                {
                    try
                    {
                        spot = doc.Create.NewSpotElevation(view, faceRef, puntoCara, bendPt, endPt, puntoCara, true);
                    }
                    catch (Exception ex)
                    {
                        motivo += $"NewSpotElevation con faceRef falló: {ex.Message}. ";
                    }
                }
                else
                {
                    motivo += "No se encontró cara superior con referencia válida. ";
                }
            }

            if (spot != null)
            {
                if (spotTypeId != null && spotTypeId != ElementId.InvalidElementId)
                {
                    try { spot.ChangeTypeId(spotTypeId); } catch { }
                }
                ConfigurarSpotElevation(spot, nivelTecho, doc, opcionNivel);

                try
                {
                    double dirX = (endPt.X >= bendPt.X) ? 1.0 : -1.0;
                    spot.TextPosition = new XYZ(bendPt.X + dirX * 0.45, bendPt.Y, bendPt.Z);
                }
                catch { }

                if (collisionCtx != null)
                {
                    collisionCtx.AddPlacedTag(bendPt, endPt);
                }
            }
            else
            {
                logDebug?.Add($"Id {el?.Id}: {motivo}");
            }

            return spot;
        }

        #region Control de Colisiones y Anti-Sobreposición de Cotas en Plano

        private class Box2D
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public Box2D(double minX, double maxX, double minY, double maxY)
            {
                MinX = Math.Min(minX, maxX);
                MaxX = Math.Max(minX, maxX);
                MinY = Math.Min(minY, maxY);
                MaxY = Math.Max(minY, maxY);
            }

            public bool Intersects(Box2D other, double margin = 0.0)
            {
                if (MaxX + margin < other.MinX || MinX - margin > other.MaxX) return false;
                if (MaxY + margin < other.MinY || MinY - margin > other.MaxY) return false;
                return true;
            }

            public bool ContainsPoint(XYZ pt, double margin = 0.0)
            {
                return pt.X >= MinX - margin && pt.X <= MaxX + margin &&
                       pt.Y >= MinY - margin && pt.Y <= MaxY + margin;
            }
        }

        private class SpotCollisionContext
        {
            public List<Box2D> ObstacleBoxes = new List<Box2D>();
            public List<Tuple<XYZ, XYZ>> ElementSegments = new List<Tuple<XYZ, XYZ>>();
            public List<Tuple<XYZ, XYZ>> PlacedTagShoulders = new List<Tuple<XYZ, XYZ>>();
            public List<XYZ> PlacedTagEndPoints = new List<XYZ>();

            public static SpotCollisionContext BuildFromView(Document doc, Autodesk.Revit.DB.View view, IEnumerable<Element> excludeElements = null)
            {
                var ctx = new SpotCollisionContext();
                HashSet<ElementId> excludeIds = new HashSet<ElementId>(excludeElements?.Select(e => e.Id) ?? Enumerable.Empty<ElementId>());

                try
                {
                    // 1. Tags de cualquier categoría (IndependentTag: Conduit, Tuberías, Dispositivos, Aparatos, etc.)
                    var tags = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(IndependentTag))
                        .Cast<IndependentTag>();

                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        try
                        {
                            BoundingBoxXYZ bb = t.get_BoundingBox(view);
                            if (bb != null)
                            {
                                ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                            }
                            else
                            {
                                XYZ headPos = t.TagHeadPosition;
                                if (headPos != null)
                                {
                                    ctx.ObstacleBoxes.Add(new Box2D(headPos.X - 1.0, headPos.X + 1.0, headPos.Y - 0.45, headPos.Y + 0.45));
                                }
                            }

                            if (t.HasLeader)
                            {
                                try
                                {
                                    dynamic dynTag = t;
                                    XYZ head = t.TagHeadPosition;
                                    XYZ elbow = null;
                                    XYZ end = null;
                                    try { elbow = dynTag.LeaderElbow; } catch { }
                                    try { end = dynTag.LeaderEnd; } catch { }
                                    try { if (elbow == null) elbow = dynTag.LeaderElbowPosition; } catch { }
                                    try { if (end == null) end = dynTag.LeaderEndPosition; } catch { }

                                    if (elbow != null && head != null) ctx.ElementSegments.Add(Tuple.Create(head, (XYZ)elbow));
                                    if (elbow != null && end != null) ctx.ElementSegments.Add(Tuple.Create((XYZ)elbow, (XYZ)end));
                                    else if (head != null && end != null) ctx.ElementSegments.Add(Tuple.Create(head, (XYZ)end));
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    // 2. Cotas Lineales / Alineadas / Angulares (Dimension: OST_Dimensions como cotas 0.79, etc.)
                    var dimensions = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Dimension))
                        .Cast<Dimension>();

                    foreach (var d in dimensions)
                    {
                        if (d == null) continue;
                        try
                        {
                            BoundingBoxXYZ bb = d.get_BoundingBox(view);
                            if (bb != null)
                            {
                                ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                            }

                            if (d.Curve != null)
                            {
                                XYZ dp0 = d.Curve.GetEndPoint(0);
                                XYZ dp1 = d.Curve.GetEndPoint(1);
                                ctx.ElementSegments.Add(Tuple.Create(dp0, dp1));
                            }
                        }
                        catch { }
                    }

                    // 3. Cotas de Elevación existentes (SpotDimension)
                    var existingSpots = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpotDimension))
                        .Cast<SpotDimension>();

                    foreach (var s in existingSpots)
                    {
                        if (s == null) continue;
                        try
                        {
                            BoundingBoxXYZ bb = s.get_BoundingBox(view);
                            if (bb != null)
                            {
                                ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                            }

                            dynamic dynSpot = s;
                            XYZ endPt = null;
                            XYZ shoulderPt = null;

                            try { endPt = dynSpot.LeaderEndPosition ?? dynSpot.LeaderEnd; } catch { }
                            try { shoulderPt = dynSpot.LeaderShoulderPosition ?? dynSpot.LeaderShoulder; } catch { }
                            if (endPt == null) { try { endPt = s.Origin; } catch { } }

                            if (shoulderPt != null && endPt != null)
                            {
                                ctx.PlacedTagShoulders.Add(Tuple.Create((XYZ)shoulderPt, (XYZ)endPt));
                                ctx.PlacedTagEndPoints.Add((XYZ)endPt);
                            }
                            else if (endPt != null)
                            {
                                ctx.PlacedTagEndPoints.Add((XYZ)endPt);
                            }
                        }
                        catch { }
                    }

                    // 4. Notas de texto (TextNote)
                    var textNotes = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>();

                    foreach (var tn in textNotes)
                    {
                        if (tn == null) continue;
                        try
                        {
                            BoundingBoxXYZ bb = tn.get_BoundingBox(view);
                            if (bb != null)
                            {
                                ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                            }
                        }
                        catch { }
                    }

                    // 5. Cajas eléctricas, aparatos, tomas, luminarias, equipos mecánicos/eléctricos
                    var fixtureCategories = new[]
                    {
                        BuiltInCategory.OST_ElectricalFixtures,
                        BuiltInCategory.OST_LightingDevices,
                        BuiltInCategory.OST_LightingFixtures,
                        BuiltInCategory.OST_ElectricalEquipment,
                        BuiltInCategory.OST_CommunicationDevices,
                        BuiltInCategory.OST_DataDevices,
                        BuiltInCategory.OST_FireAlarmDevices,
                        BuiltInCategory.OST_SecurityDevices,
                        BuiltInCategory.OST_PlumbingFixtures,
                        BuiltInCategory.OST_MechanicalEquipment
                    };

                    foreach (var cat in fixtureCategories)
                    {
                        var fixtures = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType()
                            .ToElements();

                        foreach (var fix in fixtures)
                        {
                            try
                            {
                                BoundingBoxXYZ bb = fix.get_BoundingBox(view);
                                if (bb != null)
                                {
                                    ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                                }
                            }
                            catch { }
                        }
                    }

                    // 6. Curvas MEP (Tuberías, Conduits, Bandejas, Ductos, Fittings)
                    var mepCategories = new[]
                    {
                        BuiltInCategory.OST_PipeCurves,
                        BuiltInCategory.OST_Conduit,
                        BuiltInCategory.OST_CableTray,
                        BuiltInCategory.OST_DuctCurves,
                        BuiltInCategory.OST_PipeFitting,
                        BuiltInCategory.OST_ConduitFitting,
                        BuiltInCategory.OST_CableTrayFitting,
                        BuiltInCategory.OST_DuctFitting
                    };

                    foreach (var cat in mepCategories)
                    {
                        var elems = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType()
                            .ToElements();

                        foreach (var el in elems)
                        {
                            if (excludeIds.Contains(el.Id)) continue;

                            if (el.Location is LocationCurve lc && lc.Curve != null)
                            {
                                XYZ p0 = lc.Curve.GetEndPoint(0);
                                XYZ p1 = lc.Curve.GetEndPoint(1);
                                ctx.ElementSegments.Add(Tuple.Create(p0, p1));
                            }
                            else if (el.Location is LocationPoint lp && lp.Point != null)
                            {
                                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                                if (bb != null)
                                {
                                    ctx.ObstacleBoxes.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                                }
                            }
                        }
                    }
                }
                catch { }

                return ctx;
            }

            public void AddPlacedTag(XYZ bendPt, XYZ endPt)
            {
                PlacedTagShoulders.Add(Tuple.Create(bendPt, endPt));
                PlacedTagEndPoints.Add(endPt);
                ObstacleBoxes.Add(new Box2D(
                    Math.Min(bendPt.X, endPt.X) - 0.2,
                    Math.Max(bendPt.X, endPt.X) + 0.2,
                    bendPt.Y - 0.2,
                    bendPt.Y + 0.6
                ));
            }
        }

        private static void CalcularPuntosDirectrizSinColision(
            Element el,
            XYZ puntoBase,
            XYZ dirCurva,
            SpotCollisionContext collisionCtx,
            out XYZ bendPt,
            out XYZ endPt)
        {
            bool esHorizontal = Math.Abs(dirCurva.X) >= Math.Abs(dirCurva.Y);
            double shoulderLen = 1.2; // Longitud del hombro horizontal

            // Lista de combinaciones de candidatos: (dx, dy, dirX_hombro)
            List<Tuple<double, double, double>> candidatos = new List<Tuple<double, double, double>>();

            // Distancias de alejamiento (cercano, medio, extendido para zonas densas con cotas/tags)
            double[] distancias = new double[] { 1.2, 1.8, 2.5, 3.2, 4.0 };

            if (esHorizontal)
            {
                // Para tramos horizontales en planta:
                // 1. Arriba (+Y), hombro a la derecha (+1) con directriz vertical exacta (dx = 0)
                foreach (double dy in distancias)
                {
                    candidatos.Add(Tuple.Create(0.0, dy, 1.0));    // Directo arriba, hombro derecha (Prioridad 1)
                }

                // 2. Abajo (-Y), hombro a la derecha (+1) con directriz vertical exacta (dx = 0)
                foreach (double dy in distancias)
                {
                    candidatos.Add(Tuple.Create(0.0, -dy, 1.0));   // Directo abajo, hombro derecha
                }

                // 3. Opciones con hombro a la izquierda si el espacio a la derecha estuviera obstruido
                foreach (double dy in distancias)
                {
                    candidatos.Add(Tuple.Create(0.0, dy, -1.0));   // Directo arriba, hombro izquierda
                    candidatos.Add(Tuple.Create(0.0, -dy, -1.0));  // Directo abajo, hombro izquierda
                }

                // 4. Desplazamientos diagonales solo si no cabe directamente vertical
                foreach (double dy in distancias)
                {
                    candidatos.Add(Tuple.Create(1.2, dy, 1.0));
                    candidatos.Add(Tuple.Create(-1.2, dy, -1.0));
                    candidatos.Add(Tuple.Create(1.2, -dy, 1.0));
                    candidatos.Add(Tuple.Create(-1.2, -dy, -1.0));
                }
            }
            else
            {
                // Para tramos verticales en planta (recorrido en Y):
                // 1. Derecha (+X), hombro hacia la derecha (+1)
                foreach (double dx in distancias)
                {
                    candidatos.Add(Tuple.Create(dx, 0.0, 1.0));    // Directo derecha, hombro derecha
                }

                // 2. Izquierda (-X), hombro hacia la izquierda (-1)
                foreach (double dx in distancias)
                {
                    candidatos.Add(Tuple.Create(-dx, 0.0, -1.0));  // Directo izquierda, hombro izquierda
                }

                // 3. Alternativas
                foreach (double dx in distancias)
                {
                    candidatos.Add(Tuple.Create(dx, 0.0, -1.0));
                    candidatos.Add(Tuple.Create(-dx, 0.0, 1.0));
                }
            }

            XYZ mejorBend = null;
            XYZ mejorEnd = null;
            double menorPenalidad = double.MaxValue;

            foreach (var cand in candidatos)
            {
                double dx = cand.Item1;
                double dy = cand.Item2;
                double dirHombro = cand.Item3;

                XYZ testBend = new XYZ(puntoBase.X + dx, puntoBase.Y + dy, puntoBase.Z);
                XYZ testEnd = new XYZ(testBend.X + dirHombro * shoulderLen, testBend.Y, testBend.Z);

                double penalidad = EvaluarPenalidadColision(puntoBase, testBend, testEnd, el, collisionCtx);

                if (penalidad < menorPenalidad)
                {
                    menorPenalidad = penalidad;
                    mejorBend = testBend;
                    mejorEnd = testEnd;

                    // Si encontramos una posición perfectamente limpia (0 colisiones y distancia compacta)
                    if (penalidad < 100.0)
                    {
                        break;
                    }
                }
            }

            if (mejorBend != null && mejorEnd != null)
            {
                bendPt = mejorBend;
                endPt = mejorEnd;
            }
            else
            {
                // Fallback por defecto si no hubo selección
                if (esHorizontal)
                {
                    bendPt = new XYZ(puntoBase.X, puntoBase.Y + 1.4, puntoBase.Z);
                    endPt = new XYZ(bendPt.X + shoulderLen, bendPt.Y, bendPt.Z);
                }
                else
                {
                    bendPt = new XYZ(puntoBase.X + 1.4, puntoBase.Y, puntoBase.Z);
                    endPt = new XYZ(bendPt.X + shoulderLen, bendPt.Y, bendPt.Z);
                }
            }
        }

        private static double EvaluarPenalidadColision(
            XYZ pBase, XYZ bendPt, XYZ endPt, Element elActual, SpotCollisionContext ctx)
        {
            double dist = Math.Sqrt(Math.Pow(bendPt.X - pBase.X, 2) + Math.Pow(bendPt.Y - pBase.Y, 2));
            double penalidad = dist * 2.0; // Preferir menor distancia si está libre de interferencias

            if (ctx == null) return penalidad;

            double minX = Math.Min(bendPt.X, endPt.X) - 0.15;
            double maxX = Math.Max(bendPt.X, endPt.X) + 0.15;
            double minY = bendPt.Y - 0.15;
            double maxY = bendPt.Y + 0.55; // Altura ocupada por el texto sobre el hombro

            Box2D boxCota = new Box2D(minX, maxX, minY, maxY);

            // 1. Verificar colisión contra cajas de obstáculos (Tags de otras categorías, Cotas de dimensiones, Cajas eléctricas, Aparatos, etc.)
            foreach (var obs in ctx.ObstacleBoxes)
            {
                if (boxCota.Intersects(obs, margin: 0.15))
                {
                    penalidad += 10000.0;
                }
                else if (SegmentoIntersecaCaja2D(pBase, bendPt, obs.MinX, obs.MaxX, obs.MinY, obs.MaxY))
                {
                    // La directriz inclinada cruza sobre un tag o cota existente
                    penalidad += 4000.0;
                }
            }

            // 2. Verificar colisión contra hombros y extremos de cotas ya colocadas
            foreach (var pEnd in ctx.PlacedTagEndPoints)
            {
                if (Math.Abs(pEnd.X - endPt.X) < 1.6 && Math.Abs(pEnd.Y - endPt.Y) < 0.65)
                {
                    penalidad += 10000.0;
                }
            }

            foreach (var shoulder in ctx.PlacedTagShoulders)
            {
                double sMinX = Math.Min(shoulder.Item1.X, shoulder.Item2.X) - 0.15;
                double sMaxX = Math.Max(shoulder.Item1.X, shoulder.Item2.X) + 0.15;
                double sY = shoulder.Item1.Y;

                if (Math.Abs(sY - bendPt.Y) < 0.55 && !(maxX < sMinX || minX > sMaxX))
                {
                    penalidad += 10000.0;
                }
            }

            // 3. Verificar que la línea del hombro o directriz no cruce otros elementos MEP
            foreach (var seg in ctx.ElementSegments)
            {
                if (elActual != null && elActual.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ cp0 = lc.Curve.GetEndPoint(0);
                    XYZ cp1 = lc.Curve.GetEndPoint(1);
                    if ((seg.Item1.IsAlmostEqualTo(cp0) && seg.Item2.IsAlmostEqualTo(cp1)) ||
                        (seg.Item1.IsAlmostEqualTo(cp1) && seg.Item2.IsAlmostEqualTo(cp0)))
                    {
                        continue;
                    }
                }

                if (SegmentoIntersecaCaja2D(seg.Item1, seg.Item2, minX, maxX, minY, maxY))
                {
                    penalidad += 5000.0;
                }
            }

            return penalidad;
        }

        private static bool SegmentoIntersecaCaja2D(XYZ p0, XYZ p1, double minX, double maxX, double minY, double maxY)
        {
            double padMinX = minX - 0.15;
            double padMaxX = maxX + 0.15;
            double padMinY = minY - 0.15;
            double padMaxY = maxY + 0.15;

            // Si algún extremo de la tubería/elemento cae dentro de la zona de la cota
            if (p0.X >= padMinX && p0.X <= padMaxX && p0.Y >= padMinY && p0.Y <= padMaxY) return true;
            if (p1.X >= padMinX && p1.X <= padMaxX && p1.Y >= padMinY && p1.Y <= padMaxY) return true;

            // Verificación de solapamiento de rangos de coordenadas
            double sMinX = Math.Min(p0.X, p1.X);
            double sMaxX = Math.Max(p0.X, p1.X);
            double sMinY = Math.Min(p0.Y, p1.Y);
            double sMaxY = Math.Max(p0.Y, p1.Y);

            if (sMaxX < padMinX || sMinX > padMaxX || sMaxY < padMinY || sMinY > padMaxY)
            {
                return false;
            }

            return LineaIntersecaRectangulo2D(p0, p1, padMinX, padMaxX, padMinY, padMaxY);
        }

        private static bool LineaIntersecaRectangulo2D(XYZ p0, XYZ p1, double minX, double maxX, double minY, double maxY)
        {
            XYZ c1 = new XYZ(minX, minY, 0);
            XYZ c2 = new XYZ(maxX, minY, 0);
            XYZ c3 = new XYZ(maxX, maxY, 0);
            XYZ c4 = new XYZ(minX, maxY, 0);

            return LineasIntersecan2D(p0, p1, c1, c2) ||
                   LineasIntersecan2D(p0, p1, c2, c3) ||
                   LineasIntersecan2D(p0, p1, c3, c4) ||
                   LineasIntersecan2D(p0, p1, c4, c1);
        }

        private static bool LineasIntersecan2D(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            double d = (b2.Y - b1.Y) * (a2.X - a1.X) - (b2.X - b1.X) * (a2.Y - a1.Y);
            if (Math.Abs(d) < 1e-7) return false;

            double ua = ((b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X)) / d;
            double ub = ((a2.X - a1.X) * (a1.Y - b1.Y) - (a2.Y - a1.Y) * (a1.X - b1.X)) / d;

            return (ua >= 0.0 && ua <= 1.0 && ub >= 0.0 && ub <= 1.0);
        }

        #endregion

        /// <summary>
        /// Busca la referencia válida de la cara superior de un tubo/conducto y calcula el punto exacto sobre la cara
        /// correspondiente a la mitad del tubo proyectada verticalmente.
        /// </summary>
        private static bool ObtenerReferenciaCaraSuperiorYPoint(
            Element el,
            Autodesk.Revit.DB.View view,
            XYZ puntoMedioEje,
            out Reference refCara,
            out XYZ puntoEnCara)
        {
            refCara = null;
            puntoEnCara = null;

            try
            {
                // Intento 1: Obtener geometría asociada a la vista
                if (BuscarCaraSuperiorEnGeometria(el, view, puntoMedioEje, true, out refCara, out puntoEnCara))
                {
                    return true;
                }

                // Intento 2: Obtener geometría en nivel Fine independiente de la vista (fallback si la vista no generó sólidos)
                if (BuscarCaraSuperiorEnGeometria(el, view, puntoMedioEje, false, out refCara, out puntoEnCara))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool BuscarCaraSuperiorEnGeometria(
            Element el,
            Autodesk.Revit.DB.View view,
            XYZ puntoMedioEje,
            bool usarVista,
            out Reference refCara,
            out XYZ puntoEnCara)
        {
            refCara = null;
            puntoEnCara = null;

            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true
            };

            if (usarVista && view != null)
            {
                opt.View = view;
            }
            else
            {
                opt.DetailLevel = ViewDetailLevel.Fine;
            }

            GeometryElement geom = el.get_Geometry(opt);
            if (geom == null) return false;

            // Punto de prueba 2 pies (~60cm) verticalmente por encima del punto medio del eje
            XYZ testPoint = new XYZ(puntoMedioEje.X, puntoMedioEje.Y, puntoMedioEje.Z + 2.0);

            Reference mejorRef = null;
            XYZ mejorPt = null;
            double mayorZ = double.MinValue;

            List<Solid> solidos = new List<Solid>();

            void ExtraerSolidos(GeometryElement gElem)
            {
                foreach (GeometryObject gObj in gElem)
                {
                    if (gObj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-6)
                    {
                        solidos.Add(solid);
                    }
                    else if (gObj is GeometryInstance gInst)
                    {
                        GeometryElement instGeom = gInst.GetInstanceGeometry();
                        if (instGeom != null) ExtraerSolidos(instGeom);

                        GeometryElement symGeom = gInst.GetSymbolGeometry();
                        if (symGeom != null) ExtraerSolidos(symGeom);
                    }
                }
            }

            ExtraerSolidos(geom);

            foreach (Solid solid in solidos)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face.Reference == null) continue;

                    IntersectionResult ir = null;
                    try
                    {
                        ir = face.Project(testPoint);
                    }
                    catch { }

                    if (ir != null && ir.XYZPoint != null)
                    {
                        XYZ pt = ir.XYZPoint;

                        // Debe estar próximo al punto medio en el plano XY (tolerancia hasta 1.0 pie)
                        double distXY = Math.Sqrt(Math.Pow(pt.X - puntoMedioEje.X, 2) + Math.Pow(pt.Y - puntoMedioEje.Y, 2));
                        if (distXY > 1.0) continue;

                        // Debe estar en la mitad superior del tubo
                        if (pt.Z < puntoMedioEje.Z - 0.01) continue;

                        // Verificar que la normal en ese punto apunte hacia arriba (cara superior)
                        try
                        {
                            XYZ normal = face.ComputeNormal(ir.UVPoint);
                            if (normal.Z < -0.1) continue;
                        }
                        catch { }

                        if (pt.Z > mayorZ)
                        {
                            mayorZ = pt.Z;
                            mejorRef = face.Reference;
                            mejorPt = pt;
                        }
                    }
                }
            }

            if (mejorRef != null && mejorPt != null)
            {
                refCara = mejorRef;
                puntoEnCara = mejorPt;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Configura los parámetros clave de la cota SpotDimension:
        /// 1. Relative Base: Asigna el nivel de techo/losa correspondiente a la vista activa (ej: Nivel 04 para vista N03).
        /// 2. Display Elevations: Establece 'Bottom Elevation' (valor entero 2).
        /// 3. Activa directriz (Leader) y hombro horizontal (Leader Shoulder).
        /// </summary>
        private static void ConfigurarSpotElevation(SpotDimension spot, Level nivelTecho, Document doc, OpcionNivelReferencia opcionNivel = null)
        {
            if (spot == null) return;

            // 1. Asignar Relative Base (Nivel del techo/losa o nivel manual)
            if (nivelTecho != null)
            {
                try
                {
                    Parameter pBase = spot.LookupParameter("Relative Base")
                                   ?? spot.LookupParameter("Base relativa")
                                   ?? spot.LookupParameter("Nivel relativo")
                                   ?? spot.LookupParameter("Base Relativa")
                                   ?? spot.LookupParameter("Nivel Relativo")
                                   ?? spot.LookupParameter("Reference Level");

                    if (pBase != null && !pBase.IsReadOnly && pBase.StorageType == StorageType.ElementId)
                    {
                        pBase.Set(nivelTecho.Id);
                    }
                    else
                    {
                        foreach (Parameter p in spot.Parameters)
                        {
                            if (!p.IsReadOnly && p.StorageType == StorageType.ElementId)
                            {
                                string def = p.Definition.Name.ToLower();
                                if (def.Contains("relative") || def.Contains("base"))
                                {
                                    p.Set(nivelTecho.Id);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error al asignar Relative Base: " + ex.Message);
                }
            }

            // 2. Asignar Display Elevations (por defecto Bottom Elevation = 2 o la elegida en opcionNivel)
            try
            {
                Parameter pDisplay = spot.get_Parameter(BuiltInParameter.SPOT_ELEV_DISPLAY_ELEVATIONS)
                                  ?? spot.LookupParameter("Display Elevations")
                                  ?? spot.LookupParameter("Mostrar elevaciones");

                if (pDisplay != null && !pDisplay.IsReadOnly)
                {
                    int valDisplay = (opcionNivel != null) ? opcionNivel.DisplayElevationsValue : 2;
                    pDisplay.Set(valDisplay);

                    try
                    {
                        string strDisplay = "Bottom Elevation";
                        if (opcionNivel != null)
                        {
                            switch (opcionNivel.DisplayElevation)
                            {
                                case DisplayElevationType.ActualElevation:
                                    strDisplay = "Actual (Selected) Elevation";
                                    break;
                                case DisplayElevationType.TopElevation:
                                    strDisplay = "Top Elevation";
                                    break;
                                case DisplayElevationType.BottomElevation:
                                    strDisplay = "Bottom Elevation";
                                    break;
                                case DisplayElevationType.TopAndBottomElevation:
                                    strDisplay = "Top & Bottom Elevations";
                                    break;
                            }
                        }
                        pDisplay.SetValueString(strDisplay);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al asignar Display Elevations: " + ex.Message);
            }

            // 3. Asegurar directriz (Leader) y hombro (Leader Shoulder)
            try
            {
                Parameter pLeader = spot.LookupParameter("Leader") ?? spot.LookupParameter("Directriz");
                if (pLeader != null && !pLeader.IsReadOnly && pLeader.StorageType == StorageType.Integer)
                {
                    pLeader.Set(1);
                }

                Parameter pShoulder = spot.LookupParameter("Leader Shoulder") ?? spot.LookupParameter("Hombro de directriz");
                if (pShoulder != null && !pShoulder.IsReadOnly && pShoulder.StorageType == StorageType.Integer)
                {
                    pShoulder.Set(1);
                }
            }
            catch { }

            // 4. Ajustar el desfase del texto (Text Offset) para pegarlo a la línea horizontal
            try
            {
                ElementType spotType = doc.GetElement(spot.GetTypeId()) as ElementType;
                AjustarDesfaseDeTexto(spotType, 0.35 / 304.8);
            }
            catch { }

            try
            {
                doc.Regenerate();
            }
            catch { }
        }

        /// <summary>
        /// Ajusta dinámicamente el parámetro 'Text Offset' / 'Desfase de texto' sin depender de enums específicos de versión.
        /// </summary>
        private static void AjustarDesfaseDeTexto(ElementType spotType, double offsetValueFt)
        {
            if (spotType == null) return;
            try
            {
                Parameter pTextOffset = spotType.LookupParameter("Text Offset")
                                     ?? spotType.LookupParameter("Desfase de texto")
                                     ?? spotType.LookupParameter("Desfase del texto")
                                     ?? spotType.LookupParameter("Offset de texto");

                if (pTextOffset == null)
                {
                    foreach (Parameter p in spotType.Parameters)
                    {
                        if (!p.IsReadOnly && p.StorageType == StorageType.Double)
                        {
                            string name = p.Definition?.Name?.ToLower() ?? "";
                            if ((name.Contains("text") || name.Contains("texto")) &&
                                (name.Contains("offset") || name.Contains("desfase") || name.Contains("dist")))
                            {
                                pTextOffset = p;
                                break;
                            }
                        }
                    }
                }

                if (pTextOffset != null && !pTextOffset.IsReadOnly)
                {
                    pTextOffset.Set(offsetValueFt);
                }
            }
            catch { }
        }

        /// <summary>
        /// Obtiene el nivel de referencia general elegido por el usuario (Nivel Superior o Nivel Inferior).
        /// Lee la vista activa donde está parado el usuario para determinar con precisión el piso (GenLevel)
        /// o el techo (nivel inmediatamente superior en Z).
        /// </summary>
        private static Level ObtenerNivelReferencia(Document doc, Autodesk.Revit.DB.View view, Element el, XYZ puntoEje, OpcionNivelReferencia opcion = null)
        {
            try
            {
                var todosNiveles = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (todosNiveles.Count == 0) return null;

                // 1. Obtener nivel base de la vista activa donde está parado el usuario
                Level nivelVistaActiva = view?.GenLevel;

                // Determinar la coordenada Z real de la tubería / conducto
                double? zElemento = null;
                if (puntoEje != null)
                {
                    zElemento = puntoEje.Z;
                }
                else if (el != null)
                {
                    if (el.Location is LocationCurve lc && lc.Curve != null)
                    {
                        zElemento = 0.5 * (lc.Curve.GetEndPoint(0).Z + lc.Curve.GetEndPoint(1).Z);
                    }
                    else if (el.Location is LocationPoint lp)
                    {
                        zElemento = lp.Point.Z;
                    }
                    else
                    {
                        BoundingBoxXYZ bbox = el.get_BoundingBox(view) ?? el.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            zElemento = 0.5 * (bbox.Min.Z + bbox.Max.Z);
                        }
                    }
                }

                // 0. Modo Nivel Manual: si el usuario seleccionó un nivel específico del proyecto
                if (opcion != null && opcion.Modo == ModoNivelReferencia.Manual)
                {
                    if (opcion.NivelManual != null)
                    {
                        return opcion.NivelManual;
                    }
                    if (!string.IsNullOrWhiteSpace(opcion.NombreNivelManual))
                    {
                        var matchManual = todosNiveles.FirstOrDefault(l => l.Name.Equals(opcion.NombreNivelManual, StringComparison.OrdinalIgnoreCase));
                        if (matchManual != null) return matchManual;
                    }
                }

                bool esInferior = opcion != null && opcion.Modo == ModoNivelReferencia.Inferior;

                if (esInferior)
                {
                    // Modo Nivel Inferior: Usar prioritariamente el nivel de la vista activa donde está parado el usuario
                    if (nivelVistaActiva != null)
                    {
                        return nivelVistaActiva;
                    }

                    // Si la vista no tiene GenLevel, buscar el nivel inmediatamente inferior en Z
                    if (zElemento.HasValue)
                    {
                        var nivelInferior = todosNiveles
                            .OrderByDescending(l => l.Elevation)
                            .FirstOrDefault(l => l.Elevation < zElemento.Value - 0.05);

                        if (nivelInferior != null)
                        {
                            return nivelInferior;
                        }
                    }

                    return todosNiveles.FirstOrDefault();
                }
                else
                {
                    // Modo Nivel Superior (Techo/Losa): Buscar el nivel inmediatamente superior al nivel de la vista activa
                    if (nivelVistaActiva != null)
                    {
                        var supDeVista = todosNiveles
                            .FirstOrDefault(l => l.Elevation > nivelVistaActiva.Elevation + 0.05);

                        if (supDeVista != null)
                        {
                            return supDeVista;
                        }
                    }

                    // Si no hay nivel de vista o es el más alto, buscar el nivel superior en Z al elemento
                    if (zElemento.HasValue)
                    {
                        var nivelInmediatamenteSuperior = todosNiveles
                            .FirstOrDefault(l => l.Elevation > zElemento.Value + 0.05);

                        if (nivelInmediatamenteSuperior != null)
                        {
                            return nivelInmediatamenteSuperior;
                        }
                    }

                    return todosNiveles.LastOrDefault();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Wrapper de compatibilidad para obtener el nivel de techo.
        /// </summary>
        private static Level ObtenerNivelTecho(Document doc, Autodesk.Revit.DB.View view, Element el, XYZ puntoEje)
        {
            return ObtenerNivelReferencia(doc, view, el, puntoEje, null);
        }

        /// <summary>
        /// Localiza el tipo de SpotDimension correspondiente a 'DC - Elevacion relativa'.
        /// Si no existe en el proyecto, duplica y configura automáticamente un tipo base existente.
        /// </summary>
        private static ElementId ObtenerTipoSpotElevation(Document doc)
        {
            try
            {
                // 1. Buscar en SpotDimensionType existentes
                var spotTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpotDimensionType))
                    .Cast<SpotDimensionType>()
                    .ToList();

                var objetivo = spotTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("DC - Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC - Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? spotTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? spotTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0);

                if (objetivo != null)
                {
                    try
                    {
                        if (doc.IsModifiable)
                        {
                            AjustarDesfaseDeTexto(objetivo, 0.35 / 304.8);
                        }
                    }
                    catch { }
                    return objetivo.Id;
                }

                // 2. Si no existe, duplicar un tipo existente y crearlo automáticamente como "DC - Elevacion relativa"
                SpotDimensionType baseType = spotTypes.FirstOrDefault();
                if (baseType != null)
                {
                    SpotDimensionType nuevoTipo = null;
                    if (doc.IsModifiable)
                    {
                        try
                        {
                            nuevoTipo = baseType.Duplicate("DC - Elevacion relativa") as SpotDimensionType;
                            if (nuevoTipo != null)
                            {
                                AjustarDesfaseDeTexto(nuevoTipo, 0.35 / 304.8);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        using (Transaction tx = new Transaction(doc, "Crear tipo Spot Elevation DC"))
                        {
                            tx.Start();
                            try
                            {
                                nuevoTipo = baseType.Duplicate("DC - Elevacion relativa") as SpotDimensionType;
                                if (nuevoTipo != null)
                                {
                                    AjustarDesfaseDeTexto(nuevoTipo, 0.35 / 304.8);
                                }
                            }
                            catch { }
                            tx.Commit();
                        }
                    }

                    if (nuevoTipo != null)
                    {
                        return nuevoTipo.Id;
                    }
                }

                // 3. Fallback en DimensionType
                var dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => t.Category != null && t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_SpotElevations)
                    .ToList();

                var objetivoDim = dimTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("DC - Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC - Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? dimTypes.FirstOrDefault();

                if (objetivoDim != null)
                {
                    return objetivoDim.Id;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Proyecta el punto del clic sobre el eje de la curva del elemento.
        /// Garantiza un punto válido en 3D incluso si el punto de clic es nulo (selección por recuadro o preselección).
        /// </summary>
        private static XYZ ObtenerPuntoEjeDesdeClic(Element el, XYZ puntoClic)
        {
            try
            {
                if (puntoClic != null && el.Location is LocationCurve lc && lc.Curve != null)
                {
                    IntersectionResult ir = lc.Curve.Project(puntoClic);
                    if (ir != null)
                    {
                        return ir.XYZPoint;
                    }
                }
                if (el.Location is LocationPoint lp)
                {
                    return lp.Point;
                }
                if (el.Location is LocationCurve lc2 && lc2.Curve != null)
                {
                    return lc2.Curve.Evaluate(0.5, true);
                }
                if (el is FamilyInstance fi)
                {
                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    if (bb != null)
                    {
                        return (bb.Min + bb.Max) / 2.0;
                    }
                }
            }
            catch { }

            return puntoClic ?? (el.Location is LocationPoint lpFallback ? lpFallback.Point : XYZ.Zero);
        }

        /// <summary>
        /// Obtiene el punto medio en 3D y la dirección del elemento.
        /// </summary>
        private static XYZ ObtenerPuntoCentro(Element el, out XYZ dirCurva)
        {
            dirCurva = null;
            try
            {
                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    Curve curve = lc.Curve;
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);

                    XYZ vector = end - start;
                    if (!vector.IsZeroLength())
                    {
                        dirCurva = vector.Normalize();
                    }

                    return new XYZ(
                        (start.X + end.X) / 2.0,
                        (start.Y + end.Y) / 2.0,
                        (start.Z + end.Z) / 2.0
                    );
                }
                else if (el.Location is LocationPoint lp)
                {
                    return lp.Point;
                }
                else if (el is FamilyInstance fi)
                {
                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    if (bb != null)
                    {
                        return (bb.Min + bb.Max) / 2.0;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Valida si el elemento es una tubería, conduit o red MEP horizontal y sin pendiente.
        /// Requisitos del usuario:
        /// 1. Sin pendiente (dZ ≈ 0, diferencia de Z entre inicio y fin < 1 mm y pendiente = 0).
        /// 2. Orientación horizontal en planta (a lo largo del eje X en la vista, dX >> dY).
        /// </summary>
        public static bool EsTuberiaHorizontalSinPendiente(Element el, out XYZ dirCurva, out XYZ puntoCentro)
        {
            dirCurva = null;
            puntoCentro = null;

            if (el == null) return false;

            if (!(el.Location is LocationCurve lc) || lc.Curve == null)
            {
                return false;
            }

            Curve curve = lc.Curve;
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);

            double dX = Math.Abs(p1.X - p0.X);
            double dY = Math.Abs(p1.Y - p0.Y);
            double dZ = Math.Abs(p1.Z - p0.Z);
            double longitud = curve.Length; // en pies

            // Longitud mínima para ignorar niples diminutos entre accesorios pegados (< 12 cm)
            if (longitud < 0.4)
            {
                return false;
            }

            // 1. FILTRO SIN PENDIENTE: dZ debe ser prácticamente 0 (tubo nivelado en el espacio, no montante vertical Z)
            if (dZ > 0.02)
            {
                return false;
            }

            // Verificar parámetro de pendiente si aplica
            Parameter paramSlope = el.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
            if (paramSlope != null && paramSlope.HasValue)
            {
                if (Math.Abs(paramSlope.AsDouble()) > 0.0001)
                {
                    return false;
                }
            }

            // 2. FILTRO DE TENDIDO EN PLANTA: debe recorrer en el plano de planta (XY)
            double desplazamientoHorizontal = Math.Sqrt(dX * dX + dY * dY);
            if (desplazamientoHorizontal < 0.35)
            {
                return false;
            }

            XYZ vec = p1 - p0;
            if (!vec.IsZeroLength())
            {
                dirCurva = vec.Normalize();
            }
            puntoCentro = (p0 + p1) / 2.0;

            return true;
        }

        private class MepElementSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                int catId = e.Category.Id.IntegerValue;

                return catId == (int)BuiltInCategory.OST_PipeCurves
                    || catId == (int)BuiltInCategory.OST_Conduit
                    || catId == (int)BuiltInCategory.OST_CableTray
                    || catId == (int)BuiltInCategory.OST_DuctCurves;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        #region 4. CAMAS DE CONDUITS (REDES SECAS)

        public static int TaguearCamasConduitsPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> conduits = new List<Element>();
            var filter = new ConduitSelectionFilter();

            // 1. Revisar si el usuario ya tenía conduits preseleccionados en la vista
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element e = doc.GetElement(id);
                    if (e != null && filter.AllowElement(e))
                    {
                        conduits.Add(e);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (conduits.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los conduits de las camas a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element e = doc.GetElement(r.ElementId);
                            if (e != null && filter.AllowElement(e) && !conduits.Any(x => x.Id == e.Id))
                            {
                                conduits.Add(e);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (conduits.Count == 0) return 0;

            return ProcesarYColocarTagsCamasConduits(doc, view, conduits);
        }

        public static int TaguearCamasConduitsTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            var pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            elements.AddRange(pipes);

            if (elements.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Camas Conduits", "No se encontraron conduits o tuberías en la vista activa.");
                return 0;
            }

            return ProcesarYColocarTagsCamasConduits(doc, view, elements);
        }

        private class ConduitInfo
        {
            public Element Element;
            public ElementId Id => Element.Id;
            public XYZ P0;
            public XYZ P1;
            public XYZ Dir2D;
            public XYZ MidPoint;
            public double Length2D;
            public double ElevationZ;
            public double OuterDiameter;
            public string DiamText;
            public string MatAbbr;
        }

        private class ConduitCama
        {
            public double ElevationZ;
            public List<ConduitInfo> Conduits = new List<ConduitInfo>();
        }

        private class ConduitBank
        {
            public XYZ Direction2D;
            public XYZ Perp2D;
            public XYZ Center2D;
            public List<ConduitCama> Camas = new List<ConduitCama>();
            public List<ConduitInfo> AllConduits = new List<ConduitInfo>();
        }

        private static int ProcesarYColocarTagsCamasConduits(Document doc, Autodesk.Revit.DB.View view, List<Element> conduits)
        {
            if (conduits == null || conduits.Count == 0) return 0;

            // 1. Extraer información geométrica y metadata de cada conduit/tubo
            List<ConduitInfo> listInfo = new List<ConduitInfo>();
            foreach (var c in conduits)
            {
                if (c == null) continue;
                if (c.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v3D = p1 - p0;
                    double len3D = v3D.GetLength();
                    if (len3D < 0.1) continue;

                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    double len2D = v2D.GetLength();
                    XYZ dir2D = (len2D >= 0.15) ? (v2D / len2D) : XYZ.BasisX;

                    // Unificar orientación del vector director
                    if (dir2D.X < -1e-4 || (Math.Abs(dir2D.X) <= 1e-4 && dir2D.Y < -1e-4))
                    {
                        dir2D = -dir2D;
                    }

                    double outerDiam = 0.15; // default ~2 inches
                    string diamStr = "1\"ø";
                    string matAbbr = "EMT";

                    try
                    {
                        Parameter pDiam = c.get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM)
                                       ?? c.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                                       ?? c.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                       ?? c.LookupParameter("Diameter")
                                       ?? c.LookupParameter("Diámetro");
                        if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0)
                        {
                            outerDiam = pDiam.AsDouble();
                            double dInches = Math.Round(outerDiam * 12.0);
                            diamStr = $"{dInches}\"ø";
                        }
                    }
                    catch { }

                    try
                    {
                        Parameter pMat = c.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                      ?? c.LookupParameter("Material")
                                      ?? c.LookupParameter("Materialidad")
                                      ?? c.LookupParameter("Conduit Material")
                                      ?? c.LookupParameter("Pipe Material");
                        if (pMat != null && pMat.HasValue)
                        {
                            string rawMat = pMat.AsString() ?? string.Empty;
                            if (pMat.StorageType == StorageType.ElementId && pMat.AsElementId() != ElementId.InvalidElementId)
                            {
                                var matEl = doc.GetElement(pMat.AsElementId());
                                if (matEl != null) rawMat = matEl.Name;
                            }
                            if (!string.IsNullOrWhiteSpace(rawMat))
                            {
                                matAbbr = ObtenerAbreviaturaMaterial(rawMat);
                            }
                        }
                    }
                    catch { }

                    XYZ mid = (p0 + p1) * 0.5;
                    listInfo.Add(new ConduitInfo
                    {
                        Element = c,
                        P0 = p0,
                        P1 = p1,
                        Dir2D = dir2D,
                        MidPoint = mid,
                        Length2D = len2D >= 0.15 ? len2D : len3D,
                        ElevationZ = mid.Z,
                        OuterDiameter = outerDiam,
                        DiamText = diamStr,
                        MatAbbr = matAbbr
                    });
                }
            }

            if (listInfo.Count == 0) return 0;

            // 2. Agrupar conduits en Bancos / Racks paralelos
            List<ConduitBank> banks = new List<ConduitBank>();
            HashSet<ElementId> visitados = new HashSet<ElementId>();

            foreach (var c1 in listInfo)
            {
                if (visitados.Contains(c1.Id)) continue;

                List<ConduitInfo> cluster = new List<ConduitInfo> { c1 };
                visitados.Add(c1.Id);

                // Buscar otros conduits paralelos y contiguos
                bool agregado = true;
                while (agregado)
                {
                    agregado = false;
                    foreach (var c2 in listInfo)
                    {
                        if (visitados.Contains(c2.Id)) continue;

                        // Verificar si c2 es paralelo a c1 y contiguo a cualquiera del cluster
                        if (Math.Abs(c1.Dir2D.DotProduct(c2.Dir2D)) >= 0.90 || c1.MidPoint.DistanceTo(c2.MidPoint) <= 3.5)
                        {
                            bool esCercano = false;
                            XYZ perp = new XYZ(-c1.Dir2D.Y, c1.Dir2D.X, 0);

                            foreach (var member in cluster)
                            {
                                double distPerp = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(perp));
                                double distAlong = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(c1.Dir2D));
                                double dist3D = c2.MidPoint.DistanceTo(member.MidPoint);

                                // Separación lateral entre tubos del banco <= 3.0 ft
                                if (dist3D <= 3.5 || (distPerp <= 3.0 && distAlong <= (member.Length2D * 0.85 + c2.Length2D * 0.85)))
                                {
                                    esCercano = true;
                                    break;
                                }
                            }

                            if (esCercano)
                            {
                                cluster.Add(c2);
                                visitados.Add(c2.Id);
                                agregado = true;
                            }
                        }
                    }
                }

                // 3. Subdividir el banco en CAMAS según la elevación Z
                ConduitBank bank = new ConduitBank
                {
                    Direction2D = c1.Dir2D,
                    Perp2D = new XYZ(-c1.Dir2D.Y, c1.Dir2D.X, 0),
                    AllConduits = cluster
                };

                double sumX = 0, sumY = 0, sumZ = 0;
                foreach (var item in cluster)
                {
                    sumX += item.MidPoint.X;
                    sumY += item.MidPoint.Y;
                    sumZ += item.MidPoint.Z;
                }
                bank.Center2D = new XYZ(sumX / cluster.Count, sumY / cluster.Count, sumZ / cluster.Count);

                // Agrupar por niveles de Z (tolerancia 0.40 ft ≈ 12 cm)
                foreach (var c in cluster)
                {
                    var camaExistente = bank.Camas.FirstOrDefault(cm => Math.Abs(cm.ElevationZ - c.ElevationZ) <= 0.40);
                    if (camaExistente != null)
                    {
                        camaExistente.Conduits.Add(c);
                    }
                    else
                    {
                        var nuevaCama = new ConduitCama
                        {
                            ElevationZ = c.ElevationZ,
                            Conduits = new List<ConduitInfo> { c }
                        };
                        bank.Camas.Add(nuevaCama);
                    }
                }

                // Ordenar camas por elevación Z descendente (Cama superior primero)
                bank.Camas = bank.Camas.OrderByDescending(cm => cm.ElevationZ).ToList();
                banks.Add(bank);
            }

            if (banks.Count == 0) return 0;

            // 4. Crear etiquetas y asignar comentarios en transacción
            int totalTagsCreados = 0;
            using (Transaction tx = new Transaction(doc, "Taguear Camas de Conduits"))
            {
                tx.Start();

                double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
                double margenMm = (3.5 / 1000.0 * scale) * 3.28084;
                double gapEntreTags = (5.0 / 1000.0 * scale) * 3.28084;

                foreach (var bank in banks)
                {
                    int numCamas = bank.Camas.Count;

                    bool esVerticalEnPlanta = Math.Abs(bank.Direction2D.Y) > Math.Abs(bank.Direction2D.X);
                    TagOrientation orientacionTag = TagOrientation.Horizontal;

                    XYZ perp = bank.Perp2D;
                    if (esVerticalEnPlanta)
                    {
                        if (perp.X > 0.1) perp = -perp;
                    }
                    else
                    {
                        if (perp.Y < -0.1) perp = -perp;
                    }

                    double maxRadioBank = 0.0;
                    foreach (var c in bank.AllConduits)
                    {
                        double distPerp = (c.MidPoint - bank.Center2D).DotProduct(perp);
                        double r = c.OuterDiameter > 0 ? (c.OuterDiameter / 2.0) : 0.10;
                        if (Math.Abs(distPerp) + r > maxRadioBank)
                        {
                            maxRadioBank = Math.Abs(distPerp) + r;
                        }
                    }
                    if (maxRadioBank < 0.15) maxRadioBank = 0.15;

                    double offsetBase = maxRadioBank + margenMm;

                    for (int i = 0; i < numCamas; i++)
                    {
                        var cama = bank.Camas[i];
                        int countTubos = cama.Conduits.Count;
                        string valorComentario = countTubos > 1 ? $"({countTubos})-" : string.Empty;

                        // Asignar comentario y DC. TAG a todos los elementos de la cama
                        foreach (var cInfo in cama.Conduits)
                        {
                            try
                            {
                                Parameter pCom = cInfo.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                              ?? cInfo.Element.LookupParameter("Comentarios")
                                              ?? cInfo.Element.LookupParameter("Comments");
                                if (pCom != null && !pCom.IsReadOnly)
                                {
                                    pCom.Set(valorComentario);
                                }

                                Parameter pDcTag = cInfo.Element.LookupParameter("DC. TAG conduit accesorio")
                                                ?? cInfo.Element.LookupParameter("DC. TAG conduit")
                                                ?? cInfo.Element.LookupParameter("DC. TAG pipe")
                                                ?? cInfo.Element.LookupParameter("DC. TAG accesorio");
                                if (pDcTag != null && !pDcTag.IsReadOnly && pDcTag.StorageType == StorageType.String)
                                {
                                    pDcTag.Set(cInfo.MatAbbr);
                                }
                            }
                            catch { }
                        }

                        double offsetCama = offsetBase + (i * gapEntreTags);
                        XYZ tagHeadPos = bank.Center2D + (perp * offsetCama) + new XYZ(0.3, 0, 0);

                        ConduitInfo hostConduit = cama.Conduits.OrderBy(c => (c.MidPoint - tagHeadPos).GetLength()).FirstOrDefault() ?? cama.Conduits.First();

                        ElementId tagTypeId = ObtenerTipoTagCamasConduits(doc, hostConduit.Element);
                        bool tagCreado = false;

                        if (tagTypeId != ElementId.InvalidElementId)
                        {
                            var tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
                            if (tagSymbol != null && !tagSymbol.IsActive)
                            {
                                tagSymbol.Activate();
                                doc.Regenerate();
                            }

                            try
                            {
                                IndependentTag tag = IndependentTag.Create(
                                    doc,
                                    tagTypeId,
                                    view.Id,
                                    new Reference(hostConduit.Element),
                                    true, // con líder
                                    orientacionTag,
                                    tagHeadPos
                                );

                                if (tag != null)
                                {
                                    tag.HasLeader = true;
                                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                                    tag.TagHeadPosition = tagHeadPos;
                                    try { tag.Pinned = false; } catch { }
                                    tagCreado = true;
                                    totalTagsCreados++;
                                }
                            }
                            catch { }
                        }

                        // Fallback: Si no hay familia de etiqueta cargada, crear TextNote con líder visible
                        if (!tagCreado)
                        {
                            try
                            {
                                var textType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(TextNoteType))
                                    .Cast<TextNoteType>()
                                    .FirstOrDefault();

                                if (textType != null)
                                {
                                    TextNoteOptions tOpt = new TextNoteOptions(textType.Id)
                                    {
                                        HorizontalAlignment = HorizontalTextAlignment.Left
                                    };
                                    string textoCama = countTubos > 1
                                        ? $"({countTubos})- {hostConduit.DiamText} - {hostConduit.MatAbbr}"
                                        : $"{hostConduit.DiamText} - {hostConduit.MatAbbr}";

                                    var note = TextNote.Create(doc, view.Id, tagHeadPos, textoCama, tOpt);
                                    if (note != null)
                                    {
                                        try { note.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L); } catch { }
                                        totalTagsCreados++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                tx.Commit();
            }

            return totalTagsCreados;
        }

        private static ElementId ObtenerTipoTagCamasConduits(Document doc, Element hostElement)
        {
            try
            {
                BuiltInCategory catTag = BuiltInCategory.OST_ConduitTags;
                if (hostElement != null && hostElement.Category != null)
                {
#if REVIT2024_OR_LATER
                    long catId = hostElement.Category.Id.Value;
                    if (catId == (long)BuiltInCategory.OST_PipeCurves || catId == (long)BuiltInCategory.OST_PipeFitting || catId == (long)BuiltInCategory.OST_FlexPipeCurves)
                    {
                        catTag = BuiltInCategory.OST_PipeTags;
                    }
#else
                    int catId = hostElement.Category.Id.IntegerValue;
                    if (catId == (int)BuiltInCategory.OST_PipeCurves || catId == (int)BuiltInCategory.OST_PipeFitting || catId == (int)BuiltInCategory.OST_FlexPipeCurves)
                    {
                        catTag = BuiltInCategory.OST_PipeTags;
                    }
#endif
                }

                var tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(catTag)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                var multiTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                tagTypes.AddRange(multiTypes);

                if (tagTypes.Count > 0)
                {
                    // 1. Prioridad: Etiquetas específicas de camas/bancos o que combinen Comentario + Size + Tipo/Material/ETO
                    var camaType = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return n.IndexOf("Cama", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Bank", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               ((n.IndexOf("Coment", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                (n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                (n.IndexOf("ETO", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Mat", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Tipo", StringComparison.OrdinalIgnoreCase) >= 0));
                    });
                    if (camaType != null) return camaType.Id;

                    // 2. Prioridad: Etiquetas que incluyan Comentario + Diámetro/Size
                    var comentSizeType = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return (n.IndexOf("Coment", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               (n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (comentSizeType != null) return comentSizeType.Id;

                    // 3. Prioridad: "DC - Tag Conduit", "DC. TAG conduit", o "Tag Conduit" / "Tag Tubería"
                    var dcConduitType = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return (n.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Tuber", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (dcConduitType != null) return dcConduitType.Id;

                    return tagTypes.First().Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private static string ObtenerNombreFamiliaTag(ElementType t)
        {
            if (t == null) return string.Empty;
            try
            {
                if (t is FamilySymbol fs && fs.Family != null && !string.IsNullOrEmpty(fs.Family.Name))
                    return fs.Family.Name;

                Parameter p = t.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME) ?? t.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                if (p != null && p.HasValue) return p.AsString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        private class ConduitSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
#if REVIT2024_OR_LATER
                long catId = elem.Category.Id.Value;
                return catId == (long)BuiltInCategory.OST_Conduit ||
                       catId == (long)BuiltInCategory.OST_ConduitRun ||
                       catId == (long)BuiltInCategory.OST_PipeCurves ||
                       catId == (long)BuiltInCategory.OST_FlexPipeCurves ||
                       catId == (long)BuiltInCategory.OST_DuctCurves;
#else
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_Conduit ||
                       catId == (int)BuiltInCategory.OST_ConduitRun ||
                       catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_FlexPipeCurves ||
                       catId == (int)BuiltInCategory.OST_DuctCurves;
#endif
            }
            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        #endregion

        #region 5. CAMAS DE TUBERÍAS / SALIDAS DE TABLERO (REDES SECAS)

        /// <summary>
        /// Flujo por selección interactiva (con cursor +/- y botón Finish) para taguear y acotar Camas de Tuberías
        /// (formato: N.E. [Cota] / [N] TUBERÍAS / [Diámetro] - [Material] + cotas de espaciamiento).
        /// </summary>
        public static int TaguearCamasDeTubosPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> conduits = new List<Element>();
            var filter = new ConduitSelectionFilter();

            // 1. Revisar si el usuario ya tenía conduits preseleccionados en la vista
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element e = doc.GetElement(id);
                    if (e != null && filter.AllowElement(e))
                    {
                        conduits.Add(e);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (conduits.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los conduits de las camas de tuberías a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element e = doc.GetElement(r.ElementId);
                            if (e != null && filter.AllowElement(e) && !conduits.Any(x => x.Id == e.Id))
                            {
                                conduits.Add(e);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (conduits.Count == 0) return 0;

            return ProcesarYColocarTagsCamasDeTubos(doc, view, conduits);
        }

        /// <summary>
        /// Flujo automático para taguear todas las camas de tubos en la vista activa.
        /// </summary>
        public static int TaguearCamasDeTubosTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            var pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            elements.AddRange(pipes);

            if (elements.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Camas de Tuberías", "No se encontraron conduits o tuberías en la vista activa.");
                return 0;
            }

            return ProcesarYColocarTagsCamasDeTubos(doc, view, elements);
        }

        private static int ProcesarYColocarTagsCamasDeTubos(Document doc, Autodesk.Revit.DB.View view, List<Element> conduits)
        {
            if (conduits == null || conduits.Count == 0) return 0;

            // 1. Extraer información geométrica y metadata de cada conduit/tubo
            List<ConduitInfo> listInfo = new List<ConduitInfo>();
            foreach (var c in conduits)
            {
                if (c == null) continue;
                if (c.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v3D = p1 - p0;
                    double len3D = v3D.GetLength();
                    if (len3D < 0.1) continue;

                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    double len2D = v2D.GetLength();
                    XYZ dir2D = (len2D >= 0.15) ? (v2D / len2D) : XYZ.BasisX;

                    if (dir2D.X < -1e-4 || (Math.Abs(dir2D.X) <= 1e-4 && dir2D.Y < -1e-4))
                    {
                        dir2D = -dir2D;
                    }

                    double outerDiam = 0.15;
                    string diamStr = "1\"ø";
                    string matAbbr = "EMT";

                    try
                    {
                        Parameter pDiam = c.get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM)
                                       ?? c.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                                       ?? c.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                       ?? c.LookupParameter("Diameter")
                                       ?? c.LookupParameter("Diámetro");
                        if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0)
                        {
                            outerDiam = pDiam.AsDouble();
                            double dInches = Math.Round(outerDiam * 12.0);
                            diamStr = $"{dInches}\"ø";
                        }
                    }
                    catch { }

                    try
                    {
                        Parameter pMat = c.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                      ?? c.LookupParameter("Material")
                                      ?? c.LookupParameter("Materialidad")
                                      ?? c.LookupParameter("Conduit Material")
                                      ?? c.LookupParameter("Pipe Material");
                        if (pMat != null && pMat.HasValue)
                        {
                            string rawMat = pMat.AsString() ?? string.Empty;
                            if (pMat.StorageType == StorageType.ElementId && pMat.AsElementId() != ElementId.InvalidElementId)
                            {
                                var matEl = doc.GetElement(pMat.AsElementId());
                                if (matEl != null) rawMat = matEl.Name;
                            }
                            if (!string.IsNullOrWhiteSpace(rawMat))
                            {
                                matAbbr = ObtenerAbreviaturaMaterial(rawMat);
                            }
                        }
                    }
                    catch { }

                    XYZ mid = (p0 + p1) * 0.5;
                    listInfo.Add(new ConduitInfo
                    {
                        Element = c,
                        P0 = p0,
                        P1 = p1,
                        Dir2D = dir2D,
                        MidPoint = mid,
                        Length2D = len2D >= 0.15 ? len2D : len3D,
                        ElevationZ = mid.Z,
                        OuterDiameter = outerDiam,
                        DiamText = diamStr,
                        MatAbbr = matAbbr
                    });
                }
            }

            if (listInfo.Count == 0) return 0;

            // 2. Agrupar conduits en Bancos / Camas de Tubos
            List<ConduitBank> banks = new List<ConduitBank>();
            HashSet<ElementId> visitados = new HashSet<ElementId>();

            foreach (var c1 in listInfo)
            {
                if (visitados.Contains(c1.Id)) continue;

                List<ConduitInfo> cluster = new List<ConduitInfo> { c1 };
                visitados.Add(c1.Id);

                bool agregado = true;
                while (agregado)
                {
                    agregado = false;
                    foreach (var c2 in listInfo)
                    {
                        if (visitados.Contains(c2.Id)) continue;

                        if (Math.Abs(c1.Dir2D.DotProduct(c2.Dir2D)) >= 0.90 || c1.MidPoint.DistanceTo(c2.MidPoint) <= 3.5)
                        {
                            bool esCercano = false;
                            XYZ perp = new XYZ(-c1.Dir2D.Y, c1.Dir2D.X, 0);

                            foreach (var member in cluster)
                            {
                                double distPerp = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(perp));
                                double distAlong = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(c1.Dir2D));
                                double dist3D = c2.MidPoint.DistanceTo(member.MidPoint);

                                if (dist3D <= 3.5 || (distPerp <= 3.0 && distAlong <= (member.Length2D * 0.85 + c2.Length2D * 0.85)))
                                {
                                    esCercano = true;
                                    break;
                                }
                            }

                            if (esCercano)
                            {
                                cluster.Add(c2);
                                visitados.Add(c2.Id);
                                agregado = true;
                            }
                        }
                    }
                }

                ConduitBank bank = new ConduitBank
                {
                    Direction2D = c1.Dir2D,
                    Perp2D = new XYZ(-c1.Dir2D.Y, c1.Dir2D.X, 0),
                    AllConduits = cluster
                };

                double sumX = 0, sumY = 0, sumZ = 0;
                foreach (var item in cluster)
                {
                    sumX += item.MidPoint.X;
                    sumY += item.MidPoint.Y;
                    sumZ += item.MidPoint.Z;
                }
                bank.Center2D = new XYZ(sumX / cluster.Count, sumY / cluster.Count, sumZ / cluster.Count);

                foreach (var c in cluster)
                {
                    var camaExistente = bank.Camas.FirstOrDefault(cm => Math.Abs(cm.ElevationZ - c.ElevationZ) <= 0.40);
                    if (camaExistente != null)
                    {
                        camaExistente.Conduits.Add(c);
                    }
                    else
                    {
                        var nuevaCama = new ConduitCama
                        {
                            ElevationZ = c.ElevationZ,
                            Conduits = new List<ConduitInfo> { c }
                        };
                        bank.Camas.Add(nuevaCama);
                    }
                }

                bank.Camas = bank.Camas.OrderByDescending(cm => cm.ElevationZ).ToList();
                banks.Add(bank);
            }

            if (banks.Count == 0) return 0;

            // Obtener nivel de referencia de la vista para calcular cota N.E.
            Level nivelRef = null;
            if (view.GenLevel != null)
            {
                nivelRef = view.GenLevel;
            }
            else if (view.LevelId != ElementId.InvalidElementId)
            {
                nivelRef = doc.GetElement(view.LevelId) as Level;
            }

            double zNivelBase = nivelRef != null ? nivelRef.Elevation : 0.0;

            // 3. Crear etiquetas y asignar formato N.E. / N TUBERÍAS / Diámetro - Material
            int totalTagsCreados = 0;
            using (Transaction tx = new Transaction(doc, "Taguear Camas de Tuberías (Salidas Tablero)"))
            {
                tx.Start();

                double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
                double margenMm = (4.0 / 1000.0 * scale) * 3.28084;
                double gapEntreTags = (6.0 / 1000.0 * scale) * 3.28084;

                foreach (var bank in banks)
                {
                    int numCamas = bank.Camas.Count;

                    bool esVerticalEnPlanta = Math.Abs(bank.Direction2D.Y) > Math.Abs(bank.Direction2D.X);
                    TagOrientation orientacionTag = TagOrientation.Horizontal;

                    XYZ perp = bank.Perp2D;
                    if (esVerticalEnPlanta)
                    {
                        if (perp.X > 0.1) perp = -perp;
                    }
                    else
                    {
                        if (perp.Y < -0.1) perp = -perp;
                    }

                    double maxRadioBank = 0.0;
                    foreach (var c in bank.AllConduits)
                    {
                        double distPerp = (c.MidPoint - bank.Center2D).DotProduct(perp);
                        double r = c.OuterDiameter > 0 ? (c.OuterDiameter / 2.0) : 0.10;
                        if (Math.Abs(distPerp) + r > maxRadioBank)
                        {
                            maxRadioBank = Math.Abs(distPerp) + r;
                        }
                    }
                    if (maxRadioBank < 0.15) maxRadioBank = 0.15;

                    double offsetBase = maxRadioBank + margenMm;

                    for (int i = 0; i < numCamas; i++)
                    {
                        var cama = bank.Camas[i];
                        int countTubos = cama.Conduits.Count;
                        ConduitInfo hostConduit = cama.Conduits.First();

                        // Calcular delta Z relativo al nivel en metros
                        double deltaZFeet = cama.ElevationZ - zNivelBase;
                        double deltaZMeters = deltaZFeet * 0.3048;
                        string signo = deltaZMeters >= 0 ? "+" : "";
                        string cotaZTexto = $"{signo}{deltaZMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";

                        string cantTubosTexto = countTubos == 1 ? "1 TUBERÍA" : $"{countTubos} TUBERÍAS";
                        string textoCamaCompleto = $"N.E. {cotaZTexto} / {cantTubosTexto} / {hostConduit.DiamText} - {hostConduit.MatAbbr}";

                        // Asignar parámetros DC sin modificar Comments (que está reservado para Camas Conduits)
                        foreach (var cInfo in cama.Conduits)
                        {
                            try
                            {
                                Parameter pDcTag = cInfo.Element.LookupParameter("DC. TAG conduit accesorio")
                                                ?? cInfo.Element.LookupParameter("DC. TAG conduit")
                                                ?? cInfo.Element.LookupParameter("DC. TAG pipe")
                                                ?? cInfo.Element.LookupParameter("DC. TAG accesorio");
                                if (pDcTag != null && !pDcTag.IsReadOnly && pDcTag.StorageType == StorageType.String)
                                {
                                    pDcTag.Set(cInfo.MatAbbr);
                                }
                            }
                            catch { }
                        }

                        double offsetCama = offsetBase + (i * gapEntreTags);
                        XYZ tagHeadPos = bank.Center2D + (perp * offsetCama) + new XYZ(0.35, 0, 0);

                        ElementId specificTagId = ObtenerTipoTagCamasDeTubosEspecifico(doc);
                        bool tagCreado = false;

                        if (specificTagId != ElementId.InvalidElementId)
                        {
                            var tagSymbol = doc.GetElement(specificTagId) as FamilySymbol;
                            if (tagSymbol != null && !tagSymbol.IsActive)
                            {
                                tagSymbol.Activate();
                                doc.Regenerate();
                            }

                            try
                            {
                                IndependentTag tag = IndependentTag.Create(
                                    doc,
                                    specificTagId,
                                    view.Id,
                                    new Reference(hostConduit.Element),
                                    true,
                                    orientacionTag,
                                    tagHeadPos
                                );

                                if (tag != null)
                                {
                                    tag.HasLeader = true;
                                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                                    tag.TagHeadPosition = tagHeadPos;
                                    try { tag.Pinned = false; } catch { }
                                    tagCreado = true;
                                    totalTagsCreados++;
                                }
                            }
                            catch { }
                        }

                        // Anotación con Texto y Líder independiente (no colisiona con otros tags ni parámetros)
                        if (!tagCreado)
                        {
                            try
                            {
                                var textType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(TextNoteType))
                                    .Cast<TextNoteType>()
                                    .OrderBy(tt =>
                                    {
                                        string n = (tt.Name ?? "").ToLowerInvariant();
                                        if (n.Contains("2.0") || n.Contains("2.5") || n.Contains("arial") || n.Contains("dicta")) return 0;
                                        return 1;
                                    })
                                    .FirstOrDefault();

                                if (textType != null)
                                {
                                    TextNoteOptions tOpt = new TextNoteOptions(textType.Id)
                                    {
                                        HorizontalAlignment = HorizontalTextAlignment.Left
                                    };

                                    var note = TextNote.Create(doc, view.Id, tagHeadPos, textoCamaCompleto, tOpt);
                                    if (note != null)
                                    {
                                        try
                                        {
                                            var leaderType = tagHeadPos.X >= hostConduit.MidPoint.X
                                                ? TextNoteLeaderTypes.TNLT_STRAIGHT_L
                                                : TextNoteLeaderTypes.TNLT_STRAIGHT_R;

                                            Leader ldr = note.AddLeader(leaderType);
                                            if (ldr != null)
                                            {
                                                ldr.End = hostConduit.MidPoint;
                                                XYZ elbow = new XYZ((tagHeadPos.X + hostConduit.MidPoint.X) * 0.5, tagHeadPos.Y, hostConduit.MidPoint.Z);
                                                try { ldr.Elbow = elbow; } catch { }
                                            }
                                        }
                                        catch { }
                                        totalTagsCreados++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                tx.Commit();
            }

            return totalTagsCreados;
        }

        private static ElementId ObtenerTipoTagCamasDeTubosEspecifico(Document doc)
        {
            try
            {
                var tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ConduitTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                var multiTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                tagTypes.AddRange(multiTypes);

                var specific = tagTypes.FirstOrDefault(t =>
                {
                    string n = ((t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t)).ToLowerInvariant();
                    return (n.Contains("cama") && (n.Contains("tuberia") || n.Contains("tubería") || n.Contains("tubo"))) ||
                           n.Contains("salida tablero") ||
                           n.Contains("rack conduit");
                });

                if (specific != null) return specific.Id;
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        #endregion

        #region 6. COTAS DE BANDEJAS PORTACABLES (REDES SECAS)

        public class CableTraySelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
#if REVIT2024_OR_LATER
                long catId = elem.Category.Id.Value;
                return catId == (long)BuiltInCategory.OST_CableTray;
#else
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_CableTray;
#endif
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        /// <summary>
        /// Flujo por selección interactiva (con cursor +/- y botón Finish) para acotar bandejas portacables
        /// a la estructura más cercana (muros, vigas, columnas).
        /// </summary>
        public static int TaguearCotasBandejasPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> elementos = new List<Element>();
            var filter = new CableTraySelectionFilter();

            // 1. Revisar si el usuario ya tenía bandejas seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        elementos.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (elementos.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las bandejas portacables a acotar y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !elementos.Any(x => x.Id == el.Id))
                            {
                                elementos.Add(el);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (elementos.Count == 0) return 0;

            return ProcesarAcotadoBandejas(doc, view, elementos);
        }

        /// <summary>
        /// Flujo automático en la vista para acotar todas las bandejas portacables a estructuras más cercanas.
        /// </summary>
        public static int TaguearCotasBandejasTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> bandejas = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .ToList();

            if (bandejas.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Cotas de Bandejas", "No se encontraron bandejas portacables en la vista activa.");
                return 0;
            }

            return ProcesarAcotadoBandejas(doc, view, bandejas);
        }

        /// <summary>
        /// Procesa el acotado de bandejas a estructuras (muros, vigas, columnas) desde una sola cara exterior.
        /// </summary>
        private static int ProcesarAcotadoBandejas(Document doc, Autodesk.Revit.DB.View view, List<Element> elementos)
        {
            if (elementos == null || elementos.Count == 0) return 0;

            int cotasCreadas = 0;
            using (Transaction tx = new Transaction(doc, "Acotar Cotas de Bandejas Portacables"))
            {
                tx.Start();

                DimensionType dimType = ObtenerTipoCotaAlineada(doc);

                // Recolectar estructuras de soporte en la vista
                var muros = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .ToList();

                var vigas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                var columnas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToList();

                // Filtrar solo bandejas lineales
                var bandejas = elementos.Where(e =>
#if REVIT2024_OR_LATER
                    e.Category?.Id.Value == (long)BuiltInCategory.OST_CableTray
#else
                    e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_CableTray
#endif
                ).ToList();

                // Evitar cotas duplicadas: tramos colineales de una misma corrida hacia la misma estructura
                var clavesCreadas = new HashSet<string>();

                // 1. Acotar bandejas lineales desde una sola cara a la estructura más cercana (cálculo en planta XY)
                foreach (var tray in bandejas)
                {
                    if (!(tray.Location is LocationCurve lc) || lc.Curve == null) continue;

                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    if (v2D.GetLength() < 0.3) continue; // tramos verticales o muy cortos

                    XYZ dirTray = v2D.Normalize();
                    XYZ perp = new XYZ(-dirTray.Y, dirTray.X, 0);
                    double zTray = (p0.Z + p1.Z) * 0.5;
                    XYZ mid2D = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, 0);
                    XYZ a2D = new XYZ(p0.X, p0.Y, 0);
                    XYZ b2D = new XYZ(p1.X, p1.Y, 0);

                    // Ancho de bandeja para medir desde la cara exterior (no desde el eje)
                    double widthFt = 1.0;
                    Parameter pWidth = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)
                                    ?? tray.LookupParameter("Width")
                                    ?? tray.LookupParameter("Ancho");
                    if (pWidth != null && pWidth.HasValue)
                    {
                        widthFt = pWidth.AsDouble();
                    }
                    double halfWidth = widthFt * 0.5;

                    Reference refEstructura = null;
                    Element elemEstructura = null;
                    double menorDist = double.MaxValue;
                    double offsetEstructura = 0;   // distancia perpendicular firmada (eje bandeja -> eje estructura)
                    XYZ dirHaciaEstructura = null;

                    // Evalúa una estructura lineal paralela en planta
                    void Evaluar(Element est, Curve cEst, bool esMuro)
                    {
                        XYZ e0 = new XYZ(cEst.GetEndPoint(0).X, cEst.GetEndPoint(0).Y, 0);
                        XYZ e1 = new XYZ(cEst.GetEndPoint(1).X, cEst.GetEndPoint(1).Y, 0);
                        XYZ eV = e1 - e0;
                        double eLen = eV.GetLength();
                        if (eLen < 0.2) return;
                        XYZ eDir = eV / eLen;
                        if (Math.Abs(dirTray.DotProduct(eDir)) < 0.85) return;

                        // Traslape longitudinal: la bandeja debe correr junto a la estructura
                        double ta = (a2D - e0).DotProduct(eDir);
                        double tb = (b2D - e0).DotProduct(eDir);
                        double tMin = Math.Min(ta, tb), tMax = Math.Max(ta, tb);
                        if (tMax < 0.1 || tMin > eLen - 0.1) return;

                        // Distancia perpendicular en planta (sin componente Z)
                        double offset = (e0 - mid2D).DotProduct(perp);
                        double distCara = Math.Abs(offset) - halfWidth;
                        if (distCara <= 0.05 || distCara > 20.0 || distCara >= menorDist) return;

                        XYZ normal = perp * Math.Sign(offset);
                        Reference r = esMuro
                            ? ObtenerReferenciaCaraAcabadaMuro((Wall)est, view, normal)
                            : ObtenerReferenciaCaraElemento(est, view, normal);
                        if (r == null) return;

                        menorDist = distCara;
                        refEstructura = r;
                        elemEstructura = est;
                        offsetEstructura = offset;
                        dirHaciaEstructura = normal;
                    }

                    // A) Muros
                    foreach (var wall in muros)
                    {
                        if (wall.Location is LocationCurve wLc && wLc.Curve != null)
                            Evaluar(wall, wLc.Curve, true);
                    }

                    // B) Si no hay muro, Vigas
                    if (refEstructura == null)
                    {
                        foreach (var viga in vigas)
                        {
                            if (viga.Location is LocationCurve vLc && vLc.Curve != null)
                                Evaluar(viga, vLc.Curve, false);
                        }
                    }

                    if (refEstructura == null || elemEstructura == null) continue;

                    // Clave de corrida: misma estructura + misma línea de bandeja (offset del eje respecto al origen)
                    double lineaBandeja = new XYZ(mid2D.X, mid2D.Y, 0).DotProduct(perp) * Math.Sign(offsetEstructura);
                    string clave = $"{elemEstructura.Id}_{Math.Round(lineaBandeja / 0.05)}_{Math.Round(halfWidth / 0.02)}";
                    if (!clavesCreadas.Add(clave)) continue;

                    Reference refTray = ObtenerReferenciaCaraBandeja(tray, view, dirHaciaEstructura);
                    if (refTray == null) continue;

                    try
                    {
                        ReferenceArray refArray = new ReferenceArray();
                        refArray.Append(refEstructura);
                        refArray.Append(refTray);

                        // Línea de cota horizontal, a la cota Z de la bandeja, perpendicular a la corrida
                        XYZ pDimStart = new XYZ(mid2D.X, mid2D.Y, zTray) + dirHaciaEstructura * halfWidth;
                        XYZ pDimEnd = new XYZ(mid2D.X, mid2D.Y, zTray) + dirHaciaEstructura * Math.Abs(offsetEstructura);
                        if ((pDimEnd - pDimStart).GetLength() > 0.05)
                        {
                            Line dimLine = Line.CreateBound(pDimStart, pDimEnd);
                            Dimension dim = doc.Create.NewDimension(view, dimLine, refArray, dimType);
                            if (dim != null)
                            {
                                AjustarPosicionTextoDimension(dim);
                                cotasCreadas++;
                            }
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return cotasCreadas;
        }

        private static Reference ObtenerReferenciaCaraBandeja(Element tray, Autodesk.Revit.DB.View view, XYZ dirHaciaEstructura)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = tray.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid && solid.Volume > 1e-5)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    if (Math.Abs(pf.FaceNormal.Z) < 0.20 && pf.FaceNormal.DotProduct(dirHaciaEstructura) > 0.70)
                                    {
                                        if (face.Reference != null) return face.Reference;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return ObtenerReferenciaEjeElemento(tray, view);
        }

        private static Reference ObtenerReferenciaCaraElemento(Element elem, Autodesk.Revit.DB.View view, XYZ dirNormal)
        {
            if (elem == null) return null;
            if (elem is Wall wall) return ObtenerReferenciaCaraAcabadaMuro(wall, view, dirNormal);

            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid && solid.Volume > 1e-5)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    if (Math.Abs(pf.FaceNormal.Z) < 0.20 && pf.FaceNormal.DotProduct(dirNormal) > 0.70)
                                    {
                                        if (face.Reference != null) return face.Reference;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return ObtenerReferenciaEjeElemento(elem, view);
        }

        #endregion
        #region 8. TAGS DE EXOSTOS (REDES SECAS / ALIMENTADORES / RCI)

        public static int TaguearExostosPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> exostos = new List<Element>();
            var filter = new ExostoSelectionFilter();

            // 1. Revisar si el usuario ya tenía ductos o tuberías de exostos seleccionados en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        exostos.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y Finish
            if (exostos.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los ductos y tuberías de exostos a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !exostos.Any(x => x.Id == el.Id))
                            {
                                exostos.Add(el);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (exostos.Count == 0) return 0;

            return ProcesarTagueoExostos(doc, view, exostos);
        }

        public static int TaguearExostosTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            var cats = new[]
            {
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_FlexDuctCurves,
                BuiltInCategory.OST_FlexPipeCurves
            };

            List<Element> candidatos = new List<Element>();
            foreach (var cat in cats)
            {
                var elems = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();
                candidatos.AddRange(elems);
            }

            if (candidatos.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Exostos", "No se encontraron ductos o tuberías en la vista activa.");
                return 0;
            }

            // Filtrar elementos que correspondan a Exostos (por sistema, nombre de tipo o ductos redondos de exosto)
            List<Element> exostosEnVista = candidatos.Where(el => EsElementoExosto(el, doc)).ToList();

            if (exostosEnVista.Count == 0)
            {
                // Fallback: si no tienen nombre explícito "Exosto", tomar ductos redondos o tuberías principales
                exostosEnVista = candidatos.Where(el => EsDuctoRedondoOTuberiaValida(el)).ToList();
            }

            if (exostosEnVista.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Exostos", "No se identificaron elementos de exosto en la vista activa.");
                return 0;
            }

            return ProcesarTagueoExostos(doc, view, exostosEnVista);
        }

        private static bool EsElementoExosto(Element el, Document doc)
        {
            if (el == null) return false;

            string tipoNombre = (doc.GetElement(el.GetTypeId()) as ElementType)?.Name?.ToLower() ?? "";
            string famNombre = (el as FamilyInstance)?.Symbol?.Family?.Name?.ToLower() ?? "";
            string comStr = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.ToLower() ?? "";

            string sysName = "";
            Parameter pSys = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)
                          ?? el.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)
                          ?? el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            if (pSys != null && pSys.HasValue)
            {
                sysName = pSys.AsString()?.ToLower() ?? pSys.AsValueString()?.ToLower() ?? "";
            }

            string combined = $"{tipoNombre} {famNombre} {comStr} {sysName}";

            return combined.Contains("exosto") ||
                   combined.Contains("escape") ||
                   combined.Contains("humo") ||
                   combined.Contains("extraccion") ||
                   combined.Contains("extracción") ||
                   combined.Contains("ventilacion") ||
                   combined.Contains("ventilación") ||
                   combined.Contains("rci") ||
                   combined.Contains("generador") ||
                   combined.Contains("planta");
        }

        private static bool EsDuctoRedondoOTuberiaValida(Element el)
        {
            if (el == null) return false;

            Parameter pDiam = el.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                           ?? el.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                           ?? el.LookupParameter("Diameter")
                           ?? el.LookupParameter("Diámetro");

            if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0.25) // Diámetro >= 3"
            {
                return true;
            }

            return false;
        }

        #region Helper de Colisiones y Búsqueda de Espacio Libre para Exostos

        private class ExostoBox2D
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public ExostoBox2D(double minX, double maxX, double minY, double maxY)
            {
                MinX = Math.Min(minX, maxX);
                MaxX = Math.Max(minX, maxX);
                MinY = Math.Min(minY, maxY);
                MaxY = Math.Max(minY, maxY);
            }

            public bool Intersects(ExostoBox2D other, double margin = 0.0)
            {
                if (other == null) return false;
                return !(MaxX + margin < other.MinX ||
                         MinX - margin > other.MaxX ||
                         MaxY + margin < other.MinY ||
                         MinY - margin > other.MaxY);
            }
        }

        private class ExostoCollisionContext
        {
            public List<ExostoBox2D> TagObstacles = new List<ExostoBox2D>();
            public List<ExostoBox2D> MEPObstacles = new List<ExostoBox2D>();

            public static ExostoCollisionContext BuildFromView(Document doc, Autodesk.Revit.DB.View view)
            {
                var ctx = new ExostoCollisionContext();
                try
                {
                    // 1. Tags existentes
                    var tags = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(IndependentTag))
                        .Cast<IndependentTag>()
                        .ToList();

                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        BoundingBoxXYZ bb = t.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.TagObstacles.Add(new ExostoBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    // 2. Cotas y Spot Dimensions
                    var spotDims = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpotDimension))
                        .ToElements();
                    foreach (var sd in spotDims)
                    {
                        if (sd == null) continue;
                        BoundingBoxXYZ bb = sd.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.TagObstacles.Add(new ExostoBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    var dims = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(Dimension))
                        .ToElements();
                    foreach (var d in dims)
                    {
                        if (d == null) continue;
                        BoundingBoxXYZ bb = d.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.TagObstacles.Add(new ExostoBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    // 3. Tuberías, Ductos y Conduits
                    var pipes = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    foreach (var p in pipes)
                    {
                        BoundingBoxXYZ bb = p?.get_BoundingBox(view);
                        if (bb != null) ctx.MEPObstacles.Add(new ExostoBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                    }

                    var ducts = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_DuctCurves)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    foreach (var d in ducts)
                    {
                        BoundingBoxXYZ bb = d?.get_BoundingBox(view);
                        if (bb != null) ctx.MEPObstacles.Add(new ExostoBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                    }
                }
                catch { }
                return ctx;
            }

            public void AddPlacedTag(XYZ pos, double halfW, double halfH)
            {
                TagObstacles.Add(new ExostoBox2D(pos.X - halfW, pos.X + halfW, pos.Y - halfH, pos.Y + halfH));
            }

            public double EvaluatePosition(XYZ pos, double halfW, double halfH)
            {
                var testBox = new ExostoBox2D(pos.X - halfW, pos.X + halfW, pos.Y - halfH, pos.Y + halfH);
                double penalty = 0.0;

                foreach (var t in TagObstacles)
                {
                    if (testBox.Intersects(t, 0.15)) penalty += 1000.0;
                }

                foreach (var m in MEPObstacles)
                {
                    if (testBox.Intersects(m, 0.05)) penalty += 300.0;
                }

                return penalty;
            }
        }

        #endregion

        private static int ProcesarTagueoExostos(Document doc, Autodesk.Revit.DB.View view, List<Element> exostos)
        {
            if (exostos == null || exostos.Count == 0) return 0;

            int tagsCreados = 0;

            // Contexto de colisiones para buscar espacios limpios y libres de cruces
            ExostoCollisionContext collisionCtx = ExostoCollisionContext.BuildFromView(doc, view);

            using (Transaction tx = new Transaction(doc, "Taguear Exostos (Diámetro + Materialidad)"))
            {
                tx.Start();

                double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
                double offsetBaseFt = (4.0 / 1000.0 * scale) * 3.28084; // ~4mm en escala
                double halfTagW = (22.0 / 1000.0 * scale) * 3.28084 * 0.5; // semiancho estimado del tag (~1.8 ft a 1:50)
                double halfTagH = (8.0 / 1000.0 * scale) * 3.28084 * 0.5;  // semialto estimado del tag (~0.65 ft a 1:50)

                // 1. Resolver cada elemento a su tubería/ducto primario y extraer metadata (diámetro y material)
                var elementDataList = new List<(Element original, Element target, string matAbbr, string diamStr, XYZ pMid)>();

                foreach (var el in exostos)
                {
                    if (el == null) continue;
                    Element targetEl = BuscarTuberiaODuctoConectado(el, doc, view);
                    ObtenerFormatoTagExosto(doc, targetEl, out string matAbbr, out string diamStr);

                    XYZ pMid = null;
                    if (targetEl.Location is LocationCurve lc && lc.Curve != null)
                    {
                        pMid = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
                    }
                    else if (targetEl.Location is LocationPoint lp)
                    {
                        pMid = lp.Point;
                    }
                    else if (el.Location is LocationPoint lpEl)
                    {
                        pMid = lpEl.Point;
                    }

                    if (pMid == null) pMid = XYZ.Zero;

                    elementDataList.Add((el, targetEl, matAbbr, diamStr, pMid));
                }

                if (elementDataList.Count == 0)
                {
                    tx.RollBack();
                    return 0;
                }

                // 2. Agrupar elementos por (Material + Diámetro) que pertenezcan a la misma red/ensamble
                List<List<(Element original, Element target, string matAbbr, string diamStr, XYZ pMid)>> clusters =
                    new List<List<(Element original, Element target, string matAbbr, string diamStr, XYZ pMid)>>();

                foreach (var item in elementDataList)
                {
                    bool agregado = false;
                    foreach (var cluster in clusters)
                    {
                        var first = cluster[0];
                        if (string.Equals(first.matAbbr, item.matAbbr, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(first.diamStr, item.diamStr, StringComparison.OrdinalIgnoreCase))
                        {
                            bool conectadoOCercano = cluster.Any(c =>
                                c.target.Id == item.target.Id ||
                                c.original.Id == item.original.Id ||
                                c.pMid.DistanceTo(item.pMid) < 7.0 ||
                                EstanConectadosMEP(c.target, item.target) ||
                                EstanConectadosMEP(c.original, item.original));

                            if (conectadoOCercano)
                            {
                                cluster.Add(item);
                                agregado = true;
                                break;
                            }
                        }
                    }

                    if (!agregado)
                    {
                        clusters.Add(new List<(Element original, Element target, string matAbbr, string diamStr, XYZ pMid)> { item });
                    }
                }

                // 3. Crear exactamente 1 tag por cada grupo/cluster único buscando la posición más despejada
                foreach (var cluster in clusters)
                {
                    var first = cluster[0];
                    string matAbbr = first.matAbbr;

                    // Inyectar el parámetro Comments / DC. TAG en todos los elementos del cluster
                    void SetCom(Element eTarget)
                    {
                        if (eTarget == null) return;
                        try
                        {
                            Parameter pCom = eTarget.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                          ?? eTarget.LookupParameter("Comentarios")
                                          ?? eTarget.LookupParameter("Comments");
                            if (pCom != null && !pCom.IsReadOnly)
                            {
                                pCom.Set(matAbbr);
                            }

                            Parameter pDcTag = eTarget.LookupParameter("DC. TAG conduit accesorio")
                                            ?? eTarget.LookupParameter("DC. TAG duct")
                                            ?? eTarget.LookupParameter("DC. TAG pipe")
                                            ?? eTarget.LookupParameter("DC. TAG accesorio");
                            if (pDcTag != null && !pDcTag.IsReadOnly && pDcTag.StorageType == StorageType.String)
                            {
                                pDcTag.Set(matAbbr);
                            }
                        }
                        catch { }
                    }

                    foreach (var member in cluster)
                    {
                        SetCom(member.target);
                        if (member.original != member.target) SetCom(member.original);
                    }

                    // Seleccionar el mejor elemento representativo (priorizar Pipe/Duct de mayor longitud)
                    Element bestTarget = null;
                    double maxLen = -1;

                    foreach (var member in cluster)
                    {
                        if (member.target is Pipe || member.target is Duct)
                        {
                            if (member.target.Location is LocationCurve lc && lc.Curve != null)
                            {
                                double len = lc.Curve.Length;
                                if (len > maxLen)
                                {
                                    maxLen = len;
                                    bestTarget = member.target;
                                }
                            }
                        }
                    }

                    if (bestTarget == null)
                    {
                        bestTarget = cluster[0].target ?? cluster[0].original;
                    }

                    ElementId tagTypeId = ObtenerTipoTagExosto(doc, bestTarget);
                    if (tagTypeId == ElementId.InvalidElementId) continue;

                    var tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
                    if (tagSymbol != null && !tagSymbol.IsActive)
                    {
                        tagSymbol.Activate();
                        doc.Regenerate();
                    }

                    double halfDuctFt = 0.5;
                    Parameter pDiam = bestTarget.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                   ?? bestTarget.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                                   ?? bestTarget.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                    if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0)
                    {
                        halfDuctFt = pDiam.AsDouble() * 0.5;
                    }

                    // 4. Búsqueda inteligente de espacio en blanco (Collision Avoidance)
                    List<XYZ> candidatePoints = new List<XYZ>();
                    XYZ perp = XYZ.BasisY;
                    bool esVertical = false;

                    if (bestTarget.Location is LocationCurve lcBest && lcBest.Curve != null)
                    {
                        XYZ p0 = lcBest.Curve.GetEndPoint(0);
                        XYZ p1 = lcBest.Curve.GetEndPoint(1);
                        XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                        double len2D = v2D.GetLength();

                        if (len2D < 0.25)
                        {
                            esVertical = true;
                            candidatePoints.Add(new XYZ(p0.X, p0.Y, (p0.Z + p1.Z) * 0.5));
                            perp = -XYZ.BasisX;
                        }
                        else
                        {
                            XYZ dir = v2D.Normalize();
                            perp = new XYZ(-dir.Y, dir.X, 0);
                            // Muestrear a lo largo del tramo (centro, 35%, 65%, 20%, 80%)
                            candidatePoints.Add(p0 + (p1 - p0) * 0.50);
                            if (len2D > 1.2)
                            {
                                candidatePoints.Add(p0 + (p1 - p0) * 0.35);
                                candidatePoints.Add(p0 + (p1 - p0) * 0.65);
                            }
                            if (len2D > 2.5)
                            {
                                candidatePoints.Add(p0 + (p1 - p0) * 0.20);
                                candidatePoints.Add(p0 + (p1 - p0) * 0.80);
                            }
                        }
                    }
                    else if (bestTarget.Location is LocationPoint lpBest)
                    {
                        candidatePoints.Add(lpBest.Point);
                    }
                    else
                    {
                        candidatePoints.Add(cluster[0].pMid);
                    }

                    // Evaluar candidatos en espacio libre
                    XYZ bestTagHeadPos = null;
                    double bestPenalty = double.MaxValue;

                    double[] distMultipliers = new double[] { 1.0, 1.4, 1.9, 2.5, 3.2 };

                    foreach (var samplePt in candidatePoints)
                    {
                        List<XYZ> dirOptions = new List<XYZ>();
                        if (esVertical)
                        {
                            dirOptions.Add(XYZ.BasisX);
                            dirOptions.Add(-XYZ.BasisX);
                            dirOptions.Add(XYZ.BasisY);
                            dirOptions.Add(-XYZ.BasisY);
                        }
                        else
                        {
                            // Preferir hacia arriba (+Y) y luego hacia abajo (-Y)
                            XYZ d1 = (perp.Y >= -0.01) ? perp : -perp;
                            XYZ d2 = -d1;
                            dirOptions.Add(d1);
                            dirOptions.Add(d2);
                            // También direcciones ortogonales si hay obstáculos
                            dirOptions.Add(new XYZ(d1.X + 0.3, d1.Y, 0).Normalize());
                            dirOptions.Add(new XYZ(d1.X - 0.3, d1.Y, 0).Normalize());
                        }

                        foreach (var dir in dirOptions)
                        {
                            foreach (double mult in distMultipliers)
                            {
                                double totalDist = (halfDuctFt + offsetBaseFt) * mult;
                                XYZ testPos = samplePt + (dir * totalDist);

                                double penalty = collisionCtx.EvaluatePosition(testPos, halfTagW, halfTagH);
                                double distCost = totalDist * 3.0;
                                double totalCost = penalty + distCost;

                                if (totalCost < bestPenalty)
                                {
                                    bestPenalty = totalCost;
                                    bestTagHeadPos = testPos;

                                    // Si la posición está 100% libre de obstáculos a distancia moderada, seleccionarla de inmediato
                                    if (penalty == 0 && mult <= 1.4)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (bestPenalty == 0) break;
                        }
                        if (bestPenalty == 0) break;
                    }

                    if (bestTagHeadPos == null)
                    {
                        bestTagHeadPos = candidatePoints[0] + (perp * (halfDuctFt + offsetBaseFt + 0.5));
                    }

                    try
                    {
                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagTypeId,
                            view.Id,
                            new Reference(bestTarget),
                            true, // con directriz elástica
                            TagOrientation.Horizontal,
                            bestTagHeadPos
                        );

                        if (tag != null)
                        {
                            if (tagTypeId != ElementId.InvalidElementId)
                            {
                                try { tag.ChangeTypeId(tagTypeId); } catch { }
                            }

                            // Forzar orientación horizontal y permitir mover libremente
                            tag.TagOrientation = TagOrientation.Horizontal;
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = bestTagHeadPos;
                            try { tag.Pinned = false; } catch { } // Desbloqueado para permitir mover

                            // Registrar en el contexto de colisiones para evitar que tags sucesivos se sobrepongan
                            collisionCtx.AddPlacedTag(bestTagHeadPos, halfTagW, halfTagH);

                            tagsCreados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return tagsCreados;
        }

        private static bool EstanConectadosMEP(Element e1, Element e2)
        {
            if (e1 == null || e2 == null || e1.Id == e2.Id) return true;
            try
            {
                ConnectorSet cSet1 = null;
                if (e1 is MEPCurve mc1) cSet1 = mc1.ConnectorManager?.Connectors;
                else if (e1 is FamilyInstance fi1) cSet1 = fi1.MEPModel?.ConnectorManager?.Connectors;

                if (cSet1 == null) return false;

                foreach (Connector c in cSet1)
                {
                    if (c.IsConnected)
                    {
                        foreach (Connector other in c.AllRefs)
                        {
                            if (other.Owner != null && other.Owner.Id == e2.Id)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static Element BuscarTuberiaODuctoConectado(Element el, Document doc, Autodesk.Revit.DB.View view)
        {
            if (el == null) return null;
            if (el is Pipe || el is Duct) return el;

            if (el is FamilyInstance fi)
            {
                var mepModel = fi.MEPModel;
                if (mepModel != null && mepModel.ConnectorManager != null)
                {
                    HashSet<ElementId> visitados = new HashSet<ElementId> { el.Id };
                    Queue<Connector> cola = new Queue<Connector>();
                    foreach (Connector c in mepModel.ConnectorManager.Connectors)
                    {
                        cola.Enqueue(c);
                    }

                    int limite = 25;
                    while (cola.Count > 0 && limite-- > 0)
                    {
                        Connector curr = cola.Dequeue();
                        if (curr == null || !curr.IsConnected) continue;

                        foreach (Connector other in curr.AllRefs)
                        {
                            if (other.Owner == null || visitados.Contains(other.Owner.Id)) continue;
                            if (other.Owner is Pipe || other.Owner is Duct)
                            {
                                return other.Owner;
                            }
                            if (other.Owner is FamilyInstance fiOther)
                            {
                                visitados.Add(fiOther.Id);
                                if (fiOther.MEPModel?.ConnectorManager != null)
                                {
                                    foreach (Connector c2 in fiOther.MEPModel.ConnectorManager.Connectors)
                                    {
                                        if (c2.Id != other.Id)
                                        {
                                            cola.Enqueue(c2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Búsqueda por proximidad si el fitting/válvula no tiene enlace analítico directo
                if (el.Location is LocationPoint lp)
                {
                    XYZ p = lp.Point;
                    double minDist = double.MaxValue;
                    Element closest = null;
                    var candidates = new FilteredElementCollector(doc, view.Id)
                        .WherePasses(new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves), new ElementCategoryFilter(BuiltInCategory.OST_DuctCurves)))
                        .WhereElementIsNotElementType();

                    foreach (var cand in candidates)
                    {
                        if (cand.Location is LocationCurve lc && lc.Curve != null)
                        {
                            double d = lc.Curve.Distance(p);
                            if (d < 2.0 && d < minDist)
                            {
                                minDist = d;
                                closest = cand;
                            }
                        }
                    }
                    if (closest != null) return closest;
                }
            }

            return el;
        }

        private static string ObtenerFormatoTagExosto(Document doc, Element el, out string matAbbr, out string diamStr)
        {
            matAbbr = "A.G.";
            diamStr = "10\"ø";
            try
            {
                // 1. Extraer Diámetro en formato pulgadas con símbolo ø (ej: 10"ø)
                Parameter pDiam = el.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                               ?? el.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                               ?? el.LookupParameter("Diameter")
                               ?? el.LookupParameter("Diámetro");

                if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0)
                {
                    double dInches = Math.Round(pDiam.AsDouble() * 12.0);
                    diamStr = $"{dInches}\"ø";
                }
                else
                {
                    Parameter pW = el.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM) ?? el.LookupParameter("Width") ?? el.LookupParameter("Ancho");
                    Parameter pH = el.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM) ?? el.LookupParameter("Height") ?? el.LookupParameter("Alto");
                    if (pW != null && pH != null && pW.HasValue && pH.HasValue)
                    {
                        double wInches = Math.Round(pW.AsDouble() * 12.0);
                        double hInches = Math.Round(pH.AsDouble() * 12.0);
                        diamStr = (wInches == hInches) ? $"{wInches}\"ø" : $"{wInches}\"x{hInches}\"";
                    }
                    else
                    {
                        Parameter pCalcSize = el.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)
                                           ?? el.LookupParameter("Size")
                                           ?? el.LookupParameter("Tamaño");
                        if (pCalcSize != null && pCalcSize.HasValue && !string.IsNullOrWhiteSpace(pCalcSize.AsString()))
                        {
                            diamStr = SimplificarTextoDimension(pCalcSize.AsString());
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(diamStr))
                {
                    diamStr = "10\"ø";
                }

                // 2. Extraer Materialidad (Por norma: "Acero galvanizado", "Acero al carbón", etc.)
                string matStr = "Acero galvanizado";
                Parameter pMat = el.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                              ?? el.LookupParameter("Material")
                              ?? el.LookupParameter("Materialidad")
                              ?? el.LookupParameter("Duct Material")
                              ?? el.LookupParameter("Pipe Material");

                if (pMat != null && pMat.HasValue)
                {
                    if (pMat.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(pMat.AsString()))
                    {
                        matStr = pMat.AsString();
                    }
                    else if (pMat.StorageType == StorageType.ElementId && pMat.AsElementId() != ElementId.InvalidElementId)
                    {
                        Element matEl = doc.GetElement(pMat.AsElementId());
                        if (matEl != null && !string.IsNullOrWhiteSpace(matEl.Name))
                        {
                            matStr = matEl.Name;
                        }
                    }
                }

                // Obtener abreviatura limpia y concisa (ej: A.C., A.G., PVC, HF)
                matAbbr = ObtenerAbreviaturaMaterial(matStr);

                return $"{matAbbr} {diamStr}";
            }
            catch
            {
                matAbbr = "A.G.";
                diamStr = "10\"ø";
                return "A.G. 10\"ø";
            }
        }

        private static string ObtenerAbreviaturaMaterial(string matStr)
        {
            if (string.IsNullOrWhiteSpace(matStr)) return "A.G.";
            string m = matStr.ToUpperInvariant();

            if (m.Contains("CARB") || m.Contains("RCI") || m.Contains("INCEND") || m.Contains("SCH") || m.Contains("NEGRO"))
                return "A.C.";

            if (m.Contains("GALV") || m.Contains("EXOST") || m.Contains("DUCT") || m.Contains("ZINC"))
                return "A.G.";

            if (m.Contains("PVC") || m.Contains("SANITA") || m.Contains("PAVCO"))
                return "PVC";

            if (m.Contains("COBRE") || m.Contains("COPPER") || m.Contains("CU"))
                return "Cu";

            if (m.Contains("HIERRO") || m.Contains("HF") || m.Contains("FUND"))
                return "HF";

            if (m.Contains("INOX") || m.Contains("STAINLESS"))
                return "A.I.";

            if (m.Contains("ALUM"))
                return "Al";

            return "A.G.";
        }

        private static string SimplificarTextoDimension(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();

            // Eliminar espacios alrededor de guiones
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*-\s*", "-");

            // Si tiene formato "3/4"ø-3/4"ø" o "3/4"-3/4"" o "3/4" - 3/4""
            if (s.Contains("-"))
            {
                string[] parts = s.Split('-');
                if (parts.Length > 1)
                {
                    string p0 = parts[0].Replace("ø", "").Replace("Ø", "").Trim();
                    bool allEqual = true;
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string pi = parts[i].Replace("ø", "").Replace("Ø", "").Trim();
                        if (!string.Equals(p0, pi, StringComparison.OrdinalIgnoreCase))
                        {
                            allEqual = false;
                            break;
                        }
                    }
                    if (allEqual)
                    {
                        return p0.EndsWith("\"") ? $"{p0}ø" : $"{p0}\"ø";
                    }
                }
            }

            if (!s.Contains("ø") && !s.Contains("Ø") && !s.Contains("x") && !s.Contains("X"))
            {
                s = s.EndsWith("\"") ? $"{s}ø" : $"{s}\"ø";
            }

            return s;
        }

        private static ElementId ObtenerTipoTagExosto(Document doc, Element el)
        {
            try
            {
                BuiltInCategory catTag = BuiltInCategory.OST_DuctTags;
                if (el != null && el.Category != null)
                {
#if REVIT2024_OR_LATER
                    long catId = el.Category.Id.Value;
                    if (catId == (long)BuiltInCategory.OST_PipeCurves || catId == (long)BuiltInCategory.OST_PipeFitting || catId == (long)BuiltInCategory.OST_FlexPipeCurves)
                    {
                        catTag = BuiltInCategory.OST_PipeTags;
                    }
#else
                    int catId = el.Category.Id.IntegerValue;
                    if (catId == (int)BuiltInCategory.OST_PipeCurves || catId == (int)BuiltInCategory.OST_PipeFitting || catId == (int)BuiltInCategory.OST_FlexPipeCurves)
                    {
                        catTag = BuiltInCategory.OST_PipeTags;
                    }
#endif
                }

                var tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(catTag)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                var multiTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                tagTypes.AddRange(multiTypes);

                if (tagTypes.Count > 0)
                {
                    // Prioridad 1: Tags que contengan "Exosto", "Material", "Diámetro", o "Dim" (usan la abreviatura A.C., A.G., etc.)
                    var tagExosto = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return n.IndexOf("Exosto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0);
                    });

                    if (tagExosto != null)
                    {
                        AsegurarTamanoFuenteTag(doc, tagExosto);
                        return tagExosto.Id;
                    }

                    var tagMat = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Mat", StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                    if (tagMat != null)
                    {
                        AsegurarTamanoFuenteTag(doc, tagMat);
                        return tagMat.Id;
                    }

                    var fallback = tagTypes.First();
                    AsegurarTamanoFuenteTag(doc, fallback);
                    return fallback.Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private static void AsegurarTamanoFuenteTag(Document doc, ElementType tagType)
        {
            if (tagType == null) return;
            try
            {
                // Si el tipo de tag tiene parámetro de tamaño de texto modificable, ajustarlo ligeramente (ej: 2.7 mm)
                Parameter pSize = tagType.get_Parameter(BuiltInParameter.TEXT_SIZE)
                               ?? tagType.LookupParameter("Text Size")
                               ?? tagType.LookupParameter("Tamaño de texto")
                               ?? tagType.LookupParameter("Tamaño de Texto")
                               ?? tagType.LookupParameter("Altura de texto")
                               ?? tagType.LookupParameter("Text Height");

                if (pSize != null && !pSize.IsReadOnly && pSize.StorageType == StorageType.Double)
                {
                    double currentVal = pSize.AsDouble();
                    if (currentVal < 0.00853) // si es menor a ~2.6 mm
                    {
                        pSize.Set(0.00885); // incrementar a ~2.7 mm
                    }
                }
            }
            catch { }
        }

        private class ExostoSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                int catId = e.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_DuctCurves ||
                       catId == (int)BuiltInCategory.OST_DuctFitting ||
                       catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_PipeFitting ||
                       catId == (int)BuiltInCategory.OST_FlexDuctCurves ||
                       catId == (int)BuiltInCategory.OST_FlexPipeCurves ||
                       catId == (int)BuiltInCategory.OST_MechanicalEquipment;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        #endregion

        #region 8. TAGS DE BUITRONES Y CUARTOS TÉCNICOS (ROOM TAGS)

        /// <summary>
        /// Flujo por selección interactiva con cursor (+/-) y botón Finish para colocar Room Tags
        /// en Buitrones (diminutivo por disciplina: HID, ELE, ETO, HVAC, RCI) y Cuartos Técnicos
        /// tanto en el modelo anfitrión como en modelos arquitectónicos vinculados.
        /// </summary>
        public static int TaguearBuitronesYCuartosPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Room> hostRooms = new List<Room>();
            List<Tuple<RevitLinkInstance, Room>> linkedRooms = new List<Tuple<RevitLinkInstance, Room>>();
            var filter = new RoomOrLinkSelectionFilter(doc);

            // 1. Revisar preselección en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el is Room r && r.Area > 0.01)
                    {
                        if (!hostRooms.Any(x => x.Id == r.Id)) hostRooms.Add(r);
                    }
                    else if (el is RevitLinkInstance linkInst)
                    {
                        Document linkDoc = linkInst.GetLinkDocument();
                        if (linkDoc != null)
                        {
                            var rList = new FilteredElementCollector(linkDoc)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .WhereElementIsNotElementType()
                                .OfType<Room>()
                                .Where(rm => rm.Area > 0.01 && IsRoomInView(rm, linkInst.GetTotalTransform(), view))
                                .ToList();
                            foreach (var rm in rList)
                            {
                                if (!linkedRooms.Any(x => x.Item1.Id == linkInst.Id && x.Item2.Id == rm.Id))
                                    linkedRooms.Add(Tuple.Create(linkInst, rm));
                            }
                        }
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (hostRooms.Count == 0 && linkedRooms.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los cuartos técnicos, buitrones (o vínculos) a taguear y haz clic en 'Finish':"
                    );

                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el is Room hostRoom && hostRoom.Area > 0.01)
                            {
                                if (!hostRooms.Any(x => x.Id == hostRoom.Id))
                                    hostRooms.Add(hostRoom);
                            }
                            else if (el is RevitLinkInstance linkInst)
                            {
                                Document linkDoc = linkInst.GetLinkDocument();
                                if (linkDoc != null)
                                {
                                    if (r.LinkedElementId != null && r.LinkedElementId != ElementId.InvalidElementId)
                                    {
                                        Element linkedEl = linkDoc.GetElement(r.LinkedElementId);
                                        if (linkedEl is Room linkedRm && linkedRm.Area > 0.01)
                                        {
                                            if (!linkedRooms.Any(x => x.Item1.Id == linkInst.Id && x.Item2.Id == linkedRm.Id))
                                                linkedRooms.Add(Tuple.Create(linkInst, linkedRm));
                                        }
                                    }
                                    else
                                    {
                                        var rList = new FilteredElementCollector(linkDoc)
                                            .OfCategory(BuiltInCategory.OST_Rooms)
                                            .WhereElementIsNotElementType()
                                            .OfType<Room>()
                                            .Where(rm => rm.Area > 0.01 && IsRoomInView(rm, linkInst.GetTotalTransform(), view))
                                            .ToList();
                                        foreach (var rm in rList)
                                        {
                                            if (!linkedRooms.Any(x => x.Item1.Id == linkInst.Id && x.Item2.Id == rm.Id))
                                                linkedRooms.Add(Tuple.Create(linkInst, rm));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (hostRooms.Count == 0 && linkedRooms.Count == 0) return 0;

            return ProcesarTagueoBuitronesYCuartos(doc, view, hostRooms, linkedRooms);
        }

        /// <summary>
        /// Flujo automático que escanea la vista activa para detectar todos los Buitrones y Cuartos Técnicos
        /// (tanto en el proyecto anfitrión como en vínculos arquitectónicos) y colocarles sus Room Tags.
        /// </summary>
        public static int TaguearBuitronesYCuartosTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Room> hostRooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0.01 && EsBuitronOCuartoTecnico(r.Name))
                .ToList();

            List<Tuple<RevitLinkInstance, Room>> linkedRooms = new List<Tuple<RevitLinkInstance, Room>>();
            var linkInstances = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RvtLinks)
                .WhereElementIsNotElementType()
                .OfType<RevitLinkInstance>()
                .ToList();

            foreach (var linkInst in linkInstances)
            {
                Document linkDoc = linkInst.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform transform = linkInst.GetTotalTransform();
                var rooms = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.Area > 0.01 && EsBuitronOCuartoTecnico(r.Name))
                    .ToList();

                foreach (var rm in rooms)
                {
                    if (IsRoomInView(rm, transform, view))
                    {
                        linkedRooms.Add(Tuple.Create(linkInst, rm));
                    }
                }
            }

            if (hostRooms.Count == 0 && linkedRooms.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags Buitrones / C.T.",
                    "No se encontraron habitaciones identificadas como buitrones o cuartos técnicos en la vista activa.\n\n" +
                    "Tip: Puedes usar la opción 'Por Selección Múltiple' para seleccionar directamente las habitaciones o el vínculo.");
                return 0;
            }

            return ProcesarTagueoBuitronesYCuartos(doc, view, hostRooms, linkedRooms);
        }

        private static int ProcesarTagueoBuitronesYCuartos(
            Document doc,
            Autodesk.Revit.DB.View view,
            List<Room> hostRooms,
            List<Tuple<RevitLinkInstance, Room>> linkedRooms)
        {
            // Validar existencia de tipos de Room Tag en el documento
            var roomTagTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsElementType()
                .ToList();

            if (roomTagTypes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags Buitrones / C.T.",
                    "No se encontró ninguna familia de Room Tag cargada en el proyecto (OST_RoomTags).\n\n" +
                    "Para colocar este tag, carga una etiqueta de habitación o usa el comando 'RT' (Room Tag) desde la pestaña Arquitectura de Revit.");
                return 0;
            }

            // Recopilar RoomTags existentes en la vista para evitar duplicados
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .OfType<RoomTag>()
                .ToList();

            HashSet<string> existingKeys = new HashSet<string>();
            foreach (var tag in existingTags)
            {
                try
                {
                    if (tag.Room != null)
                    {
                        existingKeys.Add($"host_{tag.Room.Id.IntegerValue}");
                        existingKeys.Add($"link_{tag.Room.Id.IntegerValue}");
                    }
                }
                catch { }
            }

            int creados = 0;

            using (Transaction tx = new Transaction(doc, "Taguear Buitrones y Cuartos Técnicos (Room Tags)"))
            {
                tx.Start();

                // 1. Taguear habitaciones locales
                foreach (var room in hostRooms)
                {
                    if (room == null) continue;
                    string key = $"host_{room.Id.IntegerValue}";
                    if (existingKeys.Contains(key)) continue;

                    XYZ pt = null;
                    if (room.Location is LocationPoint lp) pt = lp.Point;
                    if (pt == null)
                    {
                        var bbox = room.get_BoundingBox(view);
                        if (bbox != null) pt = (bbox.Min + bbox.Max) * 0.5;
                    }
                    if (pt == null) continue;

                    UV uv = new UV(pt.X, pt.Y);
                    try
                    {
                        LinkElementId hostLinkElemId = new LinkElementId(room.Id);
                        RoomTag tag = doc.Create.NewRoomTag(hostLinkElemId, uv, view.Id);
                        if (tag != null)
                        {
                            creados++;
                            existingKeys.Add(key);
                        }
                    }
                    catch { }
                }

                // 2. Taguear habitaciones vinculadas
                foreach (var pair in linkedRooms)
                {
                    var linkInst = pair.Item1;
                    var linkedRm = pair.Item2;
                    if (linkInst == null || linkedRm == null) continue;

                    string key = $"link_{linkInst.Id.IntegerValue}_{linkedRm.Id.IntegerValue}";
                    if (existingKeys.Contains(key)) continue;

                    XYZ pt = null;
                    if (linkedRm.Location is LocationPoint lp) pt = lp.Point;
                    if (pt == null)
                    {
                        var bbox = linkedRm.get_BoundingBox(null);
                        if (bbox != null) pt = (bbox.Min + bbox.Max) * 0.5;
                    }
                    if (pt == null) continue;

                    XYZ ptTransformed = linkInst.GetTotalTransform().OfPoint(pt);
                    UV uv = new UV(ptTransformed.X, ptTransformed.Y);

                    try
                    {
                        LinkElementId linkElemId = new LinkElementId(linkInst.Id, linkedRm.Id);
                        RoomTag tag = doc.Create.NewRoomTag(linkElemId, uv, view.Id);
                        if (tag != null)
                        {
                            creados++;
                            existingKeys.Add(key);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return creados;
        }

        private static bool EsBuitronOCuartoTecnico(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName)) return false;
            string upper = roomName.ToUpperInvariant();

            // 1. Buitrones / Shafts / Pasos
            if (upper.Contains("BUITRON") || upper.Contains("BUITRÓN") || upper.Contains("SHAFT") ||
                upper.Contains("DUCTO") || upper.Contains("PASO") || upper.Contains("VACIO") ||
                upper.Contains("VACÍO") || upper.Contains("PLENO") || upper.Contains("SH"))
                return true;

            // 2. Disciplinas (diminutivos o nombres)
            if (upper.Contains("HID") || upper.Contains("ELE") || upper.Contains("ETO") ||
                upper.Contains("HVAC") || upper.Contains("RCI") || upper.Contains("GAS") ||
                upper.Contains("CLIMA") || upper.Contains("SANIT") || upper.Contains("PLUVIAL"))
                return true;

            // 3. Cuartos técnicos
            if (upper.Contains("CUARTO") || upper.Contains("TECNICO") || upper.Contains("TÉCNICO") ||
                upper.Contains("ELECTR") || upper.Contains("ELECTRÓN") || upper.Contains("SUBESTACION") ||
                upper.Contains("SUBESTACIÓN") || upper.Contains("TABLERO") || upper.Contains("TRANSFORMADOR") ||
                upper.Contains("UPS") || upper.Contains("RACK") || upper.Contains("DATA") ||
                upper.Contains("BOMBA") || upper.Contains("PLANTA") || upper.Contains("TELECOM") ||
                upper.Contains("COMUNICACION") || upper.Contains("COMUNICACIÓN") || upper.Contains("MAQUINAS") ||
                upper.Contains("MÁQUINAS") || upper.Contains("CALDERAS") || upper.Contains("MEDIDORES"))
                return true;

            return false;
        }

        private static bool IsRoomInView(Room room, Transform transform, Autodesk.Revit.DB.View view)
        {
            try
            {
                var bbox = room.get_BoundingBox(null);
                if (bbox == null) return false;

                XYZ minT = transform.OfPoint(bbox.Min);
                XYZ maxT = transform.OfPoint(bbox.Max);

                if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
                {
                    double levelZ = viewPlan.GenLevel.Elevation;
                    double minZ = Math.Min(minT.Z, maxT.Z) - 2.0;
                    double maxZ = Math.Max(minT.Z, maxT.Z) + 12.0;
                    if (levelZ < minZ || levelZ > maxZ) return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        public class RoomOrLinkSelectionFilter : ISelectionFilter
        {
            private readonly Document _doc;
            public RoomOrLinkSelectionFilter(Document doc) { _doc = doc; }

            public bool AllowElement(Element e)
            {
                if (e == null) return false;
                if (e is Room || e is SpatialElement) return true;
                if (e is RevitLinkInstance) return true;
                if (e.Category != null)
                {
                    int catId = e.Category.Id.IntegerValue;
                    if (catId == (int)BuiltInCategory.OST_Rooms ||
                        catId == (int)BuiltInCategory.OST_RvtLinks ||
                        catId == (int)BuiltInCategory.OST_Walls ||
                        catId == (int)BuiltInCategory.OST_Floors ||
                        catId == (int)BuiltInCategory.OST_Doors ||
                        catId == (int)BuiltInCategory.OST_DuctCurves ||
                        catId == (int)BuiltInCategory.OST_PipeCurves ||
                        catId == (int)BuiltInCategory.OST_Conduit ||
                        catId == (int)BuiltInCategory.OST_CableTray)
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        #endregion

        #region 9. CONDUIT TAGS (TYPE NAME + SIZE)

        public static int TaguearConduitTagsPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> conduits = new List<Element>();
            var filter = new ConduitTagSelectionFilter();

            // 1. Revisar si el usuario ya tenía conduits seleccionados
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        conduits.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y Finish
            if (conduits.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los conduits a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !conduits.Any(x => x.Id == el.Id))
                            {
                                conduits.Add(el);
                            }
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (conduits.Count == 0) return 0;

            return ProcesarTagueoConduits(doc, view, conduits);
        }

        public static int TaguearConduitTagsTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (conduits.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Conduit Tags", "No se encontraron conduits en la vista activa.");
                return 0;
            }

            return ProcesarTagueoConduits(doc, view, conduits);
        }

        /// <summary>
        /// Información de un tramo de conduit candidato a ser etiquetado.
        /// </summary>
        private class TramoConduitInfo
        {
            public Element Elemento;
            public XYZ P0;
            public XYZ P1;
            public XYZ Dir;          // Dirección 2D normalizada en planta
            public double Z;
            public string Clave;     // Tipo + Diámetro (solo se agrupan conduits iguales)
            public string Texto;     // Texto estimado del tag (Type Name + Size) para calcular su ancho
            public bool YaEtiquetado;
        }

        /// <summary>
        /// Coloca Conduit Tags (Type Name + Size) evitando sobreposición:
        /// 1. Agrupa tramos continuos y colineales del mismo tipo/diámetro (igual que Cotas de Nivel) → 1 tag por corrida.
        /// 2. Omite corridas que ya tengan un tag, y corridas con un tag igual (mismo tipo + diámetro) muy cerca.
        /// 3. Busca la posición más despejada evaluando candidatos contra tags, cotas, textos y elementos MEP.
        /// 4. Si no existe ninguna posición libre de otros tags, NO coloca el tag (prefiere omitir a sobreponer).
        /// </summary>
        private static int ProcesarTagueoConduits(Document doc, Autodesk.Revit.DB.View view, List<Element> conduits)
        {
            if (conduits == null || conduits.Count == 0) return 0;

            ElementId tagTypeId = ObtenerTipoTagConduitTypeNameSize(doc);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Conduit Tags", "No se encontró ningún tipo de etiqueta de Conduit (OST_ConduitTags) cargado en el proyecto.");
                return 0;
            }

            var tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;

            double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
            double ftPorMm = scale / 1000.0 * 3.28084;           // 1 mm en papel expresado en pies de modelo
            double gapFt = 1.5 * ftPorMm;                         // Separación mínima entre tags (~1.5 mm en papel)
            double halfTagH = 2.2 * ftPorMm;                      // Semialto estimado del tag (~4.4 mm en papel)
            double radioMismoTagFt = 15.0 * ftPorMm;              // No repetir un tag igual a menos de ~15 mm en papel
            double longitudMinimaCorrida = Math.Max(1.0, 6.0 * ftPorMm); // Omitir corridas demasiado cortas

            // ------------------------------------------------------------------
            // 1. Tags existentes: conduits ya etiquetados + cajas de tags (obstáculos duros)
            // ------------------------------------------------------------------
            HashSet<ElementId> taggedConduitIds = new HashSet<ElementId>();
            List<Tuple<string, XYZ>> tagsPorClave = new List<Tuple<string, XYZ>>();
            List<Box2D> cajasTags = new List<Box2D>();

            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            foreach (var t in existingTags)
            {
                XYZ head = null;
                try { head = t.TagHeadPosition; } catch { }

                try
                {
                    BoundingBoxXYZ bb = t.get_BoundingBox(view);
                    if (bb != null) cajasTags.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                    else if (head != null) cajasTags.Add(new Box2D(head.X - 1.0, head.X + 1.0, head.Y - 0.45, head.Y + 0.45));
                }
                catch { }

                foreach (ElementId id in ObtenerIdsElementosEtiquetados(t))
                {
                    taggedConduitIds.Add(id);
                    Element tagged = doc.GetElement(id);
                    if (head != null && tagged is Conduit)
                    {
                        tagsPorClave.Add(Tuple.Create(ObtenerClaveConduit(doc, tagged), head));
                    }
                }
            }

            // Cotas de elevación existentes también cuentan como obstáculo duro
            try
            {
                var spots = new FilteredElementCollector(doc, view.Id).OfClass(typeof(SpotDimension)).ToElements();
                foreach (var s in spots)
                {
                    BoundingBoxXYZ bb = s?.get_BoundingBox(view);
                    if (bb != null) cajasTags.Add(new Box2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                }
            }
            catch { }

            // ------------------------------------------------------------------
            // 2. Recolectar tramos válidos (horizontales en planta; se omiten bajantes/subidas verticales)
            // ------------------------------------------------------------------
            List<TramoConduitInfo> tramos = new List<TramoConduitInfo>();
            foreach (var conduit in conduits)
            {
                if (conduit == null) continue;
                if (!(conduit.Location is LocationCurve lc) || lc.Curve == null) continue;

                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                if (v2D.GetLength() < 0.25) continue; // Vertical en planta o degenerado

                tramos.Add(new TramoConduitInfo
                {
                    Elemento = conduit,
                    P0 = p0,
                    P1 = p1,
                    Dir = v2D.Normalize(),
                    Z = (p0.Z + p1.Z) * 0.5,
                    Clave = ObtenerClaveConduit(doc, conduit),
                    Texto = ObtenerTextoEstimadoConduit(doc, conduit),
                    YaEtiquetado = taggedConduitIds.Contains(conduit.Id)
                });
            }

            if (tramos.Count == 0) return 0;

            // ------------------------------------------------------------------
            // 3. Agrupar corridas continuas (Union-Find): mismo tipo/diámetro, colineales, misma Z y con huecos <= 4 ft
            // ------------------------------------------------------------------
            int n = tramos.Count;
            int[] padre = Enumerable.Range(0, n).ToArray();
            Func<int, int> raiz = null;
            raiz = i => padre[i] == i ? i : (padre[i] = raiz(padre[i]));

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (TramosConduitContinuos(tramos[i], tramos[j]))
                    {
                        int ri = raiz(i), rj = raiz(j);
                        if (ri != rj) padre[rj] = ri;
                    }
                }
            }

            var corridas = Enumerable.Range(0, n)
                .GroupBy(i => raiz(i))
                .Select(g => g.Select(i => tramos[i]).ToList())
                .ToList();

            // ------------------------------------------------------------------
            // 4. Contexto de colisiones (misma lógica que Cotas de Nivel)
            // ------------------------------------------------------------------
            SpotCollisionContext collisionCtx = SpotCollisionContext.BuildFromView(doc, view);

            int totalCreados = 0;
            using (Transaction tx = new Transaction(doc, "Taguear Conduit Tags (Type Name + Size)"))
            {
                tx.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                // Etiquetar primero las corridas más largas (son las más importantes en el plano)
                foreach (var grupo in corridas.OrderByDescending(g => g.Sum(t => t.P0.DistanceTo(t.P1))))
                {
                    try
                    {
                        // 4.1 Si cualquier tramo de la corrida ya tiene tag → no duplicar
                        if (grupo.Any(t => t.YaEtiquetado)) continue;

                        // 4.2 Extensión de la corrida proyectada sobre su eje
                        XYZ dir = grupo[0].Dir;
                        XYZ origen = grupo[0].P0;
                        double tMin = double.MaxValue, tMax = double.MinValue;
                        foreach (var t in grupo)
                        {
                            double a = ProyectarEnEje2D(t.P0, origen, dir);
                            double b = ProyectarEnEje2D(t.P1, origen, dir);
                            tMin = Math.Min(tMin, Math.Min(a, b));
                            tMax = Math.Max(tMax, Math.Max(a, b));
                        }

                        if (tMax - tMin < longitudMinimaCorrida) continue;

                        // 4.3 Dimensiones estimadas del tag según su texto y la escala
                        string texto = grupo[0].Texto ?? "";
                        double halfTagW = Math.Max(10.0, texto.Length * 1.7) * 0.5 * ftPorMm;

                        // 4.4 Lado preferido: arriba (+Y) en tramos horizontales, izquierda (-X) en tramos verticales
                        XYZ perp = new XYZ(-dir.Y, dir.X, 0);
                        if (Math.Abs(dir.Y) > Math.Abs(dir.X)) { if (perp.X > 0) perp = -perp; }
                        else { if (perp.Y < 0) perp = -perp; }

                        double offsetBase = Math.Abs(perp.X) * halfTagW + Math.Abs(perp.Y) * halfTagH + 2.0 * ftPorMm;
                        double[] fracciones = { 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8 };
                        double[] multiplicadores = { 1.0, 1.5, 2.2, 3.0, 4.0 };

                        // 4.5 Si ya hay un tag igual (mismo tipo + diámetro) cerca de la corrida → omitir
                        XYZ centroCorrida = PuntoSobreCorrida(grupo, origen, dir, (tMin + tMax) * 0.5) ?? grupo[0].P0;
                        bool hayTagIgualCerca = tagsPorClave.Any(tc =>
                            tc.Item1 == grupo[0].Clave &&
                            DistanciaPuntoACorrida2D(tc.Item2, grupo) < radioMismoTagFt);
                        if (hayTagIgualCerca) continue;

                        // 4.6 Búsqueda de la posición más despejada
                        XYZ mejorHead = null;
                        XYZ mejorBase = null;
                        double mejorPenalidad = double.MaxValue;
                        bool mejorChocaTag = true;
                        bool encontradoLimpio = false;

                        foreach (double f in fracciones)
                        {
                            XYZ basePt = PuntoSobreCorrida(grupo, origen, dir, tMin + (tMax - tMin) * f);
                            if (basePt == null) continue; // Cae en un hueco (accesorio) de la corrida

                            foreach (double lado in new[] { 1.0, -1.0 })
                            {
                                foreach (double mult in multiplicadores)
                                {
                                    XYZ head = basePt + perp * (lado * offsetBase * mult);
                                    Box2D cajaTag = new Box2D(head.X - halfTagW, head.X + halfTagW, head.Y - halfTagH, head.Y + halfTagH);

                                    bool chocaTag = cajasTags.Any(c => cajaTag.Intersects(c, gapFt));
                                    double penalidad = EvaluarPenalidadTagConduit(basePt, head, cajaTag, gapFt, collisionCtx, grupo);
                                    if (chocaTag) penalidad += 20000.0;
                                    if (lado < 0) penalidad += 50.0; // Preferir ligeramente el lado principal

                                    if (penalidad < mejorPenalidad)
                                    {
                                        mejorPenalidad = penalidad;
                                        mejorHead = head;
                                        mejorBase = basePt;
                                        mejorChocaTag = chocaTag;
                                    }

                                    if (!chocaTag && penalidad < 1000.0)
                                    {
                                        encontradoLimpio = true;
                                        break;
                                    }
                                }
                                if (encontradoLimpio) break;
                            }
                            if (encontradoLimpio) break;
                        }

                        // 4.7 Nunca sobreponer sobre otro tag/cota: si no hay espacio libre, se omite
                        if (mejorHead == null || mejorChocaTag) continue;

                        TramoConduitInfo tramoRef = grupo
                            .OrderBy(t => DistanciaPuntoASegmento2D(mejorBase, t.P0, t.P1))
                            .First();

                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            tagTypeId,
                            view.Id,
                            new Reference(tramoRef.Elemento),
                            true, // Con líder
                            TagOrientation.Horizontal,
                            mejorHead
                        );

                        if (newTag != null)
                        {
                            try { newTag.ChangeTypeId(tagTypeId); } catch { }

                            newTag.HasLeader = true;
                            newTag.LeaderEndCondition = LeaderEndCondition.Attached;
                            newTag.TagHeadPosition = mejorHead;

                            // Registrar el nuevo tag como obstáculo para los siguientes
                            Box2D cajaReal = null;
                            try
                            {
                                BoundingBoxXYZ bbNew = newTag.get_BoundingBox(view);
                                if (bbNew != null) cajaReal = new Box2D(bbNew.Min.X, bbNew.Max.X, bbNew.Min.Y, bbNew.Max.Y);
                            }
                            catch { }
                            if (cajaReal == null)
                            {
                                cajaReal = new Box2D(mejorHead.X - halfTagW, mejorHead.X + halfTagW, mejorHead.Y - halfTagH, mejorHead.Y + halfTagH);
                            }

                            cajasTags.Add(cajaReal);
                            collisionCtx.ObstacleBoxes.Add(cajaReal);
                            collisionCtx.ElementSegments.Add(Tuple.Create(mejorBase, mejorHead)); // La directriz también es obstáculo
                            tagsPorClave.Add(Tuple.Create(grupo[0].Clave, mejorHead));
                            foreach (var t in grupo) taggedConduitIds.Add(t.Elemento.Id);

                            totalCreados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return totalCreados;
        }

        private static bool TramosConduitContinuos(TramoConduitInfo a, TramoConduitInfo b)
        {
            if (!string.Equals(a.Clave, b.Clave, StringComparison.Ordinal)) return false;
            if (Math.Abs(a.Z - b.Z) >= 0.05) return false;

            // Colineales (paralelos y sobre la misma línea en planta)
            double dot = Math.Abs(a.Dir.X * b.Dir.X + a.Dir.Y * b.Dir.Y);
            if (dot < 0.995) return false;

            XYZ perp = new XYZ(-a.Dir.Y, a.Dir.X, 0);
            double offPerp = Math.Abs((b.P0.X - a.P0.X) * perp.X + (b.P0.Y - a.P0.Y) * perp.Y);
            if (offPerp > 0.35) return false;

            // Hueco entre tramos a lo largo del eje (accesorios/uniones)
            double a0 = ProyectarEnEje2D(a.P0, a.P0, a.Dir);
            double a1 = ProyectarEnEje2D(a.P1, a.P0, a.Dir);
            double b0 = ProyectarEnEje2D(b.P0, a.P0, a.Dir);
            double b1 = ProyectarEnEje2D(b.P1, a.P0, a.Dir);
            return DistanciaEntreIntervalos(Math.Min(a0, a1), Math.Max(a0, a1), Math.Min(b0, b1), Math.Max(b0, b1)) <= 4.0;
        }

        private static double ProyectarEnEje2D(XYZ p, XYZ origen, XYZ dir)
        {
            return (p.X - origen.X) * dir.X + (p.Y - origen.Y) * dir.Y;
        }

        /// <summary>
        /// Devuelve el punto real sobre el conduit correspondiente al parámetro t del eje de la corrida,
        /// o null si t cae en un hueco (accesorio) entre tramos.
        /// </summary>
        private static XYZ PuntoSobreCorrida(List<TramoConduitInfo> grupo, XYZ origen, XYZ dir, double t)
        {
            const double margen = 0.3;
            foreach (var tr in grupo)
            {
                double a = ProyectarEnEje2D(tr.P0, origen, dir);
                double b = ProyectarEnEje2D(tr.P1, origen, dir);
                double lo = Math.Min(a, b), hi = Math.Max(a, b);
                if (hi - lo < 2 * margen) continue;
                if (t < lo + margen || t > hi - margen) continue;

                double u = (t - a) / (b - a);
                return tr.P0 + (tr.P1 - tr.P0) * u;
            }
            return null;
        }

        private static double DistanciaPuntoASegmento2D(XYZ p, XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double u = len2 < 1e-9 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            double cx = a.X + u * dx - p.X, cy = a.Y + u * dy - p.Y;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        private static double DistanciaPuntoACorrida2D(XYZ p, List<TramoConduitInfo> grupo)
        {
            double min = double.MaxValue;
            foreach (var t in grupo) min = Math.Min(min, DistanciaPuntoASegmento2D(p, t.P0, t.P1));
            return min;
        }

        private static double EvaluarPenalidadTagConduit(
            XYZ basePt, XYZ head, Box2D cajaTag, double gapFt, SpotCollisionContext ctx, List<TramoConduitInfo> grupo)
        {
            double dist = Math.Sqrt(Math.Pow(head.X - basePt.X, 2) + Math.Pow(head.Y - basePt.Y, 2));
            double penalidad = dist * 2.0; // Preferir la posición más cercana al conduit

            if (ctx == null) return penalidad;

            // 1. Cajas de obstáculos (tags, cotas, textos, aparatos, accesorios)
            foreach (var obs in ctx.ObstacleBoxes)
            {
                if (cajaTag.Intersects(obs, gapFt))
                {
                    penalidad += 10000.0;
                }
                else if (SegmentoIntersecaCaja2D(basePt, head, obs.MinX, obs.MaxX, obs.MinY, obs.MaxY))
                {
                    penalidad += 3000.0; // La directriz cruza sobre un tag o cota existente
                }
            }

            // 2. Líneas MEP (otros conduits, tuberías, bandejas, ductos y directrices ya colocadas)
            foreach (var seg in ctx.ElementSegments)
            {
                if (EsSegmentoDeCorrida(seg, grupo)) continue;
                if (SegmentoIntersecaCaja2D(seg.Item1, seg.Item2, cajaTag.MinX, cajaTag.MaxX, cajaTag.MinY, cajaTag.MaxY))
                {
                    penalidad += 5000.0;
                }
            }

            return penalidad;
        }

        private static bool EsSegmentoDeCorrida(Tuple<XYZ, XYZ> seg, List<TramoConduitInfo> grupo)
        {
            foreach (var t in grupo)
            {
                if ((seg.Item1.IsAlmostEqualTo(t.P0) && seg.Item2.IsAlmostEqualTo(t.P1)) ||
                    (seg.Item1.IsAlmostEqualTo(t.P1) && seg.Item2.IsAlmostEqualTo(t.P0)))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ObtenerClaveConduit(Document doc, Element el)
        {
            if (el == null) return "";
            double diam = 0;
            try
            {
                Parameter pD = el.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (pD != null && pD.HasValue) diam = pD.AsDouble();
            }
            catch { }
            return el.GetTypeId().ToString() + "|" + Math.Round(diam * 1000.0).ToString();
        }

        private static string ObtenerTextoEstimadoConduit(Document doc, Element el)
        {
            if (el == null) return "";
            string tipo = doc.GetElement(el.GetTypeId())?.Name ?? "";
            string size = "";
            try
            {
                size = el.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString()
                       ?? el.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM)?.AsValueString()
                       ?? "";
            }
            catch { }
            return (tipo + " " + size).Trim();
        }

        /// <summary>
        /// Obtiene los Ids de los elementos (host) etiquetados por un IndependentTag, compatible con varias versiones de Revit.
        /// </summary>
        private static List<ElementId> ObtenerIdsElementosEtiquetados(IndependentTag t)
        {
            var ids = new List<ElementId>();
            if (t == null) return ids;
            dynamic dyn = t;

            try
            {
                var refs = dyn.GetTaggedLocalElementIds();
                if (refs != null)
                {
                    foreach (ElementId id in refs)
                    {
                        if (id != null && id != ElementId.InvalidElementId) ids.Add(id);
                    }
                    if (ids.Count > 0) return ids;
                }
            }
            catch { }

            try
            {
                ElementId locId = dyn.GetTaggedLocalElementId();
                if (locId != null && locId != ElementId.InvalidElementId) { ids.Add(locId); return ids; }
            }
            catch { }

            try
            {
                var refs = dyn.GetTaggedElementIds();
                if (refs != null)
                {
                    foreach (dynamic r in refs)
                    {
                        ElementId hostId = r.HostElementId;
                        if (hostId != null && hostId != ElementId.InvalidElementId) ids.Add(hostId);
                    }
                    if (ids.Count > 0) return ids;
                }
            }
            catch { }

            try
            {
                ElementId hostId = dyn.TaggedLocalElementId;
                if (hostId != null && hostId != ElementId.InvalidElementId) ids.Add(hostId);
            }
            catch { }

            return ids;
        }

        private static ElementId ObtenerTipoTagConduitTypeNameSize(Document doc)
        {
            try
            {
                var tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ConduitTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                var multiTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                tagTypes.AddRange(multiTypes);

                if (tagTypes.Count > 0)
                {
                    // 1. Prioridad: Etiquetas con Type Name / Tipo / Material Y Size / Diámetro / ø
                    var typeSizeMatch = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        bool hasType = n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Tipo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Mat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("EMT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("PVC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("IMC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Alambron", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Alambrón", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("SPT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("SIPRA", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool hasSize = n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Ø", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("\"", StringComparison.OrdinalIgnoreCase) >= 0;
                        return hasType && hasSize;
                    });
                    if (typeSizeMatch != null) return typeSizeMatch.Id;

                    // 2. Prioridad: Etiquetas que contengan "Size", "Diam", "ø" o nombres de material/tipo
                    var sizeOrTypeMatch = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Diam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("ø", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Tipo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("EMT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("PVC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("IMC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               n.IndexOf("Alambr", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (sizeOrTypeMatch != null) return sizeOrTypeMatch.Id;

                    // 3. Prioridad: Etiquetas con prefijo "DC" o "Conduit"
                    var dcConduit = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return (n.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (dcConduit != null) return dcConduit.Id;

                    // 4. Fallback: Primer tag de conduit disponible
                    return tagTypes.First().Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        #endregion
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class MyTAGS_Debug_Inspeccionar : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                while (true)
                {
                    Reference r;
                    try
                    {
                        r = uidoc.Selection.PickObject(ObjectType.Element, "Clic en el elemento a inspeccionar (ESC para salir)");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break;
                    }

                    Element el = doc.GetElement(r.ElementId);
                    if (el == null) continue;

                    string catNombre = el.Category?.Name ?? "SIN CATEGORÍA";
                    int catId = el.Category?.Id.IntegerValue ?? 0;
                    string familia = (el as FamilyInstance)?.Symbol?.Family?.Name ?? "N/A";
                    string tipo = doc.GetElement(el.GetTypeId())?.Name ?? "N/A";
                    string locType = el.Location?.GetType().Name ?? "SIN LOCATION";

                    string curvaInfo = "";
                    if (el.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        curvaInfo = $"\nP0: ({p0.X:F2}, {p0.Y:F2}, {p0.Z:F2})" +
                                    $"\nP1: ({p1.X:F2}, {p1.Y:F2}, {p1.Z:F2})" +
                                    $"\nLongitud: {lc.Curve.Length:F2} ft";
                    }
                    else if (el.Location is LocationPoint lp)
                    {
                        curvaInfo = $"\nPunto: ({lp.Point.X:F2}, {lp.Point.Y:F2}, {lp.Point.Z:F2})";
                    }

                    Autodesk.Revit.UI.TaskDialog.Show("Inspección de Elemento",
                        $"ElementId: {el.Id}\n" +
                        $"Categoría: {catNombre} (BuiltInCategory Id: {catId})\n" +
                        $"Clase: {el.GetType().Name}\n" +
                        $"Familia: {familia}\n" +
                        $"Tipo: {tipo}\n" +
                        $"Location: {locType}" +
                        curvaInfo);
                }
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
            }

            return Result.Succeeded;
        }
    }

    public class ConduitTagSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem == null || elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Conduit;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
