using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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
        public static int TaguearCambioDeNivelPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            FamilySymbol tagSymbol = ObtenerTagConduitAccesorio(doc);
            if (tagSymbol == null)
            {
                ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
                return TaguearCambioDeNivelSpotElevationFallback(uidoc, doc, view, spotTypeId);
            }

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

            // Si no había preselección, permitir selección por ventana (rectángulo) o clics continuos finalizando con ESC
            if (refs.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre los codos de cambio de nivel:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        foreach (var el in rectElements)
                        {
                            refs.Add(new Reference(el));
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (refs.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en codos de cambio de nivel (presiona ESC cuando termines para taguear):"
                            );
                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (!EsCodoCambioDeNivelValido(el, out string motivoRechazo))
                                {
                                    Autodesk.Revit.UI.TaskDialog.Show("Tag C.N - No Aplica",
                                        $"El elemento seleccionado no aplica como cambio de nivel:\n\n{motivoRechazo}");
                                    continue;
                                }

                                if (!refs.Any(r => r.ElementId == pick.ElementId))
                                {
                                    refs.Add(pick);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede a taguear
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (refs.Count == 0) return 0;

            int colocados = 0;
            int omitidos = 0;
            List<string> motivosOmitidos = new List<string>();

            using (Transaction tx = new Transaction(doc, "Tags C.N por Selección (Redes Secas)"))
            {
                tx.Start();

                if (!tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

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

                        XYZ puntoEje = ObtenerPuntoEjeDesdeClic(el, r.GlobalPoint);
                        if (puntoEje == null) continue;

                        XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSymbol.Id,
                            view.Id,
                            r,
                            true,
                            TagOrientation.Horizontal,
                            tagHeadPos
                        );

                        if (tag != null)
                        {
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = tagHeadPos;

                            Parameter pCambio = el.LookupParameter("DC. TAG conduit accesorio")
                                             ?? el.LookupParameter("cambio de nivel")
                                             ?? el.LookupParameter("Cambio de Nivel")
                                             ?? el.LookupParameter("C.N");

                            if (pCambio != null && !pCambio.IsReadOnly && pCambio.StorageType == StorageType.String)
                            {
                                pCambio.Set("Cambio de nivel");
                            }

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

        /// Flujo interactivo por clic para colocar Tags "C.N" en cambios de nivel (codos con diferencia de altura Z).
        /// Al seleccionar cualquiera de los dos codos del cambio de nivel (descartando codos a 90° y giros horizontales en planta),
        /// se valida la elevación y se taguea con la familia "DC- Tag accesorio conduit_URB" / "DC. TAG conduit accesorio".
        public static int TaguearCambioDeNivel(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            int colocados = 0;
            FamilySymbol tagSymbol = ObtenerTagConduitAccesorio(doc);

            if (tagSymbol == null)
            {
                // Fallback a SpotElevation si no existe la familia de tag de accesorio en el proyecto
                ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
                return TaguearCambioDeNivelSpotElevationFallback(uidoc, doc, view, spotTypeId);
            }

            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new CodoCambioDeNivelSelectionFilter(),
                        "Clic en cualquiera de los dos codos del cambio de nivel (ESC para terminar)"
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

                // Posicionamiento del Tag en zona limpia
                XYZ puntoEje = ObtenerPuntoEjeDesdeClic(el, pickRef.GlobalPoint);
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
                        pickRef,
                        true,
                        TagOrientation.Horizontal,
                        tagHeadPos
                    );

                    if (tag != null)
                    {
                        tag.HasLeader = true;
                        tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        tag.TagHeadPosition = tagHeadPos;

                        // Asignar parámetros "DC. TAG conduit accesorio" o "cambio de nivel" si existen en el elemento
                        Parameter pCambio = el.LookupParameter("DC. TAG conduit accesorio")
                                         ?? el.LookupParameter("cambio de nivel")
                                         ?? el.LookupParameter("Cambio de Nivel")
                                         ?? el.LookupParameter("C.N");

                        if (pCambio != null && !pCambio.IsReadOnly && pCambio.StorageType == StorageType.String)
                        {
                            pCambio.Set("Cambio de nivel");
                        }

                        colocados++;
                    }

                    tx.Commit();
                }
            }

            return colocados;
        }

        /// Flujo automático para taguear TODOS los cambios de nivel válidos en la vista activa.
        /// Recolecta los codos de conduit y tuberías, filtra los codos a 90° y giros horizontales,
        /// agrupa pares de codos pertenecientes a la misma bajada/subida (para no duplicar tag)
        /// y coloca el Tag C.N automáticamente.
        public static int TaguearCambioDeNivelTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            var categorias = new[]
            {
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_DuctFitting
            };

            List<Element> codosCandidatos = new List<Element>();
            foreach (var cat in categorias)
            {
                var elems = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();
                codosCandidatos.AddRange(elems);
            }

            if (codosCandidatos.Count == 0) return 0;

            // 1. Filtrar solo codos válidos de cambio de nivel (descartando giros puramente horizontales)
            List<Element> codosValidos = codosCandidatos
                .Where(el => EsCodoCambioDeNivelValido(el, out _))
                .ToList();

            if (codosValidos.Count == 0) return 0;

            FamilySymbol tagSymbol = ObtenerTagConduitAccesorio(doc);
            if (tagSymbol == null) return 0;

            // 2. Identificar qué codos ya tienen específicamente un Tag C.N asignado en la vista (no cualquier tag genérico)
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

                    bool esTagCN = (tagTypeId == tagSymbol.Id) ||
                                   tagFamName.Contains("conduit accesorio") ||
                                   tagFamName.Contains("cambio de nivel") ||
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

            // 3. Agrupar codos por cada cambio de nivel individual (pares de codos a <= 5.5 ft)
            List<List<Element>> gruposCambios = AgruparCodosEnCambiosDeNivel(codosValidos);
            List<Element> codosATaguear = new List<Element>();

            foreach (var grupo in gruposCambios)
            {
                // Si algún codo de este cambio de nivel ya fue tagueado previamente con C.N en la vista, omitir
                if (grupo.Any(c => elementosYaTagueados.Contains(c.Id)))
                {
                    continue;
                }

                // Seleccionar un codo representativo por cada cambio de nivel individual
                Element codoSeleccionado = grupo
                    .OrderBy(c =>
                    {
                        XYZ pt = (c.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        return pt.X * 1000.0 + pt.Y * 10.0 + pt.Z;
                    })
                    .FirstOrDefault();

                if (codoSeleccionado != null)
                {
                    codosATaguear.Add(codoSeleccionado);
                }
            }

            if (codosATaguear.Count == 0) return 0;

            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Tags C.N Automático"))
            {
                tx.Start();

                if (!tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                foreach (var codo in codosATaguear)
                {
                    try
                    {
                        XYZ puntoEje = ObtenerPuntoCentro(codo, out _) ?? (codo.Location as LocationPoint)?.Point;
                        if (puntoEje == null) continue;

                        XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSymbol.Id,
                            view.Id,
                            new Reference(codo),
                            true,
                            TagOrientation.Horizontal,
                            tagHeadPos
                        );

                        if (tag != null)
                        {
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = tagHeadPos;

                            Parameter pCambio = codo.LookupParameter("DC. TAG conduit accesorio")
                                             ?? codo.LookupParameter("cambio de nivel")
                                             ?? codo.LookupParameter("Cambio de Nivel")
                                             ?? codo.LookupParameter("C.N");

                            if (pCambio != null && !pCambio.IsReadOnly && pCambio.StorageType == StorageType.String)
                            {
                                pCambio.Set("Cambio de nivel");
                            }

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
        /// Agrupa los codos que pertenecen al MISMO cambio de nivel (unidos directamente o por un tramo corto de transición <= 5.5 pies).
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
                                    if (pActual == null || pOwner == null || pActual.DistanceTo(pOwner) <= 5.5)
                                    {
                                        visitados.Add(owner.Id);
                                        cola.Enqueue(owner);
                                    }
                                }
                                // Caso 2: Conexión a través de un tramo corto de tubería/conduit inclinado (longitud <= 5.5 pies)
                                else if (owner.Location is LocationCurve lc && lc.Curve != null && lc.Curve.Length <= 5.5)
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
                                                    if (pActual == null || pVecino == null || pActual.DistanceTo(pVecino) <= 5.5)
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
        /// Determina si un elemento es una caja eléctrica/derivación, toma, interruptor, tablero o dispositivo terminal.
        /// </summary>
        public static bool EsCajaODispositivoElemento(Element e)
        {
            if (e == null || e.Category == null) return false;

            int catId = e.Category.Id.IntegerValue;
            if (catId == (int)BuiltInCategory.OST_ElectricalFixtures ||
                catId == (int)BuiltInCategory.OST_LightingDevices ||
                catId == (int)BuiltInCategory.OST_LightingFixtures ||
                catId == (int)BuiltInCategory.OST_ElectricalEquipment ||
                catId == (int)BuiltInCategory.OST_CommunicationDevices ||
                catId == (int)BuiltInCategory.OST_DataDevices ||
                catId == (int)BuiltInCategory.OST_FireAlarmDevices ||
                catId == (int)BuiltInCategory.OST_SecurityDevices ||
                catId == (int)BuiltInCategory.OST_PlumbingFixtures ||
                catId == (int)BuiltInCategory.OST_MechanicalEquipment)
            {
                return true;
            }

            // Si es un fitting, verificar si su PartType es JunctionBox
            if (e is FamilyInstance fi && fi.MEPModel != null)
            {
                try
                {
                    System.Reflection.PropertyInfo piPartType = fi.MEPModel.GetType().GetProperty("PartType");
                    if (piPartType != null)
                    {
                        object partTypeVal = piPartType.GetValue(fi.MEPModel);
                        if (partTypeVal != null && partTypeVal.ToString().Equals("JunctionBox", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch { }
            }

            string famName = (e as FamilyInstance)?.Symbol?.Family?.Name?.ToLower() ?? "";
            if (famName.Contains("caja") || famName.Contains("junction box") || famName.Contains("condulet") || famName.Contains("pull box"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verifica si el accesorio/codo es o está conectado directamente (o por un tramo corto de bajada/subida) a una caja o dispositivo terminal.
        /// </summary>
        public static bool EstaConectadoACajaODispositivo(Element fitting)
        {
            if (fitting == null) return false;

            if (EsCajaODispositivoElemento(fitting)) return true;

            // Revisar conexiones vía conectores MEP
            if (fitting is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        Element owner = refConn.Owner;
                        if (owner == null || owner.Id == fitting.Id) continue;

                        if (EsCajaODispositivoElemento(owner)) return true;

                        // Si el conector va a un tramo de tubería/conduit (MEPCurve)
                        if (owner is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
                        {
                            foreach (Connector pipeConn in mepCurve.ConnectorManager.Connectors)
                            {
                                foreach (Connector pipeRef in pipeConn.AllRefs)
                                {
                                    Element nextOwner = pipeRef.Owner;
                                    if (nextOwner == null || nextOwner.Id == fitting.Id || nextOwner.Id == owner.Id) continue;

                                    if (EsCajaODispositivoElemento(nextOwner)) return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Valida si el elemento seleccionado es un accesorio MEP (Fitting / Codo) que representa un cambio de nivel real.
        /// Descarta:
        /// 1. Tramos de tubos/conduits rectos (MEPCurve).
        /// 2. Cajas de paso / derivación y conexiones directas a cajas.
        /// 3. Codos de 90° (giros a escuadra / bajadas a tablero).
        /// 4. "Caballitos" / saltos de cruce (donde el circuito salta temporalmente un obstáculo y regresa al mismo nivel original).
        /// 5. Giros horizontales en planta y pendientes mínimas sin desnivel vertical real (< 4.5 cm).
        /// </summary>
        public static bool EsCodoCambioDeNivelValido(Element el, out string motivoRechazo)
        {
            motivoRechazo = string.Empty;
            if (el == null)
            {
                motivoRechazo = "Elemento nulo";
                return false;
            }

            // 1. Debe ser un accesorio MEP (Fitting / FamilyInstance), NO una tubería/conduit recta (MEPCurve)
            if (el is MEPCurve || (el.Category != null && (
                el.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Conduit ||
                el.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves ||
                el.Category.Id.IntegerValue == (int)BuiltInCategory.OST_CableTray ||
                el.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DuctCurves)))
            {
                motivoRechazo = "Debes seleccionar un codo / accesorio (Fitting), no un tramo recto de tubería o conduit.";
                return false;
            }

            if (el.Category == null)
            {
                motivoRechazo = "El elemento no tiene categoría válida.";
                return false;
            }

            int catId = el.Category.Id.IntegerValue;
            bool esFitting = catId == (int)BuiltInCategory.OST_ConduitFitting ||
                             catId == (int)BuiltInCategory.OST_PipeFitting ||
                             catId == (int)BuiltInCategory.OST_CableTrayFitting ||
                             catId == (int)BuiltInCategory.OST_DuctFitting;

            if (!esFitting || !(el is FamilyInstance fiElem))
            {
                motivoRechazo = "Debes seleccionar un codo / accesorio de cambio de nivel (Fitting).";
                return false;
            }

            // 2. Descartar Cajas de Paso / Derivación / Aparatos y conexiones directas a cajas
            if (EstaConectadoACajaODispositivo(el))
            {
                motivoRechazo = "Las conexiones/bajadas a cajas, tomas o dispositivos no se taguean como cambio de nivel.";
                return false;
            }

            // 3. Descartar Codos de 90° (giros a escuadra, bajadas a pared/tableros)
            if (EsCodoDe90Grados(fiElem))
            {
                motivoRechazo = "No aplica: Es un codo a 90° (los cambios de nivel se realizan con codos de 15°, 30°, 45° o 60°).";
                return false;
            }

            // 4. Descartar "Caballitos" / saltos de cruce (bypass temporal que esquiva un obstáculo y regresa al mismo nivel original)
            if (EsCaballitoOSaltoDeCruce(fiElem))
            {
                motivoRechazo = "No aplica: Es un 'caballito' o salto de cruce (el conduit regresa a su nivel original).";
                return false;
            }

            // 5. Verificar si hay un cambio de altura / elevación Z real (mínimo 0.15 ft ≈ 4.5 cm / 50 mm)
            double umbralMinimoZ = 0.15; // en pies (~4.5 cm)
            bool presentaCambioZ = false;

            if (fiElem.MEPModel?.ConnectorManager != null)
            {
                var connectors = fiElem.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
                List<double> elevaciones = new List<double>();

                foreach (Connector conn in connectors)
                {
                    elevaciones.Add(conn.Origin.Z);

                    // Componente vertical del conector (inclinación > ~8.5 grados)
                    if (conn.CoordinateSystem != null && Math.Abs(conn.CoordinateSystem.BasisZ.Z) >= 0.15)
                    {
                        presentaCambioZ = true;
                    }

                    // Analizar las tuberías/conduits conectados al accesorio
                    foreach (Connector refConn in conn.AllRefs)
                    {
                        if (refConn.Owner != null && refConn.Owner.Id != el.Id)
                        {
                            Element owner = refConn.Owner;
                            if (owner.Location is LocationCurve lc && lc.Curve != null)
                            {
                                XYZ p0 = lc.Curve.GetEndPoint(0);
                                XYZ p1 = lc.Curve.GetEndPoint(1);
                                if (Math.Abs(p1.Z - p0.Z) >= umbralMinimoZ)
                                {
                                    presentaCambioZ = true;
                                }
                                elevaciones.Add(p0.Z);
                                elevaciones.Add(p1.Z);
                            }
                            else if (owner is FamilyInstance fiVecino && fiVecino.MEPModel?.ConnectorManager != null)
                            {
                                foreach (Connector cVecino in fiVecino.MEPModel.ConnectorManager.Connectors)
                                {
                                    elevaciones.Add(cVecino.Origin.Z);
                                }
                            }
                        }
                    }
                }

                if (elevaciones.Count > 1)
                {
                    double minZ = elevaciones.Min();
                    double maxZ = elevaciones.Max();
                    if (maxZ - minZ >= umbralMinimoZ)
                    {
                        presentaCambioZ = true;
                    }
                }
            }

            if (!presentaCambioZ)
            {
                motivoRechazo = "No presenta cambio de nivel ni desnivel vertical en Z (giro horizontal en planta o pendiente mínima).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determina si un accesorio/codo es de 90 grados (giro a escuadra o bajada perpendicular).
        /// </summary>
        public static bool EsCodoDe90Grados(FamilyInstance fi)
        {
            if (fi == null) return false;

            // 1. Verificar parámetro de ángulo si existe
            Parameter pAng = fi.LookupParameter("Angle")
                           ?? fi.LookupParameter("Ángulo")
                           ?? fi.LookupParameter("Angulo")
                           ?? fi.LookupParameter("Angle 1")
                           ?? fi.LookupParameter("Angle 2");

            if (pAng != null && pAng.StorageType == StorageType.Double)
            {
                double rad = pAng.AsDouble();
                double deg = rad * (180.0 / Math.PI);
                if (deg >= 80.0 && deg <= 100.0) return true;
            }

            // 2. Verificar ángulo geométrico entre conectores MEP
            if (fi.MEPModel?.ConnectorManager != null)
            {
                var conns = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
                if (conns.Count >= 2)
                {
                    XYZ v1 = conns[0].CoordinateSystem?.BasisZ;
                    XYZ v2 = conns[1].CoordinateSystem?.BasisZ;
                    if (v1 != null && v2 != null && !v1.IsZeroLength() && !v2.IsZeroLength())
                    {
                        double dot = Math.Max(-1.0, Math.Min(1.0, v1.Normalize().DotProduct(v2.Normalize())));
                        double angDeg = Math.Acos(dot) * (180.0 / Math.PI);
                        if (angDeg >= 80.0 && angDeg <= 100.0) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Determina si un codo forma parte de un "caballito" / salto de cruce (img 1).
        /// Un caballito es un puente o salto temporal para esquivar una tubería/viga,
        /// donde los tramos principales antes y después del salto continúan al MISMO nivel Z.
        /// </summary>
        public static bool EsCaballitoOSaltoDeCruce(FamilyInstance fi)
        {
            if (fi?.MEPModel?.ConnectorManager == null) return false;

            var connectors = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
            if (connectors.Count < 2) return false;

            List<double> zExtremos = new List<double>();
            double zCodo = fi.Location is LocationPoint lp ? lp.Point.Z : connectors[0].Origin.Z;

            foreach (Connector conn in connectors)
            {
                double zExtremo = ObtenerElevacionTramoPrincipal(conn, fi.Id, 4, 10.0, out _);
                if (!double.IsNaN(zExtremo))
                {
                    zExtremos.Add(zExtremo);
                }
            }

            if (zExtremos.Count == 2)
            {
                double diffExtremos = Math.Abs(zExtremos[0] - zExtremos[1]);
                double alturaSalto = Math.Abs(zCodo - zExtremos[0]);

                // Si ambos extremos continúan en el MISMO nivel Z (diferencia < 2.5 cm / 0.08 ft)
                // y el codo o salto tiene una diferencia vertical respecto a la línea principal
                if (diffExtremos < 0.08 && (alturaSalto >= 0.10 || TieneInclinacionVertical(fi)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TieneInclinacionVertical(FamilyInstance fi)
        {
            if (fi?.MEPModel?.ConnectorManager == null) return false;
            foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
            {
                if (c.CoordinateSystem != null && Math.Abs(c.CoordinateSystem.BasisZ.Z) >= 0.15)
                {
                    return true;
                }
            }
            return false;
        }

        private static double ObtenerElevacionTramoPrincipal(Connector startConn, ElementId originId, int maxProfundidad, double maxDistanciaFt, out ElementId levelId)
        {
            levelId = ElementId.InvalidElementId;
            double distAcumulada = 0;
            Connector currConn = startConn;
            ElementId lastId = originId;

            for (int step = 0; step < maxProfundidad; step++)
            {
                Connector nextStepConn = null;
                foreach (Connector refConn in currConn.AllRefs)
                {
                    Element owner = refConn.Owner;
                    if (owner == null || owner.Id == lastId) continue;

                    if (owner is MEPCurve mepCurve)
                    {
                        if (mepCurve.ReferenceLevel != null)
                        {
                            levelId = mepCurve.ReferenceLevel.Id;
                        }
                        else if (mepCurve.LevelId != null && mepCurve.LevelId != ElementId.InvalidElementId)
                        {
                            levelId = mepCurve.LevelId;
                        }

                        if (mepCurve.Location is LocationCurve lc && lc.Curve != null)
                        {
                            double len = lc.Curve.Length;
                            distAcumulada += len;
                            XYZ p0 = lc.Curve.GetEndPoint(0);
                            XYZ p1 = lc.Curve.GetEndPoint(1);

                            // Si este tramo es horizontal y largo (> 2 ft) o sobrepasa la distancia del salto
                            if (Math.Abs(p1.Z - p0.Z) < 0.05 && (len > 2.0 || distAcumulada > maxDistanciaFt))
                            {
                                return (p0.Z + p1.Z) / 2.0;
                            }

                            if (mepCurve.ConnectorManager != null)
                            {
                                foreach (Connector nextConn in mepCurve.ConnectorManager.Connectors)
                                {
                                    if (nextConn.Id != refConn.Id)
                                    {
                                        nextStepConn = nextConn;
                                        lastId = mepCurve.Id;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else if (owner is FamilyInstance nextFi && nextFi.MEPModel?.ConnectorManager != null)
                    {
                        if (nextFi.LevelId != null && nextFi.LevelId != ElementId.InvalidElementId)
                        {
                            levelId = nextFi.LevelId;
                        }

                        lastId = nextFi.Id;
                        foreach (Connector nextConn in nextFi.MEPModel.ConnectorManager.Connectors)
                        {
                            if (nextConn.Id != refConn.Id)
                            {
                                nextStepConn = nextConn;
                                break;
                            }
                        }
                    }

                    if (nextStepConn != null) break;
                }

                if (nextStepConn == null) break;
                currConn = nextStepConn;
            }

            return double.NaN;
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
                     s.Category.Name.ToLower().Contains("tag")) &&
                    (s.Name.IndexOf("Cambio de nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Name.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0));

                if (coincidenciaNombre != null) return coincidenciaNombre;

                // 3. Fallback a cualquier tag de ConduitFitting / accesorio
                var fallbackConduitTag = tagSymbols.FirstOrDefault(s =>
                    s.Category != null && s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags);

                return fallbackConduitTag ?? tagSymbols.FirstOrDefault(s => s.Category != null && s.Category.Name.ToLower().Contains("tag"));
            }
            catch
            {
                return null;
            }
        }

        private class CodoCambioDeNivelSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                int catId = e.Category.Id.IntegerValue;
                bool esFitting = catId == (int)BuiltInCategory.OST_ConduitFitting ||
                                 catId == (int)BuiltInCategory.OST_PipeFitting;
                return esFitting;
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

            // 2. Selección por recuadro o clics continuos finalizando con ESC
            if (elementos.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre las tomas/conduits y muros a acotar:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        elementos.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (elementos.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en tomas/conduits o muros a acotar (presiona ESC cuando termines para colocar las cotas alineadas):"
                            );
                            if (pick != null)
                            {
                                Element e = doc.GetElement(pick.ElementId);
                                if (e != null && !elementos.Any(x => x.Id == e.Id))
                                {
                                    elementos.Add(e);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            break;
                        }
                    }
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
                        collisionCtx
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
        /// y coloca cotas de nivel calculadas automáticamente en los elementos seleccionados.
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

            // 1. Revisar si el usuario ya tenía elementos seleccionados previamente en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el?.Category != null)
                    {
                        int cId = el.Category.Id.IntegerValue;
                        if (cId == (int)BuiltInCategory.OST_PipeCurves ||
                            cId == (int)BuiltInCategory.OST_Conduit ||
                            cId == (int)BuiltInCategory.OST_CableTray ||
                            cId == (int)BuiltInCategory.OST_DuctCurves)
                        {
                            elementos.Add(el);
                        }
                    }
                }
            }

            // 2. Si no había preselección, solicitar selección por ventana de arrastre o clics
            if (elementos.Count == 0)
            {
                // Intento 1: Selección por ventana de arrastre (Rectángulo) - termina automáticamente al soltar el ratón
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        new MepElementSelectionFilter(),
                        "Arrastra un recuadro sobre las tuberías/conduits a acotar:"
                    );

                    if (rectElements != null && rectElements.Count > 0)
                    {
                        elementos.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Si el usuario cancela con ESC, no es error
                }
                catch { }

                // Intento 2 (Fallback): Si no usó ventana, permitir clics individuales finalizando con tecla ESC
                if (elementos.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                new MepElementSelectionFilter(),
                                "Clic en tuberías a acotar (presiona ESC cuando termines para acotarlas):"
                            );

                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (el != null && !elementos.Any(x => x.Id == el.Id))
                                {
                                    elementos.Add(el);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede a acotar inmediatamente
                        }
                        catch
                        {
                            break;
                        }
                    }
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
                            doc, view, tramoSeleccionado.Elemento, puntoMedioCorrida, tramoSeleccionado.DirCurva, spotTypeId, nivelRef, collisionCtx, logFallosSpot);

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
            List<string> logDebug = null)
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
                ConfigurarSpotElevation(spot, nivelTecho, doc);

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
            List<string> logDebug = null)
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
                ConfigurarSpotElevation(spot, nivelTecho, doc);

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
        private static void ConfigurarSpotElevation(SpotDimension spot, Level nivelTecho, Document doc)
        {
            if (spot == null) return;

            // 1. Asignar Relative Base (Nivel del techo/losa)
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

            // 2. Asignar Display Elevations = Bottom Elevation (2)
            try
            {
                Parameter pDisplay = spot.get_Parameter(BuiltInParameter.SPOT_ELEV_DISPLAY_ELEVATIONS)
                                  ?? spot.LookupParameter("Display Elevations")
                                  ?? spot.LookupParameter("Mostrar elevaciones");

                if (pDisplay != null && !pDisplay.IsReadOnly)
                {
                    // 2 = Bottom Elevation en Revit
                    pDisplay.Set(2);

                    try
                    {
                        string val = pDisplay.AsValueString();
                        if (val == null || (!val.ToLower().Contains("bottom") && !val.ToLower().Contains("inferior")))
                        {
                            pDisplay.SetValueString("Bottom Elevation");
                        }
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

            List<Element> conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (conduits.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Camas de Conduits", "No se encontraron Conduits en la vista activa.");
                return 0;
            }

            return ProcesarYColocarTagsCamasConduits(doc, view, conduits);
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

            // 1. Extraer información geométrica de cada conduit
            List<ConduitInfo> listInfo = new List<ConduitInfo>();
            foreach (var c in conduits)
            {
                if (c == null) continue;
                if (c.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    double len2D = v2D.GetLength();
                    if (len2D < 0.5) continue; // Descartar conduits verticales o muy cortos

                    XYZ dir2D = v2D / len2D;
                    // Unificar orientación del vector director
                    if (dir2D.X < -1e-4 || (Math.Abs(dir2D.X) <= 1e-4 && dir2D.Y < -1e-4))
                    {
                        dir2D = -dir2D;
                    }

                    double outerDiam = 0.15; // default ~2 inches
                    try
                    {
                        Parameter pDiam = c.get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM)
                                       ?? c.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                       ?? c.LookupParameter("Diameter")
                                       ?? c.LookupParameter("Diámetro");
                        if (pDiam != null && pDiam.HasValue && pDiam.AsDouble() > 0)
                        {
                            outerDiam = pDiam.AsDouble();
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
                        Length2D = len2D,
                        ElevationZ = mid.Z,
                        OuterDiameter = outerDiam
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
                        if (Math.Abs(c1.Dir2D.DotProduct(c2.Dir2D)) >= 0.94) // Paralelos en planta (< 20°)
                        {
                            bool esCercano = false;
                            XYZ perp = new XYZ(-c1.Dir2D.Y, c1.Dir2D.X, 0);

                            foreach (var member in cluster)
                            {
                                double distPerp = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(perp));
                                double distAlong = Math.Abs((c2.MidPoint - member.MidPoint).DotProduct(c1.Dir2D));

                                // Separación lateral entre tubos del banco <= 2.8 ft y solapamiento longitudinal
                                if (distPerp <= 2.8 && distAlong <= (member.Length2D * 0.75 + c2.Length2D * 0.75))
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

                // Agrupar por niveles de Z (tolerancia 0.35 ft ≈ 10 cm)
                foreach (var c in cluster)
                {
                    var camaExistente = bank.Camas.FirstOrDefault(cm => Math.Abs(cm.ElevationZ - c.ElevationZ) <= 0.35);
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

            // 4. Obtener tipo de Tag para Conduits
            ElementId tagTypeId = ObtenerTipoTagCamasConduits(doc);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Camas de Conduits", "No se encontró ningún tipo de etiqueta de Conduit (OST_ConduitTags) cargado en el proyecto.");
                return 0;
            }

            var tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;

            // 5. Crear etiquetas y asignar comentarios en transacción
            int totalTagsCreados = 0;
            using (Transaction tx = new Transaction(doc, "Taguear Camas de Conduits (Redes Secas)"))
            {
                tx.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
                // Distancia ajustada a pocos milímetros al lado de la tubería (~3.0 mm en plano)
                double margenMm = (3.0 / 1000.0 * scale) * 3.28084;
                double gapEntreTags = (4.5 / 1000.0 * scale) * 3.28084; // separación si hay múltiples camas

                foreach (var bank in banks)
                {
                    int numCamas = bank.Camas.Count;

                    // Orientación del tag según el sentido del conduit (vertical u horizontal)
                    bool esVerticalEnPlanta = Math.Abs(bank.Direction2D.Y) > Math.Abs(bank.Direction2D.X);
                    TagOrientation orientacionTag = esVerticalEnPlanta ? TagOrientation.Vertical : TagOrientation.Horizontal;

                    XYZ perp = bank.Perp2D;
                    if (esVerticalEnPlanta)
                    {
                        // Para conduits verticales, colocar preferentemente a la izquierda (-X)
                        if (perp.X > 0.1) perp = -perp;
                    }
                    else
                    {
                        // Para conduits horizontales, colocar preferentemente arriba (+Y)
                        if (perp.Y < -0.1) perp = -perp;
                    }

                    // Calcular el semiancho del banco de conduits en dirección perpendicular
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
                    if (maxRadioBank < 0.10) maxRadioBank = 0.10;

                    // Offset base pegado al borde exterior de los conduits con margen de unos milímetros
                    double offsetBase = maxRadioBank + margenMm;

                    for (int i = 0; i < numCamas; i++)
                    {
                        var cama = bank.Camas[i];
                        int countTubos = cama.Conduits.Count;
                        // Solo colocar el número de tubos entre paréntesis si hay 2 o más conduits en la cama; si es 1, se deja vacío para no mostrar (1)
                        string valorComentario = countTubos > 1 ? $"({countTubos})-" : string.Empty;

                        // Asignar comentario a los conduits de esta cama para que la etiqueta tome la cantidad
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
                            }
                            catch { }
                        }

                        // Posición del tag a unos milímetros al lado de la tubería
                        double offsetCama = offsetBase + (i * gapEntreTags);
                        XYZ tagHeadPos = bank.Center2D + (perp * offsetCama);

                        // Elemento host representativo para la directriz del tag
                        ConduitInfo hostConduit = cama.Conduits.OrderBy(c => (c.MidPoint - tagHeadPos).GetLength()).FirstOrDefault() ?? cama.Conduits.First();

                        try
                        {
                            IndependentTag tag = IndependentTag.Create(
                                doc,
                                tagTypeId,
                                view.Id,
                                new Reference(hostConduit.Element),
                                true, // con líder
                                orientacionTag, // Vertical si el conduit va vertical, Horizontal si va horizontal
                                tagHeadPos
                            );

                            if (tag != null)
                            {
                                if (tagTypeId != ElementId.InvalidElementId)
                                {
                                    try { tag.ChangeTypeId(tagTypeId); } catch { }
                                }

                                tag.HasLeader = true;
                                tag.LeaderEndCondition = LeaderEndCondition.Attached;
                                tag.TagHeadPosition = tagHeadPos;

                                totalTagsCreados++;
                            }
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            return totalTagsCreados;
        }

        private static ElementId ObtenerTipoTagCamasConduits(Document doc)
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

                    // 3. Prioridad: "DC - Tag Conduit", "DC. TAG conduit", o "Tag Conduit"
                    var dcConduitType = tagTypes.FirstOrDefault(t =>
                    {
                        string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                        return (n.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               n.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (dcConduitType != null) return dcConduitType.Id;

                    // 4. Fallback: Primer tag disponible
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
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_Conduit || catId == (int)BuiltInCategory.OST_ConduitRun;
            }
            public bool AllowReference(Reference reference, XYZ position) => true;
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
}
