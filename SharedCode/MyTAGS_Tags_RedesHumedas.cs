using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    /// <summary>
    /// Módulo encargado de la colocación de etiquetas y cotas en Redes Húmedas (Desagües / Tuberías):
    /// 1. Tag Pendientes (Spot Slope / OST_SpotSlopes)
    /// 2. Tag Material
    /// 3. Tag Cambio de Nivel (Desagües)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_Tags_RedesHumedas : IExternalCommand
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

            int colocados = TaguearPendientePorClic(uidoc, doc, view);
            if (colocados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes", $"Se colocaron {colocados} cotas de pendiente.");
            }
            return Result.Succeeded;
        }

        // =========================================================================
        // 1. TAG PENDIENTES (DESAGÜES / TUBERÍAS) - SPOT SLOPE
        // =========================================================================

        /// <summary>
        /// Flujo por clic para colocar cotas de pendiente Spot Slope (o tag SPOT) en tuberías.
        /// Lanza la herramienta nativa de Spot Slope de Revit (Modify | Spot Slopes) para máxima precisión y control nativo.
        /// </summary>
        public static int TaguearPendientePorClic(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            try
            {
                RevitCommandId slopeCmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.SpotSlope);
                if (slopeCmdId != null && uidoc.Application.CanPostCommand(slopeCmdId))
                {
                    uidoc.Application.PostCommand(slopeCmdId);
                    return 1;
                }
            }
            catch { }

            // Fallback interactivo por clic en caso de que PostCommand no esté disponible
            ElementId spotSlopeTypeId = ObtenerTipoSpotSlope(doc);
            var filter = new PipeSelectionFilter();
            int creados = 0;

            while (true)
            {
                try
                {
                    Reference pick = uidoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        filter,
                        "Clic en tubería para colocar Cota de Pendiente Spot Slope (Presiona ESC cuando termines):"
                    );

                    if (pick == null) break;

                    Element elem = doc.GetElement(pick);
                    if (elem == null) continue;

                    XYZ puntoMitad;
                    XYZ dirFlujo;
                    if (!ObtenerDireccionFlujoAgua(elem, out puntoMitad, out dirFlujo, out double _))
                    {
                        if (!EsTuberiaAptaParaSeleccion(elem, out puntoMitad, out dirFlujo))
                        {
                            continue;
                        }
                    }

                    XYZ puntoTag = puntoMitad;
                    if (pick.GlobalPoint != null)
                    {
                        puntoTag = new XYZ(pick.GlobalPoint.X, pick.GlobalPoint.Y, puntoMitad.Z);
                    }

                    using (Transaction tx = new Transaction(doc, "Colocar Spot Slope por Clic"))
                    {
                        tx.Start();
                        SpotDimension spot = ColocarSpotSlope(doc, view, elem, pick, puntoTag, dirFlujo, spotSlopeTypeId);
                        if (spot != null)
                        {
                            creados++;
                        }
                        tx.Commit();
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break; // Presionar ESC termina el comando limpiamente
                }
                catch
                {
                    break;
                }
            }

            return creados;
        }

        /// <summary>
        /// Permite al usuario seleccionar múltiples tuberías en pantalla mediante cursor +/- (Finish) o recuadro
        /// y coloca en cada tubería la cota de pendiente nativa Spot Slope (tipo Sloped).
        /// </summary>
        public static int TaguearPendientePorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Reference> refsList = new List<Reference>();
            var filter = new PipeSelectionFilter();

            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        refsList.Add(new Reference(el));
                    }
                }
            }

            // 2. Si no había preselección, permitir selección con PickObjects (+, -, Finish) o recuadro
            if (refsList.Count == 0)
            {
                try
                {
                    var picked = uidoc.Selection.PickObjects(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        filter,
                        "Selecciona las tuberías a colocar Cota de Pendiente (Spot Slope) y haz clic en 'Finish':"
                    );
                    if (picked != null && picked.Count > 0)
                    {
                        refsList.AddRange(picked);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (refsList.Count == 0)
                {
                    try
                    {
                        var rectElements = uidoc.Selection.PickElementsByRectangle(
                            filter,
                            "Arrastra un recuadro sobre las tuberías a colocar Spot Slope:"
                        );
                        if (rectElements != null && rectElements.Count > 0)
                        {
                            foreach (var el in rectElements)
                            {
                                refsList.Add(new Reference(el));
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                    catch { }
                }
            }

            if (refsList.Count == 0) return 0;

            ElementId spotSlopeTypeId = ObtenerTipoSpotSlope(doc);

            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Colocar Spot Slopes por Selección"))
            {
                tx.Start();

                foreach (Reference r in refsList)
                {
                    try
                    {
                        Element elem = doc.GetElement(r);
                        if (elem == null) continue;

                        XYZ puntoMitad;
                        XYZ dirFlujo;
                        if (!ObtenerDireccionFlujoAgua(elem, out puntoMitad, out dirFlujo, out double _))
                        {
                            if (!EsTuberiaAptaParaSeleccion(elem, out puntoMitad, out dirFlujo))
                            {
                                continue;
                            }
                        }

                        SpotDimension spot = ColocarSpotSlope(doc, view, elem, r, puntoMitad, dirFlujo, spotSlopeTypeId);
                        if (spot != null)
                        {
                            creados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (creados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes Spot Slope",
                    $"Se colocaron exitosamente {creados} cotas de pendiente (Spot Slope).");
            }
            else
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes Spot Slope",
                    "No se pudieron colocar cotas de pendiente en las tuberías seleccionadas. Verifica que las tuberías tengan pendiente configurada en la vista.");
            }

            return creados;
        }

        /// <summary>
        /// Valida si el elemento es una tubería apta para etiquetar por selección manual en la vista.
        /// </summary>
        private static bool EsTuberiaAptaParaSeleccion(Element e, out XYZ puntoMitad, out XYZ dirCurva)
        {
            puntoMitad = null;
            dirCurva = XYZ.BasisX;

            if (e == null || e.Category == null) return false;

#if REVIT2024_OR_LATER
            if (e.Category.Id.Value != (long)BuiltInCategory.OST_PipeCurves) return false;
#else
            if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) return false;
#endif

            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                if (lc.Curve.Length < 0.1) return false; // Al menos 3 cm de longitud

                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                puntoMitad = (p0 + p1) / 2.0;

                XYZ v = p1 - p0;
                if (!v.IsZeroLength())
                {
                    dirCurva = v.Normalize();
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Flujo totalmente automático para etiquetar la pendiente en el punto medio
        /// de todas las tuberías con pendiente real (Slope > 0%) en la vista activa.
        /// Utiliza la familia 'SPOT' (IndependentTag / PipeTag).
        /// Garantiza colocar SOLO 1 ETIQUETA por línea/tramo continuo (incluso si está dividida por codos/accesorios/tes)
        /// y omite tuberías o redes que ya tengan etiquetas en la vista activa.
        /// </summary>
        public static int TaguearPendienteTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId slopeTagTypeId = ObtenerTipoPipeTagPendiente(doc);

            List<Element> tuberiasEnVista = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Where(e => EsTuberiaConPendienteValida(e, out _, out _))
                .ToList();

            if (tuberiasEnVista.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes", "No se encontraron tuberías con pendiente (Slope > 0%) en la vista activa.");
                return 0;
            }

            // 1. Recolectar elementos que ya tienen etiquetas (IndependentTag) en la vista activa
            HashSet<ElementId> elementosEtiquetados = new HashSet<ElementId>();
            try
            {
                var tagsExistentes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tagsExistentes)
                {
                    try
                    {
                        ElementId taggedId = GetTaggedHostElementId(tag);
                        if (taggedId != null && taggedId != ElementId.InvalidElementId)
                        {
                            elementosEtiquetados.Add(taggedId);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Agrupar la red conectada por BFS y seleccionar tuberías representativas colineales
            HashSet<ElementId> visitados = new HashSet<ElementId>();
            List<Element> tuberiasATaguear = new List<Element>();

            foreach (var tuberia in tuberiasEnVista)
            {
                if (visitados.Contains(tuberia.Id)) continue;

                // Recolectar toda la red conectada mediante BFS de conectores
                List<Element> redCompleta = RecolectarTuberiasDeRedCompletaConCodos(tuberia, tuberiasEnVista);

                // Si alguna tubería de la red conectada ya tiene etiqueta en la vista, omitir toda la red
                bool redYaTieneTag = false;
                foreach (var item in redCompleta)
                {
                    if (elementosEtiquetados.Contains(item.Id))
                    {
                        redYaTieneTag = true;
                    }
                }

                if (redYaTieneTag)
                {
                    foreach (var item in redCompleta)
                    {
                        visitados.Add(item.Id);
                    }
                    continue;
                }

                // Filtrar solo tuberías con pendiente válida dentro de esta red
                var tuberiasValidasRed = redCompleta.Where(e => EsTuberiaConPendienteValida(e, out _, out _)).ToList();
                if (tuberiasValidasRed.Count == 0)
                {
                    foreach (var item in redCompleta) visitados.Add(item.Id);
                    continue;
                }

                // Agrupar las tuberías de esta red en tramos colineales (mismo eje)
                List<List<Element>> gruposColineales = AgruparTuberiasColineales(tuberiasValidasRed);

                foreach (var grupo in gruposColineales)
                {
                    foreach (var elem in grupo) visitados.Add(elem.Id);

                    // Si alguna tubería de este grupo colineal ya tiene etiqueta, omitir
                    if (grupo.Any(e => elementosEtiquetados.Contains(e.Id))) continue;

                    // Seleccionar la tubería más larga del tramo colineal
                    Element principal = grupo
                        .OrderByDescending(e => ((e.Location as LocationCurve)?.Curve?.Length ?? 0))
                        .FirstOrDefault();

                    if (principal != null)
                    {
                        tuberiasATaguear.Add(principal);
                    }
                }

                foreach (var item in redCompleta) visitados.Add(item.Id);
            }

            if (tuberiasATaguear.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes Automático",
                    "No se agregaron nuevos tags (las tuberías con pendiente ya están etiquetadas o no requieren tag).");
                return 0;
            }

            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Tags Pendientes Automático (Redes Húmedas)"))
            {
                tx.Start();

                foreach (var tuberiaPrincipal in tuberiasATaguear)
                {
                    try
                    {
                        if (!ObtenerDireccionFlujoAgua(tuberiaPrincipal, out XYZ puntoMitad, out XYZ dirFlujo, out double _))
                        {
                            continue;
                        }

                        Reference pipeRef = new Reference(tuberiaPrincipal);

                        bool esVertical = Math.Abs(dirFlujo.Y) > Math.Abs(dirFlujo.X);
                        TagOrientation orientacion = esVertical ? TagOrientation.Vertical : TagOrientation.Horizontal;

                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            view.Id,
                            pipeRef,
                            false, // sin líder
                            TagMode.TM_ADDBY_CATEGORY,
                            orientacion,
                            puntoMitad
                        );

                        if (newTag != null)
                        {
                            if (slopeTagTypeId != null && slopeTagTypeId != ElementId.InvalidElementId)
                            {
                                try { newTag.ChangeTypeId(slopeTagTypeId); } catch { }
                            }

                            // Si el flujo de agua va hacia la izquierda/abajo, voltear el tag 180°
                            bool requiereVolteo = (dirFlujo.X < -0.001) || (Math.Abs(dirFlujo.X) <= 0.001 && dirFlujo.Y < -0.001);
                            if (requiereVolteo)
                            {
                                try
                                {
                                    Line ejeRotacion = Line.CreateBound(puntoMitad, puntoMitad + XYZ.BasisZ);
                                    ElementTransformUtils.RotateElement(doc, newTag.Id, ejeRotacion, Math.PI);
                                }
                                catch { }
                            }

                            creados++;
                            elementosEtiquetados.Add(tuberiaPrincipal.Id);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (creados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Pendientes Automático",
                    $"Se etiquetaron automáticamente {creados} tramos de tuberías orientando la etiqueta 'SPOT' hacia la dirección de flujo de agua.");
            }

            return creados;
        }

        /// <summary>
        /// Agrupa tuberías visibles en tramos continuos colineales y conectados.
        /// </summary>
        private static List<List<Element>> AgruparTuberiasColineales(List<Element> tuberias)
        {
            List<List<Element>> resultado = new List<List<Element>>();
            HashSet<ElementId> procesados = new HashSet<ElementId>();

            foreach (var pipe in tuberias)
            {
                if (procesados.Contains(pipe.Id)) continue;

                List<Element> grupoActual = new List<Element> { pipe };
                procesados.Add(pipe.Id);

                bool huboCambios = true;
                while (huboCambios)
                {
                    huboCambios = false;
                    foreach (var candidato in tuberias)
                    {
                        if (procesados.Contains(candidato.Id)) continue;

                        if (grupoActual.Any(miembro => SonColinealesYConectadas(miembro, candidato)))
                        {
                            grupoActual.Add(candidato);
                            procesados.Add(candidato.Id);
                            huboCambios = true;
                        }
                    }
                }

                resultado.Add(grupoActual);
            }

            return resultado;
        }

        /// <summary>
        /// Determina si dos tuberías son colineales (mismo eje) y están cercanas/conectadas.
        /// </summary>
        private static bool SonColinealesYConectadas(Element e1, Element e2)
        {
            if (e1 == null || e2 == null || e1.Id == e2.Id) return false;

            if (e1.Location is LocationCurve lc1 && lc1.Curve is Line line1 &&
                e2.Location is LocationCurve lc2 && lc2.Curve is Line line2)
            {
                XYZ p1_start = line1.GetEndPoint(0);
                XYZ p1_end = line1.GetEndPoint(1);
                XYZ v1 = (p1_end - p1_start).Normalize();

                XYZ p2_start = line2.GetEndPoint(0);
                XYZ p2_end = line2.GetEndPoint(1);
                XYZ v2 = (p2_end - p2_start).Normalize();

                // 1. Mismo eje en 2D/3D (direcciones paralelas)
                double dot = Math.Abs(v1.DotProduct(v2));
                if (dot < 0.98) return false;

                // 2. Distancia al eje de la primera línea
                XYZ vecToP2Start = p2_start - p1_start;
                XYZ projOnV1 = p1_start + v1 * vecToP2Start.DotProduct(v1);
                if (p2_start.DistanceTo(projOnV1) > 0.25) return false;

                // 3. Proximidad entre los puntos medios (permite intersección con accesorios intermedios)
                XYZ mid1 = (p1_start + p1_end) / 2.0;
                XYZ mid2 = (p2_start + p2_end) / 2.0;
                double maxDist = (line1.Length + line2.Length) / 2.0 + 20.0;

                return mid1.DistanceTo(mid2) <= maxDist;
            }

            return false;
        }

        /// <summary>
        /// Calcula la dirección exacta del flujo de agua (del extremo más alto al más bajo en Z)
        /// y el ángulo de rotación para la flecha.
        /// </summary>
        private static bool ObtenerDireccionFlujoAgua(Element el, out XYZ puntoMitad, out XYZ dirFlujo, out double anguloFlujo)
        {
            puntoMitad = null;
            dirFlujo = XYZ.BasisX;
            anguloFlujo = 0.0;

            if (el?.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                puntoMitad = (p0 + p1) / 2.0;

                // Determinar extremo alto (origen del agua) y extremo bajo (destino del agua)
                XYZ pAlto = p0.Z >= p1.Z ? p0 : p1;
                XYZ pBajo = p0.Z >= p1.Z ? p1 : p0;

                XYZ v = pBajo - pAlto;
                if (v.IsZeroLength()) v = p1 - p0;

                XYZ dir2D = new XYZ(v.X, v.Y, 0);
                if (dir2D.IsZeroLength()) dir2D = XYZ.BasisX;
                else dir2D = dir2D.Normalize();

                dirFlujo = dir2D;
                anguloFlujo = Math.Atan2(dir2D.Y, dir2D.X);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calcula el punto medio de la tubería, el porcentaje exacto de pendiente (ej. 1.00%)
        /// y la orientación de la flecha hacia donde cae el tubo (dirección del flujo de agua).
        /// </summary>
        private static bool ObtenerDireccionFlujoYTexto(Element el, out XYZ puntoMitad, out string textoSlope, out double anguloTexto)
        {
            puntoMitad = null;
            textoSlope = "0.00%";
            anguloTexto = 0.0;

            if (el?.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                puntoMitad = (p0 + p1) / 2.0;

                XYZ v = p1 - p0;
                if (v.IsZeroLength()) return false;
                XYZ dirCurva = v.Normalize();

                Parameter pSlope = el.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                double slopeVal = (pSlope != null && pSlope.HasValue) ? Math.Abs(pSlope.AsDouble()) : Math.Abs(dirCurva.Z);
                textoSlope = $"{slopeVal * 100.0:0.00}%";

                // Determinar qué extremo está más bajo en Z (hacia dónde cae el tubo / fluye el agua)
                XYZ pAlto = p0.Z >= p1.Z ? p0 : p1;
                XYZ pBajo = p0.Z >= p1.Z ? p1 : p0;

                XYZ dirFlujo = pBajo - pAlto;
                if (dirFlujo.IsZeroLength() || (Math.Abs(dirFlujo.X) < 0.0001 && Math.Abs(dirFlujo.Y) < 0.0001))
                {
                    dirFlujo = dirCurva;
                }

                XYZ dirFlujo2D = new XYZ(dirFlujo.X, dirFlujo.Y, 0);
                if (dirFlujo2D.IsZeroLength()) dirFlujo2D = XYZ.BasisX;
                else dirFlujo2D = dirFlujo2D.Normalize();

                double anguloFlujo = Math.Atan2(dirFlujo2D.Y, dirFlujo2D.X);

                // Orientar el texto siempre de forma legible (derecha-arriba) e indicar la flecha hacia el punto bajo
                if (dirFlujo2D.X < -0.001 || (Math.Abs(dirFlujo2D.X) <= 0.001 && dirFlujo2D.Y < -0.001))
                {
                    textoSlope = $"← {textoSlope}";
                    anguloTexto = anguloFlujo + Math.PI;
                }
                else
                {
                    textoSlope = $"{textoSlope} →";
                    anguloTexto = anguloFlujo;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Valida si el elemento es una tubería apta para taguear pendiente (con inclinación real y longitud suficiente),
        /// ignorando niples, tramos muy cortos (< 36 cm / 1.2 ft), bajantes/subidas verticales y tuberías con pendiente 0%.
        /// </summary>
        private static bool EsTuberiaConPendienteValida(Element e, out XYZ puntoMitad, out XYZ dirCurva)
        {
            puntoMitad = null;
            dirCurva = XYZ.BasisX;

            if (e == null || e.Category == null) return false;

#if REVIT2024_OR_LATER
            if (e.Category.Id.Value != (long)BuiltInCategory.OST_PipeCurves) return false;
#else
            if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) return false;
#endif

            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                // 1. Longitud mínima de 1.2 ft (~36 cm)
                if (lc.Curve.Length < 1.2) return false;

                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                puntoMitad = (p0 + p1) / 2.0;

                XYZ v = p1 - p0;
                if (!v.IsZeroLength())
                {
                    dirCurva = v.Normalize();
                }

                // 2. Descartar bajantes/subidas verticales (dirCurva.Z muy alto > 0.85)
                if (Math.Abs(dirCurva.Z) > 0.85) return false;

                // 3. Verificar parámetro de pendiente RBS_PIPE_SLOPE o inclinación Z real
                Parameter pSlope = e.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                if (pSlope != null && pSlope.HasValue)
                {
                    double slopeVal = pSlope.AsDouble();
                    if (Math.Abs(slopeVal) < 0.0001 && Math.Abs(dirCurva.Z) < 0.001)
                    {
                        return false; // Tubería plana sin pendiente (0.00%)
                    }
                }
                else if (Math.Abs(dirCurva.Z) < 0.001)
                {
                    return false; // Sin pendiente Z ni parámetro de pendiente
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Valida si el elemento es una tubería apta para taguear (horizontal o vertical),
        /// ignorando niples y tramos extremadamente cortos (< 36 cm / 1.2 ft).
        /// </summary>
        private static bool EsTuberiaValidaParaTag(Element e)
        {
            if (e == null || e.Category == null) return false;

#if REVIT2024_OR_LATER
            if (e.Category.Id.Value != (long)BuiltInCategory.OST_PipeCurves) return false;
#else
            if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) return false;
#endif

            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                // Mínimo 1.2 ft (~36 cm) para ignorar niples/tramitos cortos alrededor de codos/Ts
                return lc.Curve.Length >= 1.2;
            }

            return false;
        }

        /// <summary>
        /// Crea y posiciona una cota de pendiente (Spot Slope) en la mitad de la tubería,
        /// asegurando que ÚNICAMENTE se mantenga si se convierte exitosamente al tipo Spot Slope (OST_SpotSlopes).
        /// Si falla la conversión, la cota se elimina inmediatamente para no dejar una Spot Elevation (N. X.XX).
        /// </summary>
        private static SpotDimension ColocarSpotSlope(
            Document doc,
            Autodesk.Revit.DB.View view,
            Element el,
            Reference pickRef,
            XYZ puntoMitad,
            XYZ dirCurva,
            ElementId spotSlopeTypeId)
        {
            if (el == null || view == null) return null;

            if (puntoMitad == null)
            {
                if (el.Location is LocationCurve lc && lc.Curve != null)
                    puntoMitad = lc.Curve.Evaluate(0.5, true);
                else
                    return null;
            }

            XYZ bendPt;
            XYZ endPt;

            bool esHorizontalEnPlanta = Math.Abs(dirCurva.X) >= Math.Abs(dirCurva.Y);

            if (esHorizontalEnPlanta)
            {
                double offsetY = 0.6;
                double offsetHombroX = 0.8;
                bendPt = new XYZ(puntoMitad.X, puntoMitad.Y + offsetY, puntoMitad.Z);
                endPt = new XYZ(bendPt.X + offsetHombroX, bendPt.Y, bendPt.Z);
            }
            else
            {
                double offsetX = 0.6;
                double offsetHombroY = 0.8;
                bendPt = new XYZ(puntoMitad.X + offsetX, puntoMitad.Y, puntoMitad.Z);
                endPt = new XYZ(bendPt.X, bendPt.Y + offsetHombroY, bendPt.Z);
            }

            // Extraer la referencia geométrica nativa del elemento (Line/Curve/Face)
            Reference geomRef = ObtenerReferenciaGeometricaPipe(el, view, out XYZ ptSobreGeom);
            if (ptSobreGeom != null) puntoMitad = ptSobreGeom;

            List<Reference> referenciasAProbar = new List<Reference>();
            if (geomRef != null) referenciasAProbar.Add(geomRef);

            // Si cara 3D existe, agregar también como opción
            if (ObtenerReferenciaCaraSuperiorYPoint(el, view, puntoMitad, out Reference faceRef, out XYZ puntoCara))
            {
                if (faceRef != null && !referenciasAProbar.Contains(faceRef))
                {
                    referenciasAProbar.Add(faceRef);
                }
            }

            // Si pickRef contiene una referencia geométrica válida, agregarla
            if (pickRef != null && pickRef.ElementReferenceType != ElementReferenceType.REFERENCE_TYPE_NONE)
            {
                if (!referenciasAProbar.Contains(pickRef)) referenciasAProbar.Add(pickRef);
            }

            if (referenciasAProbar.Count == 0) return null;

            SpotDimension spot = null;

            foreach (Reference r in referenciasAProbar)
            {
                // Intento 1: Con Líder (flecha/hombro)
                try
                {
                    spot = doc.Create.NewSpotElevation(view, r, puntoMitad, bendPt, endPt, puntoMitad, true);
                    if (spot != null) break;
                }
                catch { }

                // Intento 2: Sin Líder
                try
                {
                    spot = doc.Create.NewSpotElevation(view, r, puntoMitad, puntoMitad, puntoMitad, puntoMitad, false);
                    if (spot != null) break;
                }
                catch { }
            }

            if (spot == null) return null;

            // Intentar asignar el tipo específico de Spot Slope (Spot Slopes | Sloped)
            if (spotSlopeTypeId != null && spotSlopeTypeId != ElementId.InvalidElementId)
            {
                try
                {
                    spot.ChangeTypeId(spotSlopeTypeId);
                    doc.Regenerate();
                }
                catch { }
            }

            return spot;
        }

        /// <summary>
        /// Obtiene dinámicamente la referencia geométrica nativa (Line/Curve/Face) de una tubería
        /// para permitir la creación de cotas Spot Elevation/Slope en cualquier nivel de detalle de vista (Bajo, Medio, Fino).
        /// </summary>
        private static Reference ObtenerReferenciaGeometricaPipe(Element el, Autodesk.Revit.DB.View view, out XYZ puntoPoint)
        {
            puntoPoint = null;
            if (el == null) return null;

            if (el.Location is LocationCurve lc && lc.Curve != null)
            {
                puntoPoint = lc.Curve.Evaluate(0.5, true);
            }

            try
            {
                // 1. Probar con las opciones de geometría asociadas a la vista activa (ComputeReferences = true)
                Options opt = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = true
                };
                if (view != null) opt.View = view;
                else opt.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geom = el.get_Geometry(opt);
                if (geom != null)
                {
                    Reference refEncontrada = ExtraerReferenciaDesdeGeometria(geom);
                    if (refEncontrada != null) return refEncontrada;
                }

                // 2. Fallback: Probar con DetailLevel = Fine sin vista específica
                opt.View = null;
                opt.DetailLevel = ViewDetailLevel.Fine;
                geom = el.get_Geometry(opt);
                if (geom != null)
                {
                    Reference refEncontrada = ExtraerReferenciaDesdeGeometria(geom);
                    if (refEncontrada != null) return refEncontrada;
                }
            }
            catch { }

            return null;
        }

        private static Reference ExtraerReferenciaDesdeGeometria(GeometryElement gElem)
        {
            if (gElem == null) return null;

            foreach (GeometryObject gObj in gElem)
            {
                if (gObj is Line line && line.Reference != null)
                {
                    return line.Reference;
                }
                else if (gObj is Curve curve && curve.Reference != null)
                {
                    return curve.Reference;
                }
                else if (gObj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face.Reference != null) return face.Reference;
                    }
                }
                else if (gObj is GeometryInstance gInst)
                {
                    GeometryElement instGeom = gInst.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        Reference r = ExtraerReferenciaDesdeGeometria(instGeom);
                        if (r != null) return r;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Busca la referencia válida a la cara/superficie de la tubería y el punto exacto sobre la cara.
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
                Options opt = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = true
                };

                if (view != null) opt.View = view;
                else opt.DetailLevel = ViewDetailLevel.Fine;

                GeometryElement geom = el.get_Geometry(opt);
                if (geom == null) return false;

                XYZ testPointUpper = new XYZ(puntoMedioEje.X, puntoMedioEje.Y, puntoMedioEje.Z + 2.0);
                XYZ testPointSide = new XYZ(puntoMedioEje.X + 2.0, puntoMedioEje.Y + 2.0, puntoMedioEje.Z);

                double menorDistancia = double.MaxValue;

                List<Solid> solidos = new List<Solid>();
                void ExtraerSolidos(GeometryElement gElem)
                {
                    foreach (GeometryObject gObj in gElem)
                    {
                        if (gObj is Solid solid && solid.Faces.Size > 0)
                        {
                            solidos.Add(solid);
                        }
                        else if (gObj is GeometryInstance gInst)
                        {
                            GeometryElement instGeom = gInst.GetInstanceGeometry();
                            if (instGeom != null) ExtraerSolidos(instGeom);
                        }
                    }
                }

                ExtraerSolidos(geom);

                foreach (Solid solid in solidos)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face.Reference == null) continue;

                        IntersectionResult irUpper = null;
                        IntersectionResult irSide = null;
                        try { irUpper = face.Project(testPointUpper); } catch { }
                        try { irSide = face.Project(testPointSide); } catch { }

                        IntersectionResult ir = irUpper ?? irSide;
                        if (ir != null && ir.XYZPoint != null)
                        {
                            double dist = ir.XYZPoint.DistanceTo(puntoMedioEje);
                            if (dist < menorDistancia)
                            {
                                menorDistancia = dist;
                                refCara = face.Reference;
                                puntoEnCara = ir.XYZPoint;
                            }
                        }
                    }
                }

                return (refCara != null && puntoEnCara != null);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calculates the midpoint in 3D of a pipe and its direction vector.
        /// </summary>
        private static bool ObtenerPuntoMedioYDireccion(Element el, out XYZ puntoMitad, out XYZ dirCurva)
        {
            puntoMitad = null;
            dirCurva = XYZ.BasisX;

            if (el?.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);

                puntoMitad = (p0 + p1) / 2.0;

                XYZ v = p1 - p0;
                if (!v.IsZeroLength())
                {
                    dirCurva = v.Normalize();
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Obtiene el ElementId del elemento hospedado/etiquetado de forma compatible con todas las versiones de Revit API (2020-2026).
        /// </summary>
        private static ElementId GetTaggedHostElementId(IndependentTag tag)
        {
            if (tag == null) return ElementId.InvalidElementId;

            // A) Intento con GetTaggedLocalElementId / GetTaggedLocalElementIds vía reflexión
            try
            {
                var mSingle = tag.GetType().GetMethod("GetTaggedLocalElementId");
                if (mSingle != null)
                {
                    var res = mSingle.Invoke(tag, null);
                    if (res is ElementId eid && eid != ElementId.InvalidElementId) return eid;
                }

                var mMulti = tag.GetType().GetMethod("GetTaggedLocalElementIds");
                if (mMulti != null)
                {
                    var res = mMulti.Invoke(tag, null) as System.Collections.IEnumerable;
                    if (res != null)
                    {
                        foreach (var item in res)
                        {
                            if (item is ElementId eid && eid != ElementId.InvalidElementId) return eid;
                        }
                    }
                }
            }
            catch { }

            // B) Revit 2022+: TaggedLocalElementId (Propiedad)
            try
            {
                var propLocal = tag.GetType().GetProperty("TaggedLocalElementId");
                if (propLocal != null)
                {
                    var val = propLocal.GetValue(tag, null);
                    if (val is ElementId eid && eid != ElementId.InvalidElementId) return eid;
                }
            }
            catch { }

            // C) TaggedElementId (Propiedad - ElementId o LinkElementId)
            try
            {
                var prop = tag.GetType().GetProperty("TaggedElementId");
                if (prop != null)
                {
                    var val = prop.GetValue(tag, null);
                    if (val is ElementId e1 && e1 != ElementId.InvalidElementId) return e1;

                    var hostProp = val?.GetType().GetProperty("HostElementId");
                    if (hostProp != null)
                    {
                        var hostId = hostProp.GetValue(val, null);
                        if (hostId is ElementId e2 && e2 != ElementId.InvalidElementId) return e2;
                    }
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Obtiene de forma segura el nombre de la familia de un ElementType/FamilySymbol en distintas versiones de Revit.
        /// </summary>
        private static string ObtenerNombreFamilia(ElementType t)
        {
            if (t == null) return string.Empty;

            try
            {
                FamilySymbol fs = t as FamilySymbol;
                if (fs != null)
                {
                    if (fs.Family != null && !string.IsNullOrEmpty(fs.Family.Name))
                        return fs.Family.Name;
                    if (!string.IsNullOrEmpty(fs.FamilyName))
                        return fs.FamilyName;
                }

                Parameter pFamName = t.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME)
                                  ?? t.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                if (pFamName != null && pFamName.HasValue)
                {
                    string val = pFamName.AsString();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }

            return string.Empty;
        }

        /// <summary>
        /// Busca un tipo de etiqueta de tubería (Pipe Tag) o multicategoría específico para pendiente cargado en el proyecto,
        /// priorizando la familia recién cargada 'SPOT' (SPOT.rfa, SPOT 2020, etc.) y otras variantes de etiquetas de pendiente.
        /// </summary>
        private static ElementId ObtenerTipoPipeTagPendiente(Document doc)
        {
            try
            {
                List<ElementType> tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                var multiCategoryTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                tagTypes.AddRange(multiCategoryTypes);

                if (tagTypes.Count > 0)
                {
                    // 1. PRIORIDAD ABSOLUTA: Familia o Tipo con el nombre "SPOT" (SPOT.rfa, SPOT 2020, SpotSlope, etc.)
                    var spotFamilyType = tagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? "";
                        string familyName = ObtenerNombreFamilia(t);
                        return name.Equals("SPOT", StringComparison.OrdinalIgnoreCase) ||
                               familyName.Equals("SPOT", StringComparison.OrdinalIgnoreCase) ||
                               name.IndexOf("SPOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               familyName.IndexOf("SPOT", StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                    if (spotFamilyType != null) return spotFamilyType.Id;

                    // 2. Segunda Prioridad: Tipos o familias que especifiquen "Solo Pendiente", "Pendiente Solo", "Slope Only", "Solo" o "Desagüe"
                    var soloSlopeType = tagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? "";
                        string familyName = ObtenerNombreFamilia(t);
                        string full = name + " " + familyName;
                        return (full.IndexOf("Pendiente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                full.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               (full.IndexOf("Solo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                full.IndexOf("Only", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                full.IndexOf("Desagüe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                full.IndexOf("Desague", StringComparison.OrdinalIgnoreCase) >= 0);
                    });

                    if (soloSlopeType != null) return soloSlopeType.Id;

                    // 3. Tercera Prioridad: Tipos con "Pendiente" o "Slope" sin "Con", "Diámetro", "ø" o "Sistema"
                    var cleanSlopeType = tagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? "";
                        string familyName = ObtenerNombreFamilia(t);
                        string full = name + " " + familyName;
                        return (full.IndexOf("Pendiente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                full.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               full.IndexOf("Con ", StringComparison.OrdinalIgnoreCase) < 0 &&
                               full.IndexOf("Diag", StringComparison.OrdinalIgnoreCase) < 0 &&
                               full.IndexOf("ø", StringComparison.OrdinalIgnoreCase) < 0 &&
                               full.IndexOf("Sist", StringComparison.OrdinalIgnoreCase) < 0;
                    });

                    if (cleanSlopeType != null) return cleanSlopeType.Id;

                    // 4. Fallback: Cualquier tipo de etiqueta que tenga "Pendiente", "Slope" o "S="
                    var slopeTagType = tagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? "";
                        string familyName = ObtenerNombreFamilia(t);
                        string full = name + " " + familyName;
                        return full.IndexOf("Pendiente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               full.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               full.IndexOf("S=", StringComparison.OrdinalIgnoreCase) >= 0;
                    });

                    if (slopeTagType != null) return slopeTagType.Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Localiza el tipo de SpotDimension correspondiente a Spot Slope (Pendiente / OST_SpotSlopes / "Sloped").
        /// </summary>
        private static ElementId ObtenerTipoSpotSlope(Document doc)
        {
            try
            {
                // 1. Buscar en la categoría BuiltInCategory.OST_SpotSlopes
                var slopeTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_SpotSlopes)
                    .WhereElementIsElementType()
                    .ToList();

                if (slopeTypes.Count > 0)
                {
                    var objetivo = slopeTypes.FirstOrDefault(t =>
                        t.Name.Equals("Sloped", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.IndexOf("Sloped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Pendiente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? slopeTypes.First();

                    return objetivo.Id;
                }

                // 2. Fallback: Buscar en DimensionType de categoría OST_SpotSlopes
                var dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => t.Category != null && t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_SpotSlopes)
                    .ToList();

                if (dimTypes.Count > 0) return dimTypes.First().Id;

                // 3. Fallback: SpotDimensionType que contenga "Sloped" o "Slope" en el nombre
                var spotDimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpotDimensionType))
                    .Cast<SpotDimensionType>()
                    .Where(t => t.Name.IndexOf("Sloped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Pendiente", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                return spotDimTypes.FirstOrDefault()?.Id;
            }
            catch
            {
                return null;
            }
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                return e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves;
            }

            public bool AllowReference(Reference r, XYZ p) => true;
        }

        // =========================================================================
        // 2. TAG MATERIAL (DESAGÜES / TUBERÍAS)
        // =========================================================================

        /// <summary>
        /// Flujo por selección múltiple para etiquetar el material y diámetro de tuberías
        /// mediante la familia 'DC - Tag tubería 2 mm' (Type Name + Size).
        /// </summary>
        public static int TaguearMaterialPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId materialTagTypeId = ObtenerTipoPipeTagMaterial(doc);
            if (materialTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material",
                    "No se encontró la familia de etiqueta de tubería ('DC - Tag tubería 2 mm' / 'Type Name + Size') cargada en el proyecto.");
                return 0;
            }

            List<Reference> refsList = new List<Reference>();

            var filter = new PipeSelectionFilter();
            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        refsList.Add(new Reference(el));
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (refsList.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tuberías a taguear material y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        refsList.AddRange(pickedRefs);
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

            if (refsList.Count == 0) return 0;

            var tagSymbol = doc.GetElement(materialTagTypeId) as FamilySymbol;
            MaterialTagCollisionContext ctx = MaterialTagCollisionContext.BuildFromView(doc, view);

            int colocados = 0;
            using (Transaction tx = new Transaction(doc, "Tags Material por Selección (Redes Húmedas)"))
            {
                tx.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                foreach (var pickRef in refsList)
                {
                    try
                    {
                        Element pipe = doc.GetElement(pickRef.ElementId);
                        if (pipe == null || !EsTuberiaValidaParaTag(pipe)) continue;

                        if (!ObtenerPuntoMedioYDireccion(pipe, out XYZ puntoMitad, out XYZ dirCurva)) continue;

                        if (!EsTuberiaOrtogonalEnPlanta(dirCurva)) continue;

                        if (!CalcularPosicionTagMaterialOptima(pipe, view, ctx, out XYZ tagPos, out TagOrientation orientacion, out bool usarLeader, out _))
                        {
                            bool esVert = Math.Abs(dirCurva.Z) > 0.8 || (Math.Abs(dirCurva.Y) > Math.Abs(dirCurva.X));
                            orientacion = esVert ? TagOrientation.Vertical : TagOrientation.Horizontal;
                            tagPos = esVert ? puntoMitad + new XYZ(0.85, 0, 0) : puntoMitad + new XYZ(0, -0.85, 0);
                            usarLeader = false;
                        }

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            view.Id,
                            new Reference(pipe),
                            usarLeader,
                            TagMode.TM_ADDBY_CATEGORY,
                            orientacion,
                            tagPos
                        );

                        if (tag != null)
                        {
                            if (materialTagTypeId != ElementId.InvalidElementId)
                            {
                                try { tag.ChangeTypeId(materialTagTypeId); } catch { }
                            }

                            tag.HasLeader = usarLeader;
                            if (usarLeader)
                            {
                                tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            }
                            tag.TagHeadPosition = tagPos;

                            colocados++;
                            ctx.AddPlacedTag(tagPos, orientacion);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (colocados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material", $"Se colocaron {colocados} etiquetas de material.");
            }

            return colocados;
        }

        public static int TaguearMaterialPorClic(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId materialTagTypeId = ObtenerTipoPipeTagMaterial(doc);
            if (materialTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material",
                    "No se encontró la familia de etiqueta de tubería ('DC - Tag tubería 2 mm' / 'Type Name + Size') cargada en el proyecto.");
                return 0;
            }

            var tagSymbol = doc.GetElement(materialTagTypeId) as FamilySymbol;

            int colocados = 0;
            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new PipeSelectionFilter(),
                        "Clic en la tubería a taguear material (ESC para terminar)"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                if (pickRef == null) break;

                Element pipe = doc.GetElement(pickRef.ElementId);
                if (pipe == null || !EsTuberiaValidaParaTag(pipe)) continue;

                if (!ObtenerPuntoMedioYDireccion(pipe, out XYZ puntoMitad, out XYZ dirCurva)) continue;

                // Descartar tuberías en diagonal (solo permitir horizontales o verticales)
                if (!EsTuberiaOrtogonalEnPlanta(dirCurva))
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Tag Material", "Solo se pueden taguear tuberías horizontales o verticales (no diagonales).");
                    continue;
                }

                // Construir contexto de colisiones de la vista para encontrar zona en blanco
                MaterialTagCollisionContext ctx = MaterialTagCollisionContext.BuildFromView(doc, view, new[] { pipe.Id });

                if (!CalcularPosicionTagMaterialOptima(pipe, view, ctx, out XYZ tagPos, out TagOrientation orientacion, out bool usarLeader, out _))
                {
                    bool esVert = Math.Abs(dirCurva.Z) > 0.8 || (Math.Abs(dirCurva.Y) > Math.Abs(dirCurva.X));
                    orientacion = esVert ? TagOrientation.Vertical : TagOrientation.Horizontal;
                    tagPos = esVert ? puntoMitad + new XYZ(0.85, 0, 0) : puntoMitad + new XYZ(0, -0.85, 0);
                    usarLeader = false;
                }

                using (Transaction tx = new Transaction(doc, "Colocar Tag Material (Redes Húmedas)"))
                {
                    tx.Start();

                    if (tagSymbol != null && !tagSymbol.IsActive)
                    {
                        tagSymbol.Activate();
                        doc.Regenerate();
                    }

                    IndependentTag tag = IndependentTag.Create(
                        doc,
                        view.Id,
                        new Reference(pipe),
                        usarLeader,
                        TagMode.TM_ADDBY_CATEGORY,
                        orientacion,
                        tagPos
                    );

                    if (tag != null)
                    {
                        if (materialTagTypeId != ElementId.InvalidElementId)
                        {
                            try { tag.ChangeTypeId(materialTagTypeId); } catch { }
                        }

                        tag.HasLeader = usarLeader;
                        if (usarLeader)
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        }
                        tag.TagHeadPosition = tagPos;

                        colocados++;
                    }

                    tx.Commit();
                }
            }

            if (colocados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material", $"Se colocaron {colocados} etiquetas de material.");
            }

            return colocados;
        }

        private static bool EsTuberiaOrtogonalEnPlanta(XYZ dirCurva)
        {
            if (dirCurva == null) return false;
            double len2D = Math.Sqrt(dirCurva.X * dirCurva.X + dirCurva.Y * dirCurva.Y);
            if (len2D < 1e-4) return false; // Subida o bajada en Z pura

            double ux = Math.Abs(dirCurva.X) / len2D;
            double uy = Math.Abs(dirCurva.Y) / len2D;

            // Horizontal (ux cercano a 1) o Vertical (uy cercano a 1). Se descartan diagonales (45°)
            bool esHorizontal = uy <= 0.20 || ux >= 0.95;
            bool esVertical = ux <= 0.20 || uy >= 0.95;

            return esHorizontal || esVertical;
        }

        #region Control de Colisiones y Búsqueda de Espacio en Blanco para Tag Material

        private class TagBox2D
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public TagBox2D(double minX, double maxX, double minY, double maxY)
            {
                MinX = Math.Min(minX, maxX);
                MaxX = Math.Max(minX, maxX);
                MinY = Math.Min(minY, maxY);
                MaxY = Math.Max(minY, maxY);
            }

            public bool Intersects(TagBox2D other, double margin = 0.0)
            {
                if (MaxX + margin < other.MinX || MinX - margin > other.MaxX) return false;
                if (MaxY + margin < other.MinY || MinY - margin > other.MaxY) return false;
                return true;
            }
        }

        private class MaterialTagCollisionContext
        {
            public List<TagBox2D> PipeObstacles = new List<TagBox2D>();
            public List<TagBox2D> FittingObstacles = new List<TagBox2D>();
            public List<TagBox2D> OtherObstacles = new List<TagBox2D>();
            public List<TagBox2D> TagObstacles = new List<TagBox2D>();

            public static MaterialTagCollisionContext BuildFromView(Document doc, Autodesk.Revit.DB.View view, IEnumerable<ElementId> excludePipeIds = null)
            {
                var ctx = new MaterialTagCollisionContext();
                HashSet<ElementId> exclude = new HashSet<ElementId>(excludePipeIds ?? Enumerable.Empty<ElementId>());

                try
                {
                    // 1. Tuberías en la vista
                    var pipes = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_PipeCurves)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (var p in pipes)
                    {
                        if (p == null || exclude.Contains(p.Id)) continue;
                        BoundingBoxXYZ bb = p.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.PipeObstacles.Add(new TagBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    // 2. Accesorios de tubería (Fittings como Ys, codos, reducciones, tees)
                    var fittings = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_PipeFitting)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (var f in fittings)
                    {
                        if (f == null) continue;
                        BoundingBoxXYZ bb = f.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.FittingObstacles.Add(new TagBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    // 3. Otros elementos MEP y aparatos (accesorios, ductos, bandejas, aparatos sanitarios, equipos)
                    BuiltInCategory[] otherCats = new BuiltInCategory[]
                    {
                        BuiltInCategory.OST_PipeAccessory,
                        BuiltInCategory.OST_DuctCurves,
                        BuiltInCategory.OST_DuctFitting,
                        BuiltInCategory.OST_DuctAccessory,
                        BuiltInCategory.OST_CableTray,
                        BuiltInCategory.OST_Conduit,
                        BuiltInCategory.OST_PlumbingFixtures,
                        BuiltInCategory.OST_MechanicalEquipment
                    };

                    foreach (var cat in otherCats)
                    {
                        try
                        {
                            var elems = new FilteredElementCollector(doc, view.Id)
                                .OfCategory(cat)
                                .WhereElementIsNotElementType()
                                .ToElements();

                            foreach (var e in elems)
                            {
                                if (e == null) continue;
                                BoundingBoxXYZ bb = e.get_BoundingBox(view);
                                if (bb != null)
                                {
                                    ctx.OtherObstacles.Add(new TagBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                                }
                            }
                        }
                        catch { }
                    }

                    // 4. Tags existentes y dimensiones
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
                            ctx.TagObstacles.Add(new TagBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }

                    var spotDims = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpotDimension))
                        .ToElements();

                    foreach (var sd in spotDims)
                    {
                        if (sd == null) continue;
                        BoundingBoxXYZ bb = sd.get_BoundingBox(view);
                        if (bb != null)
                        {
                            ctx.TagObstacles.Add(new TagBox2D(bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y));
                        }
                    }
                }
                catch { }

                return ctx;
            }

            public void AddPlacedTag(XYZ pos, TagOrientation orientacion, double halfW = 0, double halfH = 0)
            {
                if (halfW <= 0 || halfH <= 0)
                {
                    halfW = orientacion == TagOrientation.Horizontal ? 2.5 : 0.4;
                    halfH = orientacion == TagOrientation.Horizontal ? 0.4 : 2.5;
                }
                TagObstacles.Add(new TagBox2D(pos.X - halfW, pos.X + halfW, pos.Y - halfH, pos.Y + halfH));
            }
        }

        private static bool CalcularPosicionTagMaterialOptima(
            Element pipe,
            Autodesk.Revit.DB.View view,
            MaterialTagCollisionContext ctx,
            out XYZ mejorPos,
            out TagOrientation mejorOrientacion,
            out bool usarLeader,
            out double mejorPenalidad)
        {
            mejorPos = null;
            mejorOrientacion = TagOrientation.Horizontal;
            usarLeader = false;
            mejorPenalidad = double.MaxValue;

            if (pipe == null || !(pipe.Location is LocationCurve lc) || lc.Curve == null) return false;

            XYZ p0 = lc.Curve.GetEndPoint(0);
            XYZ p1 = lc.Curve.GetEndPoint(1);
            XYZ dir = p1 - p0;
            double len2D = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (len2D < 0.3) return false;

            XYZ dir2D = new XYZ(dir.X / len2D, dir.Y / len2D, 0);

            bool esVerticalEnPlanta = Math.Abs(dir2D.Y) > Math.Abs(dir2D.X);
            mejorOrientacion = esVerticalEnPlanta ? TagOrientation.Vertical : TagOrientation.Horizontal;

            // Escala de la vista para calcular el tamaño real del tag de material en el modelo (pies)
            double scale = (view != null && view.Scale > 0) ? (double)view.Scale : 50.0;
            double textHeightModel = (2.5 / 1000.0 * scale) * 3.28084; // ~0.41 ft a 1:50
            double textWidthModel = (32.0 / 1000.0 * scale) * 3.28084;  // ~5.25 ft a 1:50

            // Dimensiones de la caja de texto del tag
            double halfW = (mejorOrientacion == TagOrientation.Horizontal) ? (textWidthModel * 0.52) : (textHeightModel * 0.85);
            double halfH = (mejorOrientacion == TagOrientation.Horizontal) ? (textHeightModel * 0.85) : (textWidthModel * 0.52);

            // Radio exterior de la tubería
            double radioExterior = 0.20;
            Parameter pDiamExt = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)
                              ?? pipe.LookupParameter("Outside Diameter")
                              ?? pipe.LookupParameter("Diámetro exterior")
                              ?? pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (pDiamExt != null && pDiamExt.HasValue)
            {
                radioExterior = pDiamExt.AsDouble() * 0.5;
            }

            // Distancia base para librar el grosor de la propia tubería
            double sepMinima = radioExterior + (esVerticalEnPlanta ? halfW : halfH) + 0.35;

            // Vector perpendicular en el plano XY
            XYZ perp2D = new XYZ(-dir2D.Y, dir2D.X, 0);

            // Puntos a lo largo del tramo (centro, cuartos, extremos)
            double[] fracciones = new double[] { 0.50, 0.40, 0.60, 0.30, 0.70, 0.20, 0.80, 0.10, 0.90 };

            // Distancias perpendiculares a explorar (a ambos lados, desde cerca hasta espacios más amplios)
            double[] distanciasPerpendiculares = new double[]
            {
                sepMinima,
                -sepMinima,
                sepMinima + 0.75,
                -(sepMinima + 0.75),
                sepMinima + 1.60,
                -(sepMinima + 1.60),
                sepMinima + 2.70,
                -(sepMinima + 2.70),
                sepMinima + 4.00,
                -(sepMinima + 4.00),
                sepMinima + 5.50,
                -(sepMinima + 5.50)
            };

            // Desplazamientos longitudinales paralelos opcionales
            double[] shiftsLongitudinales = new double[] { 0.0, 0.60, -0.60, 1.40, -1.40 };

            foreach (double t in fracciones)
            {
                XYZ puntoEnEje = p0 + t * (p1 - p0);

                foreach (double dPerp in distanciasPerpendiculares)
                {
                    foreach (double sAlong in shiftsLongitudinales)
                    {
                        XYZ testPos = puntoEnEje + (perp2D * dPerp) + (dir2D * sAlong);

                        TagBox2D tagBox = new TagBox2D(
                            testPos.X - halfW,
                            testPos.X + halfW,
                            testPos.Y - halfH,
                            testPos.Y + halfH
                        );

                        double penalidadColisiones = 0.0;
                        int pipeIntersects = 0;
                        int fittingIntersects = 0;
                        int tagIntersects = 0;
                        int otherIntersects = 0;

                        if (ctx != null)
                        {
                            // 1. Colisión con tuberías (muy grave)
                            foreach (var obs in ctx.PipeObstacles)
                            {
                                if (tagBox.Intersects(obs, 0.18))
                                {
                                    pipeIntersects++;
                                    penalidadColisiones += 30000.0;
                                }
                            }

                            // 2. Colisión con accesorios/fittings (grave)
                            foreach (var fit in ctx.FittingObstacles)
                            {
                                if (tagBox.Intersects(fit, 0.18))
                                {
                                    fittingIntersects++;
                                    penalidadColisiones += 20000.0;
                                }
                            }

                            // 3. Colisión con otros tags / cotas (muy grave)
                            foreach (var tg in ctx.TagObstacles)
                            {
                                if (tagBox.Intersects(tg, 0.25))
                                {
                                    tagIntersects++;
                                    penalidadColisiones += 50000.0;
                                }
                            }

                            // 4. Colisión con otros elementos MEP / equipos / aparatos
                            foreach (var oth in ctx.OtherObstacles)
                            {
                                if (tagBox.Intersects(oth, 0.18))
                                {
                                    otherIntersects++;
                                    penalidadColisiones += 15000.0;
                                }
                            }
                        }

                        // Costo suave por distancia para preferir posiciones cercanas y centradas si están limpias
                        double costoDistancia = (Math.Abs(dPerp) * 8.0)
                                              + (Math.Abs(t - 0.50) * len2D * 4.0)
                                              + (Math.Abs(sAlong) * 4.0);

                        double penalidadTotal = penalidadColisiones + costoDistancia;

                        if (penalidadTotal < mejorPenalidad)
                        {
                            mejorPenalidad = penalidadTotal;
                            mejorPos = testPos;
                            usarLeader = Math.Abs(dPerp) > (radioExterior + 0.40) || Math.Abs(sAlong) > 0.30;

                            // Si encontramos una posición 100% limpia de obstáculos y cercana a la tubería
                            if (pipeIntersects == 0 && fittingIntersects == 0 && tagIntersects == 0 && otherIntersects == 0 && Math.Abs(dPerp) <= sepMinima + 0.80)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return mejorPos != null;
        }

        #endregion

        /// <summary>
        /// Flujo totalmente automático para etiquetar el material y diámetro de las tuberías
        /// visibles en la vista activa usando la familia 'DC - Tag tubería 2 mm' (Type Name + Size).
        /// Agrupa las tuberías conectadas aunque tengan codos y garantiza colocar SOLO UN TAG por red/línea continua
        /// <summary>
        /// Flujo totalmente automático para etiquetar el material y diámetro de las tuberías
        /// visibles en la vista activa usando la familia 'DC - Tag tubería 2 mm' (Type Name + Size).
        /// Agrupa las tuberías conectadas aunque tengan codos y garantiza colocar SOLO UN TAG por red/línea continua
        /// en la posición más limpia y despejada de obstáculos.
        /// </summary>
        public static int TaguearMaterialTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> tuberiasEnVista = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Where(e => EsTuberiaValidaParaTag(e))
                .ToList();

            if (tuberiasEnVista.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material Automático", "No se encontraron tuberías válidas en la vista activa.");
                return 0;
            }

            ElementId materialTagTypeId = ObtenerTipoPipeTagMaterial(doc);
            if (materialTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material",
                    "No se encontró la familia de etiqueta de tubería ('DC - Tag tubería 2 mm' / 'Type Name + Size') cargada en el proyecto.");
                return 0;
            }

            var tagSymbol = doc.GetElement(materialTagTypeId) as FamilySymbol;

            // Contexto global de colisiones en la vista
            MaterialTagCollisionContext collisionCtx = MaterialTagCollisionContext.BuildFromView(doc, view);

            // 1. Recolectar elementos que ya tienen etiquetas en la vista activa para no repetir
            HashSet<ElementId> elementosEtiquetados = new HashSet<ElementId>();
            try
            {
                var tagsExistentes = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tagsExistentes)
                {
                    try
                    {
                        ElementId taggedId = GetTaggedHostElementId(tag);
                        if (taggedId != null && taggedId != ElementId.InvalidElementId)
                        {
                            elementosEtiquetados.Add(taggedId);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Agrupar la red/línea continua y seleccionar la mejor posición en blanco libre de cruces
            List<Tuple<Element, XYZ, TagOrientation, bool>> tagsParaCrear = new List<Tuple<Element, XYZ, TagOrientation, bool>>();
            HashSet<ElementId> visitados = new HashSet<ElementId>();

            foreach (var tuberia in tuberiasEnVista)
            {
                if (visitados.Contains(tuberia.Id)) continue;
                if (elementosEtiquetados.Contains(tuberia.Id))
                {
                    visitados.Add(tuberia.Id);
                    continue;
                }

                // Recolectar toda la red conectada del mismo material y diámetro
                List<Element> redCompleta = RecolectarTuberiasDeRedCompletaConCodos(tuberia, tuberiasEnVista);

                bool redYaTieneTag = false;
                foreach (var item in redCompleta)
                {
                    visitados.Add(item.Id);
                    if (elementosEtiquetados.Contains(item.Id))
                    {
                        redYaTieneTag = true;
                    }
                }

                if (redYaTieneTag) continue;

                // Filtrar solo tuberías ortogonales en planta (excluyendo diagonales a 45° y subidas/bajadas en Z)
                var tuberiasPlanares = redCompleta.Where(t =>
                {
                    if (t.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        XYZ dir = p1 - p0;
                        double len2D = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                        return len2D >= 1.0 && Math.Abs(dir.Normalize().Z) < 0.85 && EsTuberiaOrtogonalEnPlanta(dir);
                    }
                    return false;
                }).ToList();

                if (tuberiasPlanares.Count == 0) continue;

                // Evaluar cada tramo de la red para encontrar el espacio en blanco con menor interferencia
                Element mejorTuberia = null;
                XYZ mejorPos = null;
                TagOrientation mejorOrient = TagOrientation.Horizontal;
                bool mejorLeader = false;
                double menorPenalidadRed = double.MaxValue;

                foreach (var candTuberia in tuberiasPlanares)
                {
                    if (CalcularPosicionTagMaterialOptima(candTuberia, view, collisionCtx, out XYZ pos, out TagOrientation orient, out bool leader, out double penalidad))
                    {
                        if (penalidad < menorPenalidadRed)
                        {
                            menorPenalidadRed = penalidad;
                            mejorTuberia = candTuberia;
                            mejorPos = pos;
                            mejorOrient = orient;
                            mejorLeader = leader;

                            if (penalidad < 40.0) break; // Posición 100% limpia
                        }
                    }
                }

                if (mejorTuberia != null && mejorPos != null)
                {
                    tagsParaCrear.Add(Tuple.Create(mejorTuberia, mejorPos, mejorOrient, mejorLeader));
                    collisionCtx.AddPlacedTag(mejorPos, mejorOrient);
                }
            }

            if (tagsParaCrear.Count == 0) return 0;

            int creados = 0;
            using (Transaction tx = new Transaction(doc, "Tags Material Automático (Redes Húmedas)"))
            {
                tx.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                foreach (var item in tagsParaCrear)
                {
                    Element tuberia = item.Item1;
                    XYZ tagPos = item.Item2;
                    TagOrientation orientacion = item.Item3;
                    bool usarLeader = item.Item4;

                    try
                    {
                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            view.Id,
                            new Reference(tuberia),
                            usarLeader,
                            TagMode.TM_ADDBY_CATEGORY,
                            orientacion,
                            tagPos
                        );

                        if (newTag != null)
                        {
                            if (materialTagTypeId != ElementId.InvalidElementId)
                            {
                                try { newTag.ChangeTypeId(materialTagTypeId); } catch { }
                            }

                            newTag.HasLeader = usarLeader;
                            if (usarLeader)
                            {
                                newTag.LeaderEndCondition = LeaderEndCondition.Attached;
                            }
                            newTag.TagHeadPosition = tagPos;

                            creados++;
                            elementosEtiquetados.Add(tuberia.Id);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (creados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material Automático",
                    $"Se etiquetaron automáticamente {creados} tuberías principales en zonas limpias.");
            }

            return creados;
        }

        /// <summary>
        /// Recolecta todas las tuberías conectadas en la misma red/línea continua (incluyendo conexiones por codos, Ts, Ys y acoplamientos).
        /// Recorre recursivamente la red completa sin importar la longitud de los niples o cantidad de accesorios intermedios,
        /// agrupando los tramos que comparten el mismo tipo de tubería (material), diámetro y sentido en planta (Horizontal vs Vertical).
        /// </summary>
        private static List<Element> RecolectarTuberiasDeRedCompletaConCodos(Element tuberiaInicial, IEnumerable<Element> todasTuberias)
        {
            if (tuberiaInicial == null) return new List<Element>();

            string keyInicial = ObtenerClaveMaterialYDiametro(tuberiaInicial);

            HashSet<ElementId> idValidosVista = new HashSet<ElementId>(todasTuberias.Select(e => e.Id));
            List<Element> redMismoMaterial = new List<Element>();
            Queue<Element> cola = new Queue<Element>();

            cola.Enqueue(tuberiaInicial);
            HashSet<ElementId> visitadosBFS = new HashSet<ElementId> { tuberiaInicial.Id };

            while (cola.Count > 0)
            {
                Element actual = cola.Dequeue();

                // Si es una tubería de la lista válida visible en la vista y coincide en material/diámetro/sentido, la agregamos al grupo
                if (idValidosVista.Contains(actual.Id))
                {
                    string keyActual = ObtenerClaveMaterialYDiametro(actual);
                    if (string.Equals(keyInicial, keyActual, StringComparison.OrdinalIgnoreCase))
                    {
                        redMismoMaterial.Add(actual);
                    }
                }

                // Extraer conectores
                ConnectorSet conectores = GetConnectors(actual);
                if (conectores == null) continue;

                foreach (Connector conn in conectores)
                {
                    if (conn == null || conn.AllRefs == null) continue;

                    foreach (Connector refConn in conn.AllRefs)
                    {
                        if (refConn == null) continue;
                        Element owner = refConn.Owner;
                        if (owner == null) continue;

                        if (visitadosBFS.Contains(owner.Id)) continue;

                        // Continuar la exploración BFS si el vecino es tubería, accesorio o unión de tubería
                        if (EsElementoDeRedTraversable(owner))
                        {
                            visitadosBFS.Add(owner.Id);
                            cola.Enqueue(owner);
                        }
                    }
                }
            }

            return redMismoMaterial;
        }

        private static bool EsElementoDeRedTraversable(Element e)
        {
            if (e == null || e.Category == null) return false;

#if REVIT2024_OR_LATER
            long catId = e.Category.Id.Value;
            return catId == (long)BuiltInCategory.OST_PipeCurves ||
                   catId == (long)BuiltInCategory.OST_PipeFitting ||
                   catId == (long)BuiltInCategory.OST_PipeAccessory;
#else
            int catId = e.Category.Id.IntegerValue;
            return catId == (int)BuiltInCategory.OST_PipeCurves ||
                   catId == (int)BuiltInCategory.OST_PipeFitting ||
                   catId == (int)BuiltInCategory.OST_PipeAccessory;
#endif
        }

        private static ConnectorSet GetConnectors(Element e)
        {
            if (e is MEPCurve mepCurve)
            {
                return mepCurve.ConnectorManager?.Connectors;
            }
            if (e is FamilyInstance fi && fi.MEPModel != null)
            {
                return fi.MEPModel.ConnectorManager?.Connectors;
            }
            return null;
        }

        private static string ObtenerClaveMaterialYDiametro(Element pipe)
        {
            if (pipe == null) return string.Empty;

            string typeIdStr = pipe.GetTypeId().ToString();

            double diamFeet = 0;
            Parameter pDiam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (pDiam != null && pDiam.HasValue)
            {
                diamFeet = pDiam.AsDouble();
            }

            int diamMm = (int)Math.Round(diamFeet * 304.8);

            string sentido = "H";
            if (pipe.Location is LocationCurve lc && lc.Curve != null)
            {
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                XYZ v = p1 - p0;
                double len2D = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                if (len2D < 0.1 || Math.Abs(v.Normalize().Z) > 0.85)
                {
                    sentido = "Z"; // Bajadas / Subidas puramente verticales
                }
                else if (Math.Abs(v.Y) > Math.Abs(v.X))
                {
                    sentido = "V"; // Tramo vertical en el plano XY (sentido Y)
                }
                else
                {
                    sentido = "H"; // Tramo horizontal en el plano XY (sentido X)
                }
            }

            return $"{typeIdStr}_{diamMm}_{sentido}";
        }

        /// <summary>
        /// Busca el tipo de etiqueta de tubería (Pipe Tag) correspondiente a Material + Dimensión (priorizando 'Type Name + Size').
        /// </summary>
        private static ElementId ObtenerTipoPipeTagMaterial(Document doc)
        {
            try
            {
                var pipeTagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeTags)
                    .WhereElementIsElementType()
                    .ToList();

                if (pipeTagTypes.Count > 0)
                {
                    // 1. PRIORIDAD ABSOLUTA: Tipo que contenga "Type Name + Size" o combinaciones de Nombre + Tamaño/Diámetro
                    var sizeAndTypeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name;
                        return name.IndexOf("Type Name + Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("TypeName + Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (sizeAndTypeMatch != null) return sizeAndTypeMatch.Id;

                    // 2. Coincidencia por Familia "DC - Tag tubería" y Tipo con "Size", "Diámetro" o "+"
                    var famSizeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string famName = ObtenerNombreFamilia(t);
                        return (famName.IndexOf("DC - Tag tubería", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                famName.IndexOf("DC. tag", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               (t.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("+", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (famSizeMatch != null) return famSizeMatch.Id;

                    // 3. Coincidencia por "Size", "Tamaño" o "Diámetro"
                    var sizeOnlyMatch = pipeTagTypes.FirstOrDefault(t =>
                        t.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Tamaño", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (sizeOnlyMatch != null) return sizeOnlyMatch.Id;

                    // 4. Fallback: coincidencia por familia DC - Tag tubería
                    var famMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string famName = ObtenerNombreFamilia(t);
                        return famName.IndexOf("DC - Tag tubería", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               famName.IndexOf("DC. tag", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (famMatch != null) return famMatch.Id;

                    // 5. Fallback: primer tipo disponible
                    return pipeTagTypes.First().Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private static string ObtenerNombreFamilia(Element t)
        {
            if (t == null) return string.Empty;
            if (t is FamilySymbol fs && fs.Family != null)
            {
                return fs.Family.Name ?? string.Empty;
            }
            var param = t.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
            if (param != null) return param.AsString() ?? string.Empty;
            return string.Empty;
        }

        /// <summary>
        /// Flujo por selección múltiple para colocar Tags "C.N" en cambios de nivel en desagües / tuberías.
        /// </summary>
        public static int TaguearCambioDeNivelHumedasPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            FamilySymbol tagSymbol = ObtenerTagPipeAccesorio(doc);
            if (tagSymbol == null)
            {
                ElementId spotTypeId = ObtenerTipoSpotElevation(doc);
                return TaguearCambioDeNivelSpotElevationFallback(uidoc, doc, view, spotTypeId);
            }

            List<Reference> refsList = new List<Reference>();

            var filter = new CodoCambioDeNivelSelectionFilter();
            // 1. Preselección
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        refsList.Add(new Reference(el));
                    }
                }
            }

            // 2. Si no había preselección, permitir selección por recuadro o clics continuos con ESC para finalizar
            if (refsList.Count == 0)
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
                            refsList.Add(new Reference(el));
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (refsList.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en codos de cambio de nivel (presiona ESC cuando termines para colocar tags):"
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

                                if (!refsList.Any(r => r.ElementId == pick.ElementId))
                                {
                                    refsList.Add(pick);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (refsList.Count == 0) return 0;

            int colocados = 0;
            int omitidos = 0;
            List<string> motivosOmitidos = new List<string>();

            using (Transaction tx = new Transaction(doc, "Tags C.N por Selección (Redes Húmedas)"))
            {
                tx.Start();

                if (!tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                foreach (var pickRef in refsList)
                {
                    try
                    {
                        Element el = doc.GetElement(pickRef.ElementId);
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

                        XYZ puntoEje = ObtenerPuntoEjeDesdeClic(el, pickRef.GlobalPoint);
                        if (puntoEje == null) continue;

                        XYZ tagHeadPos = puntoEje + new XYZ(0.8, 0.8, 0);

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

        public static int TaguearCambioDeNivelHumedasPorClic(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            int colocados = 0;
            FamilySymbol tagSymbol = ObtenerTagPipeAccesorio(doc);

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

                        // Asignar parámetros si existen en el elemento
                        Parameter pCambio = el.LookupParameter("DC. TAG conduit accesorio")
                                         ?? el.LookupParameter("DC. TAG accesorio")
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

        /// <summary>
        /// Flujo automático para taguear TODOS los cambios de nivel válidos en la vista activa para Redes Húmedas.
        /// Recolecta los codos de tuberías y fittings, filtra los codos a 90° y giros horizontales,
        /// agrupa pares de codos pertenecientes a la misma bajada/subida (para no duplicar tag)
        /// y coloca el Tag C.N automáticamente.
        /// </summary>
        public static int TaguearCambioDeNivelHumedasTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            var categorias = new[]
            {
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_ConduitFitting,
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

            FamilySymbol tagSymbol = ObtenerTagPipeAccesorio(doc);
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
                                   tagFamName.Contains("accesorio") ||
                                   tagFamName.Contains("cambio de nivel") ||
                                   tagSymName.Contains("cambio de nivel") ||
                                   tagSymName.Contains("c.n") ||
                                   tagText.Contains("c.n") ||
                                   tagText.Contains("cambio");

                    if (!esTagCN) continue;

                    ElementId hostId = GetTaggedHostElementId(t);
                    if (hostId != null && hostId != ElementId.InvalidElementId)
                    {
                        elementosYaTagueados.Add(hostId);
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
            using (Transaction tx = new Transaction(doc, "Tags C.N Automático Desagües"))
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
                                             ?? codo.LookupParameter("DC. TAG accesorio")
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
                motivoRechazo = "Debes seleccionar un codo / accesorio (Fitting), no un tramo de tubería o conduit.";
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
                motivoRechazo = "No aplica: Es un 'caballito' o salto de cruce (la tubería regresa a su nivel original).";
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

                    if (conn.CoordinateSystem != null && Math.Abs(conn.CoordinateSystem.BasisZ.Z) >= 0.15)
                    {
                        presentaCambioZ = true;
                    }

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

        public static bool EsCodoDe90Grados(FamilyInstance fi)
        {
            if (fi == null) return false;

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

        public static bool EsCaballitoOSaltoDeCruce(FamilyInstance fi)
        {
            if (fi?.MEPModel?.ConnectorManager == null) return false;

            var connectors = fi.MEPModel.ConnectorManager.Connectors.Cast<Connector>().ToList();
            if (connectors.Count < 2) return false;

            List<double> zExtremos = new List<double>();
            double zCodo = fi.Location is LocationPoint lp ? lp.Point.Z : connectors[0].Origin.Z;

            foreach (Connector conn in connectors)
            {
                double zExtremo = ObtenerElevacionTramoPrincipalHumedas(conn, fi.Id, 4, 10.0, out _);
                if (!double.IsNaN(zExtremo))
                {
                    zExtremos.Add(zExtremo);
                }
            }

            if (zExtremos.Count == 2)
            {
                double diffExtremos = Math.Abs(zExtremos[0] - zExtremos[1]);
                double alturaSalto = Math.Abs(zCodo - zExtremos[0]);

                if (diffExtremos < 0.08 && (alturaSalto >= 0.10 || TieneInclinacionVerticalHumedas(fi)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TieneInclinacionVerticalHumedas(FamilyInstance fi)
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

        private static double ObtenerElevacionTramoPrincipalHumedas(Connector startConn, ElementId originId, int maxProfundidad, double maxDistanciaFt, out ElementId levelId)
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

        public static FamilySymbol ObtenerTagPipeAccesorio(Document doc)
        {
            try
            {
                var tagSymbols = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                var coincidenciaFamilia = tagSymbols.FirstOrDefault(s =>
                    (s.Family?.Name?.IndexOf("DC- Tag accesorio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("DC. TAG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("Tag accesorio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG accesorio", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (s.Name.IndexOf("Cambio de nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Name.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0));

                if (coincidenciaFamilia != null) return coincidenciaFamilia;

                var coincidenciaNombre = tagSymbols.FirstOrDefault(s =>
                    s.Category != null &&
                    (s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFittingTags ||
                     s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags ||
                     s.Category.Name.ToLower().Contains("tag")) &&
                    (s.Name.IndexOf("Cambio de nivel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Name.IndexOf("C.N", StringComparison.OrdinalIgnoreCase) >= 0));

                if (coincidenciaNombre != null) return coincidenciaNombre;

                var fallbackPipeTag = tagSymbols.FirstOrDefault(s =>
                    s.Category != null &&
                    (s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFittingTags ||
                     s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags));

                return fallbackPipeTag ?? tagSymbols.FirstOrDefault(s => s.Category != null && s.Category.Name.ToLower().Contains("tag"));
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

        private static XYZ ObtenerPuntoEjeDesdeClic(Element el, XYZ puntoClic)
        {
            try
            {
                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    IntersectionResult ir = lc.Curve.Project(puntoClic);
                    if (ir != null)
                    {
                        return ir.XYZPoint;
                    }
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
            }
            catch { }

            return puntoClic;
        }

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
            }
            catch { }

            return null;
        }

        private static ElementId ObtenerTipoSpotElevation(Document doc)
        {
            try
            {
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

                if (objetivo != null) return objetivo.Id;

                SpotDimensionType baseType = spotTypes.FirstOrDefault();
                if (baseType != null)
                {
                    SpotDimensionType nuevoTipo = null;
                    if (doc.IsModifiable)
                    {
                        try { nuevoTipo = baseType.Duplicate("DC - Elevacion relativa") as SpotDimensionType; } catch { }
                    }
                    else
                    {
                        using (Transaction tx = new Transaction(doc, "Crear tipo Spot Elevation DC"))
                        {
                            tx.Start();
                            try { nuevoTipo = baseType.Duplicate("DC - Elevacion relativa") as SpotDimensionType; } catch { }
                            tx.Commit();
                        }
                    }

                    if (nuevoTipo != null) return nuevoTipo.Id;
                }

                var dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => t.Category != null && t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_SpotElevations)
                    .ToList();

                var objetivoDim = dimTypes.FirstOrDefault(t =>
                    t.Name.IndexOf("DC - Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("DC - Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Elevacion relativa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Elevación relativa", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? dimTypes.FirstOrDefault();

                return objetivoDim?.Id;
            }
            catch
            {
                return null;
            }
        }

        private static Level ObtenerNivelTecho(Document doc, Autodesk.Revit.DB.View view, Element el, XYZ puntoEje)
        {
            try
            {
                var todosNiveles = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (todosNiveles.Count == 0) return null;

                // 1. Determinar la coordenada Z real de la tubería / conducto
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

                // 2. Si tenemos la Z real de la tubería, buscar SIEMPRE el nivel inmediatamente superior en Z
                if (zElemento.HasValue)
                {
                    var nivelInmediatamenteSuperior = todosNiveles
                        .FirstOrDefault(l => l.Elevation > zElemento.Value + 0.05);

                    if (nivelInmediatamenteSuperior != null)
                    {
                        return nivelInmediatamenteSuperior;
                    }

                    // Si la tubería está en la parte más alta sin niveles superiores, tomar el nivel más alto
                    return todosNiveles.LastOrDefault();
                }

                // 3. Fallback: Nivel base de la vista
                Level nivelVista = view?.GenLevel;
                if (nivelVista != null)
                {
                    var supVista = todosNiveles.FirstOrDefault(l => l.Elevation > nivelVista.Elevation + 0.05);
                    if (supVista != null) return supVista;
                    return nivelVista;
                }

                return todosNiveles.LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static SpotDimension ColocarSpotElevation(
            Document doc,
            Autodesk.Revit.DB.View view,
            Element el,
            Reference pickRef,
            XYZ puntoEje,
            XYZ puntoUbicacion,
            ElementId spotTypeId,
            Level nivelTecho)
        {
            XYZ refPt = puntoEje;
            XYZ bendPt;
            XYZ endPt;

            if (puntoUbicacion != null)
            {
                double shoulderLen = 1.2;
                bendPt = new XYZ(puntoEje.X, puntoUbicacion.Y, puntoEje.Z);
                double dirX = (puntoUbicacion.X < puntoEje.X) ? -1.0 : 1.0;
                endPt = new XYZ(bendPt.X + dirX * shoulderLen, bendPt.Y, bendPt.Z);
            }
            else
            {
                double offsetSubida = 1.5;
                double longitudHombro = 1.2;
                bendPt = new XYZ(puntoEje.X, puntoEje.Y + offsetSubida, puntoEje.Z);
                endPt = new XYZ(bendPt.X + longitudHombro, bendPt.Y, bendPt.Z);
            }

            SpotDimension spot = null;

            if (pickRef != null)
            {
                try
                {
                    spot = doc.Create.NewSpotElevation(view, pickRef, refPt, bendPt, endPt, refPt, true);
                }
                catch { }
            }

            if (spot == null && el != null)
            {
                if (ObtenerReferenciaCaraSuperiorYPoint(el, view, puntoEje, out Reference faceRef, out XYZ puntoCara))
                {
                    try
                    {
                        spot = doc.Create.NewSpotElevation(view, faceRef, puntoCara, bendPt, endPt, puntoCara, true);
                    }
                    catch { }
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
            }

            return spot;
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

                        double distXY = Math.Sqrt(Math.Pow(pt.X - puntoMedioEje.X, 2) + Math.Pow(pt.Y - puntoMedioEje.Y, 2));
                        if (distXY > 1.0) continue;

                        if (pt.Z < puntoMedioEje.Z - 0.01) continue;

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

        private static void ConfigurarSpotElevation(SpotDimension spot, Level nivelTecho, Document doc)
        {
            if (spot == null) return;

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
                catch { }
            }

            try
            {
                Parameter pDisplay = spot.get_Parameter(BuiltInParameter.SPOT_ELEV_DISPLAY_ELEVATIONS)
                                  ?? spot.LookupParameter("Display Elevations")
                                  ?? spot.LookupParameter("Mostrar elevaciones");

                if (pDisplay != null && !pDisplay.IsReadOnly)
                {
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
            catch { }

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

            try
            {
                doc.Regenerate();
            }
            catch { }
        }

        #region 4. TUBERÍAS EMBEBIDAS EN PISO / AFINADO Y EMBEBIDAS EN PLACA

        public static int TaguearEmbebidasPisoPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new List<Element>();

            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var filter = new PipeSelectionFilter();
            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        pipes.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, solicitar selección por recuadro o clics continuos con ESC para finalizar
            if (pipes.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre las tuberías en piso/afinado a acotar:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        pipes.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (pipes.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en tuberías en piso/afinado a acotar (presiona ESC cuando termines para acotar):"
                            );
                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (el != null && !pipes.Any(x => x.Id == el.Id))
                                {
                                    pipes.Add(el);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (pipes.Count == 0) return 0;

            return ProcesarAcotadoEmbebidasEnPiso(doc, view, pipes);
        }

        public static int TaguearEmbebidasPisoTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (pipes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Piso", "No se encontraron tuberías de desagüe en la vista activa.");
                return 0;
            }

            return ProcesarAcotadoEmbebidasEnPiso(doc, view, pipes);
        }

        public static int TaguearEmbebidasPlacaPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new List<Element>();

            var filter = new PipeSelectionFilter();
            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        pipes.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, solicitar selección por recuadro o clics continuos con ESC para finalizar
            if (pipes.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre las tuberías en placa a acotar:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        pipes.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (pipes.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en tuberías en placa a acotar (presiona ESC cuando termines para acotar):"
                            );
                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (el != null && !pipes.Any(x => x.Id == el.Id))
                                {
                                    pipes.Add(el);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (pipes.Count == 0) return 0;

            return ProcesarAcotadoEmbebidasEnPlaca(doc, view, pipes);
        }

        public static int TaguearEmbebidasPlacaTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (pipes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Placa", "No se encontraron tuberías en la vista activa.");
                return 0;
            }

            return ProcesarAcotadoEmbebidasEnPlaca(doc, view, pipes);
        }

        private static int ProcesarAcotadoEmbebidasEnPiso(Document doc, Autodesk.Revit.DB.View view, List<Element> pipes)
        {
            if (pipes == null || pipes.Count == 0) return 0;

            int cotasCreadas = 0;
            using (Transaction tx = new Transaction(doc, "Acotar Tuberías Embebidas en Piso"))
            {
                tx.Start();

                DimensionType dimType = ObtenerTipoCotaLineal(doc);

                // Obtener muros arquitectónicos y estructurales de la vista
                var muros = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .ToList();

                var columnas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var pipe in pipes)
                {
                    if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) continue;

                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    if (v2D.GetLength() < 0.3) continue;

                    XYZ dirTubo = v2D.Normalize();
                    XYZ perp = new XYZ(-dirTubo.Y, dirTubo.X, 0);
                    XYZ mid = (p0 + p1) * 0.5;

                    // Buscar el muro más cercano
                    Wall mejorMuro = null;
                    Reference refCaraMuro = null;
                    double menorDistMuro = double.MaxValue;
                    XYZ puntoSobreMuro = null;

                    foreach (var wall in muros)
                    {
                        if (!(wall.Location is LocationCurve wLc) || wLc.Curve == null) continue;
                        XYZ wp0 = wLc.Curve.GetEndPoint(0);
                        XYZ wp1 = wLc.Curve.GetEndPoint(1);
                        XYZ wDir = new XYZ(wp1.X - wp0.X, wp1.Y - wp0.Y, 0);
                        if (wDir.GetLength() < 0.2) continue;
                        wDir = wDir.Normalize();

                        // Comprobar si el muro es aproximadamente paralelo a la tubería
                        if (Math.Abs(dirTubo.DotProduct(wDir)) > 0.85)
                        {
                            IntersectionResult proj = wLc.Curve.Project(mid);
                            if (proj != null)
                            {
                                double dist = (proj.XYZPoint - mid).GetLength();
                                if (dist < menorDistMuro && dist <= 20.0) // hasta 20 ft (6m)
                                {
                                    Reference rCara = ObtenerReferenciaCaraMuro(wall, view, perp);
                                    if (rCara != null)
                                    {
                                        menorDistMuro = dist;
                                        mejorMuro = wall;
                                        refCaraMuro = rCara;
                                        puntoSobreMuro = proj.XYZPoint;
                                    }
                                }
                            }
                        }
                    }

                    // Obtener referencia de la tubería
                    Reference refPipe = ObtenerReferenciaEjeTuberia(pipe, view);

                    if (refCaraMuro != null && refPipe != null && puntoSobreMuro != null)
                    {
                        try
                        {
                            ReferenceArray refArray = new ReferenceArray();
                            refArray.Append(refCaraMuro);
                            refArray.Append(refPipe);

                            // Línea perpendicular donde se colocará la cota
                            XYZ pDimStart = puntoSobreMuro;
                            XYZ pDimEnd = mid;
                            if ((pDimEnd - pDimStart).GetLength() > 0.05)
                            {
                                Line dimLine = Line.CreateBound(pDimStart, pDimEnd);
                                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray, dimType);
                                if (dim != null) cotasCreadas++;
                            }
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            return cotasCreadas;
        }

        private static int ProcesarAcotadoEmbebidasEnPlaca(Document doc, Autodesk.Revit.DB.View view, List<Element> pipes)
        {
            if (pipes == null || pipes.Count == 0) return 0;

            int cotasCreadas = 0;
            using (Transaction tx = new Transaction(doc, "Acotar Tuberías Embebidas en Placa"))
            {
                tx.Start();

                // 1. Ajustar View Range para ver la estructura inferior (-15 cm)
                AjustarViewRangeParaPlaca(doc, view);

                DimensionType dimType = ObtenerTipoCotaLineal(doc);

                // Obtener elementos estructurales visibles (vigas, columnas estructurales, muros estructurales)
                var vigas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                var columnas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToList();

                var murosEstruc = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(w => w.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing)
                    .ToList();

                foreach (var pipe in pipes)
                {
                    if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) continue;

                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    if (v2D.GetLength() < 0.3) continue;

                    XYZ dirTubo = v2D.Normalize();
                    XYZ perp = new XYZ(-dirTubo.Y, dirTubo.X, 0);
                    XYZ mid = (p0 + p1) * 0.5;

                    Reference refEstructura = null;
                    XYZ puntoEstructura = null;
                    double menorDist = double.MaxValue;

                    // Buscar en vigas
                    foreach (var viga in vigas)
                    {
                        if (viga.Location is LocationCurve vLc && vLc.Curve != null)
                        {
                            XYZ vp0 = vLc.Curve.GetEndPoint(0);
                            XYZ vp1 = vLc.Curve.GetEndPoint(1);
                            XYZ vDir = new XYZ(vp1.X - vp0.X, vp1.Y - vp0.Y, 0);
                            if (vDir.GetLength() < 0.2) continue;
                            vDir = vDir.Normalize();

                            if (Math.Abs(dirTubo.DotProduct(vDir)) > 0.85)
                            {
                                IntersectionResult proj = vLc.Curve.Project(mid);
                                if (proj != null)
                                {
                                    double dist = (proj.XYZPoint - mid).GetLength();
                                    if (dist < menorDist && dist <= 20.0)
                                    {
                                        Reference rCara = ObtenerReferenciaElementoEstructural(viga, view, perp);
                                        if (rCara != null)
                                        {
                                            menorDist = dist;
                                            refEstructura = rCara;
                                            puntoEstructura = proj.XYZPoint;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Si no hubo vigas, buscar en muros estructurales
                    if (refEstructura == null)
                    {
                        foreach (var wall in murosEstruc)
                        {
                            if (!(wall.Location is LocationCurve wLc) || wLc.Curve == null) continue;
                            XYZ wp0 = wLc.Curve.GetEndPoint(0);
                            XYZ wp1 = wLc.Curve.GetEndPoint(1);
                            XYZ wDir = new XYZ(wp1.X - wp0.X, wp1.Y - wp0.Y, 0);
                            if (wDir.GetLength() < 0.2) continue;
                            wDir = wDir.Normalize();

                            if (Math.Abs(dirTubo.DotProduct(wDir)) > 0.85)
                            {
                                IntersectionResult proj = wLc.Curve.Project(mid);
                                if (proj != null)
                                {
                                    double dist = (proj.XYZPoint - mid).GetLength();
                                    if (dist < menorDist && dist <= 20.0)
                                    {
                                        Reference rCara = ObtenerReferenciaCaraMuro(wall, view, perp);
                                        if (rCara != null)
                                        {
                                            menorDist = dist;
                                            refEstructura = rCara;
                                            puntoEstructura = proj.XYZPoint;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    Reference refPipe = ObtenerReferenciaEjeTuberia(pipe, view);

                    if (refEstructura != null && refPipe != null && puntoEstructura != null)
                    {
                        try
                        {
                            ReferenceArray refArray = new ReferenceArray();
                            refArray.Append(refEstructura);
                            refArray.Append(refPipe);

                            XYZ pDimStart = puntoEstructura;
                            XYZ pDimEnd = mid;
                            if ((pDimEnd - pDimStart).GetLength() > 0.05)
                            {
                                Line dimLine = Line.CreateBound(pDimStart, pDimEnd);
                                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray, dimType);
                                if (dim != null) cotasCreadas++;
                            }
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            return cotasCreadas;
        }

        #region 5. TUBERÍAS ELEVADAS / SOBRE CIELO RASO

        /// <summary>
        /// Flujo por selección para acotar tuberías elevadas o sobre cielo raso a la estructura superior.
        /// </summary>
        public static int TaguearTuberiaElevadaPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new List<Element>();

            var filter = new PipeSelectionFilter();
            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        pipes.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, solicitar selección por recuadro o clics continuos con ESC para finalizar
            if (pipes.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre las tuberías elevadas / cielo raso a acotar:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        pipes.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (pipes.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en tuberías elevadas / cielo raso (presiona ESC cuando termines para acotar):"
                            );
                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (el != null && !pipes.Any(x => x.Id == el.Id))
                                {
                                    pipes.Add(el);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (pipes.Count == 0) return 0;

            return ProcesarAcotadoTuberiaElevada(doc, view, pipes);
        }

        /// <summary>
        /// Flujo automático para acotar todas las tuberías elevadas de la vista hacia la estructura inmediatamente superior.
        /// </summary>
        public static int TaguearTuberiaElevadaTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (pipes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tuberías Elevadas", "No se encontraron tuberías en la vista activa.");
                return 0;
            }

            return ProcesarAcotadoTuberiaElevada(doc, view, pipes);
        }

        private static int ProcesarAcotadoTuberiaElevada(Document doc, Autodesk.Revit.DB.View view, List<Element> pipes)
        {
            if (pipes == null || pipes.Count == 0) return 0;

            int cotasCreadas = 0;
            using (Transaction tx = new Transaction(doc, "Acotar Tuberías Elevadas a Estructura Superior"))
            {
                tx.Start();

                DimensionType dimType = ObtenerTipoCotaLineal(doc);

                // Obtener elementos estructurales visibles (Vigas / Framing superior, Columnas, Muros estructurales)
                var vigas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                var columnas = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .ToList();

                var murosEstruc = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(w => w.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing)
                    .ToList();

                // Si no hay vigas específicas en la vista, buscar en el documento
                if (vigas.Count == 0)
                {
                    vigas = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .ToList();
                }

                foreach (var pipe in pipes)
                {
                    if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) continue;

                    XYZ p0 = lc.Curve.GetEndPoint(0);
                    XYZ p1 = lc.Curve.GetEndPoint(1);
                    XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                    if (v2D.GetLength() < 0.2) continue;

                    XYZ dirTubo = v2D.Normalize();
                    XYZ perp = new XYZ(-dirTubo.Y, dirTubo.X, 0);
                    XYZ mid = (p0 + p1) * 0.5;

                    Reference refPipe = ObtenerReferenciaEjeTuberia(pipe, view);
                    if (refPipe == null) continue;

                    // 1. Acotar perpendicular a la viga más cercana
                    Element mejorViga = null;
                    Reference refVigaCara = null;
                    XYZ puntoViga = null;
                    double menorDist = double.MaxValue;

                    foreach (var viga in vigas)
                    {
                        if (viga.Location is LocationCurve vLc && vLc.Curve != null)
                        {
                            XYZ vp0 = vLc.Curve.GetEndPoint(0);
                            XYZ vp1 = vLc.Curve.GetEndPoint(1);
                            XYZ vDir = new XYZ(vp1.X - vp0.X, vp1.Y - vp0.Y, 0);
                            if (vDir.GetLength() < 0.2) continue;
                            vDir = vDir.Normalize();

                            if (Math.Abs(dirTubo.DotProduct(vDir)) > 0.70)
                            {
                                IntersectionResult proj = vLc.Curve.Project(mid);
                                if (proj != null)
                                {
                                    double dist = (proj.XYZPoint - mid).GetLength();
                                    if (dist < menorDist && dist <= 25.0)
                                    {
                                        Reference rCara = ObtenerReferenciaElementoEstructural(viga, view, perp);
                                        if (rCara != null)
                                        {
                                            menorDist = dist;
                                            mejorViga = viga;
                                            refVigaCara = rCara;
                                            puntoViga = proj.XYZPoint;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Si no hubo viga paralela, buscar en muros estructurales
                    if (refVigaCara == null)
                    {
                        foreach (var wall in murosEstruc)
                        {
                            if (!(wall.Location is LocationCurve wLc) || wLc.Curve == null) continue;
                            XYZ wp0 = wLc.Curve.GetEndPoint(0);
                            XYZ wp1 = wLc.Curve.GetEndPoint(1);
                            XYZ wDir = new XYZ(wp1.X - wp0.X, wp1.Y - wp0.Y, 0);
                            if (wDir.GetLength() < 0.2) continue;
                            wDir = wDir.Normalize();

                            if (Math.Abs(dirTubo.DotProduct(wDir)) > 0.70)
                            {
                                IntersectionResult proj = wLc.Curve.Project(mid);
                                if (proj != null)
                                {
                                    double dist = (proj.XYZPoint - mid).GetLength();
                                    if (dist < menorDist && dist <= 25.0)
                                    {
                                        Reference rCara = ObtenerReferenciaCaraMuro(wall, view, perp);
                                        if (rCara != null)
                                        {
                                            menorDist = dist;
                                            refVigaCara = rCara;
                                            puntoViga = proj.XYZPoint;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (refVigaCara != null && puntoViga != null)
                    {
                        try
                        {
                            ReferenceArray refArray = new ReferenceArray();
                            refArray.Append(refVigaCara);
                            refArray.Append(refPipe);

                            XYZ pDimStart = puntoViga;
                            XYZ pDimEnd = mid;
                            if ((pDimEnd - pDimStart).GetLength() > 0.05)
                            {
                                Line dimLine = Line.CreateBound(pDimStart, pDimEnd);
                                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray, dimType);
                                if (dim != null) cotasCreadas++;
                            }
                        }
                        catch { }
                    }

                    // 2. Acotar también a vigas transversales / de apoyo (ortogonales como en Figura 24)
                    XYZ dirTrans = (Math.Abs(dirTubo.X) > Math.Abs(dirTubo.Y)) ? XYZ.BasisY : XYZ.BasisX;
                    Reference refTransCara = null;
                    XYZ puntoTrans = null;
                    double menorDistTrans = double.MaxValue;

                    foreach (var viga in vigas)
                    {
                        if (viga == mejorViga) continue;
                        if (viga.Location is LocationCurve vLc && vLc.Curve != null)
                        {
                            IntersectionResult proj = vLc.Curve.Project(p0);
                            if (proj != null)
                            {
                                double dist = (proj.XYZPoint - p0).GetLength();
                                if (dist < menorDistTrans && dist <= 20.0)
                                {
                                    Reference rCara = ObtenerReferenciaElementoEstructural(viga, view, dirTrans);
                                    if (rCara != null)
                                    {
                                        menorDistTrans = dist;
                                        refTransCara = rCara;
                                        puntoTrans = proj.XYZPoint;
                                    }
                                }
                            }
                        }
                    }

                    if (refTransCara != null && puntoTrans != null)
                    {
                        try
                        {
                            ReferenceArray refArray = new ReferenceArray();
                            refArray.Append(refTransCara);
                            refArray.Append(refPipe);

                            XYZ pDimStart = puntoTrans;
                            XYZ pDimEnd = p0;
                            if ((pDimEnd - pDimStart).GetLength() > 0.05)
                            {
                                Line dimLine = Line.CreateBound(pDimStart, pDimEnd);
                                Dimension dim = doc.Create.NewDimension(view, dimLine, refArray, dimType);
                                if (dim != null) cotasCreadas++;
                            }
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            return cotasCreadas;
        }

        #endregion

        private static void AjustarViewRangeParaPlaca(Document doc, Autodesk.Revit.DB.View view)
        {
            if (view is ViewPlan vp)
            {
                try
                {
                    PlanViewRange vr = vp.GetViewRange();
                    // -15 cm equivalentes al grosor de placa + 5 cm
                    double offsetFt = -0.15 * 3.28084;
                    vr.SetOffset(PlanViewPlane.BottomClipPlane, offsetFt);
                    vr.SetOffset(PlanViewPlane.ViewDepthPlane, offsetFt);
                    vp.SetViewRange(vr);
                }
                catch { }
            }
        }

        private static DimensionType ObtenerTipoCotaLineal(Document doc)
        {
            try
            {
                var dimTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(t => t.StyleType == DimensionStyleType.Linear || t.StyleType == DimensionStyleType.LinearFixed)
                    .ToList();

                var pref = dimTypes.FirstOrDefault(t => t.Name.IndexOf("Diagonal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        t.Name.IndexOf("Cota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        t.Name.IndexOf("Linear", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        t.Name.IndexOf("Alineada", StringComparison.OrdinalIgnoreCase) >= 0);
                return pref ?? dimTypes.FirstOrDefault();
            }
            catch { return null; }
        }

        private static Reference ObtenerReferenciaCaraMuro(Wall wall, Autodesk.Revit.DB.View view, XYZ dirNormalCota)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = false };
                GeometryElement geom = wall.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    if (Math.Abs(pf.FaceNormal.DotProduct(dirNormalCota)) > 0.80)
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
            return null;
        }

        private static Reference ObtenerReferenciaElementoEstructural(Element elem, Autodesk.Revit.DB.View view, XYZ dirNormalCota)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom != null)
                {
                    foreach (GeometryObject obj in geom)
                    {
                        if (obj is Solid solid)
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (face is PlanarFace pf)
                                {
                                    if (Math.Abs(pf.FaceNormal.DotProduct(dirNormalCota)) > 0.80)
                                    {
                                        if (face.Reference != null) return face.Reference;
                                    }
                                }
                            }
                        }
                        else if (obj is GeometryInstance gInst)
                        {
                            var instGeom = gInst.GetInstanceGeometry();
                            if (instGeom != null)
                            {
                                foreach (var gObj in instGeom)
                                {
                                    if (gObj is Solid s)
                                    {
                                        foreach (Face f in s.Faces)
                                        {
                                            if (f.Reference != null) return f.Reference;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Reference ObtenerReferenciaEjeTuberia(Element pipe, Autodesk.Revit.DB.View view)
        {
            try
            {
                Options opt = new Options { ComputeReferences = true, View = view, IncludeNonVisibleObjects = true };
                GeometryElement geom = pipe.get_Geometry(opt);
                if (geom != null)
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
                                if (face.Reference != null)
                                    return face.Reference;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region 6. TAGS DE PASES EN VIGA (PASE DE VIGA - SIZE ONLY)

        /// <summary>
        /// Busca el tipo de etiqueta de tubería que muestra únicamente el parámetro 'Size' (Diámetro del elemento).
        /// </summary>
        private static ElementId ObtenerTipoPipeTagSoloSize(Document doc)
        {
            try
            {
                var pipeTagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeTags)
                    .WhereElementIsElementType()
                    .ToList();

                if (pipeTagTypes.Count > 0)
                {
                    // 1. PRIORIDAD: Tipo cuyo nombre sea exactamente "Size", "Size only" o "Diámetro"
                    var exactSize = pipeTagTypes.FirstOrDefault(t =>
                        string.Equals(t.Name.Trim(), "Size", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.Name.Trim(), "Size only", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.Name.Trim(), "Tamaño", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.Name.Trim(), "Diámetro", StringComparison.OrdinalIgnoreCase));
                    if (exactSize != null) return exactSize.Id;

                    // 2. Tipo que contenga "Size" y NO contenga "Type", "Material", "Name", "+"
                    var pureSizeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string n = t.Name;
                        bool hasSize = n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       n.IndexOf("Diametro", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool hasOther = n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        n.IndexOf("+", StringComparison.OrdinalIgnoreCase) >= 0;
                        return hasSize && !hasOther;
                    });
                    if (pureSizeMatch != null) return pureSizeMatch.Id;

                    // 3. Familia "DC - Tag tubería" con tipo "Size"
                    var famSizeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string famName = ObtenerNombreFamilia(t);
                        return (famName.IndexOf("DC - Tag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                famName.IndexOf("Tag tubería", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               (t.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (famSizeMatch != null) return famSizeMatch.Id;

                    // 4. Cualquier tag que contenga "Size"
                    var anySizeMatch = pipeTagTypes.FirstOrDefault(t =>
                        t.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (anySizeMatch != null) return anySizeMatch.Id;

                    // 5. Fallback a cualquier Pipe Tag
                    return pipeTagTypes.First().Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// Flujo por selección para taguear pases en viga mostrando únicamente el tamaño/diámetro (Size) del elemento.
        /// </summary>
        public static int TaguearPasesEnVigaPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId sizeTagTypeId = ObtenerTipoPipeTagSoloSize(doc);
            if (sizeTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Pases en Viga",
                    "No se encontró una familia de etiqueta de tubería con parámetro 'Size' cargada en el proyecto.");
                return 0;
            }

            List<Element> pipes = new List<Element>();

            var filter = new PipeSelectionFilter();
            // 1. Revisar si el usuario ya tenía tuberías seleccionadas en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        pipes.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, solicitar selección por recuadro o clics continuos con ESC para finalizar
            if (pipes.Count == 0)
            {
                try
                {
                    var rectElements = uidoc.Selection.PickElementsByRectangle(
                        filter,
                        "Arrastra un recuadro sobre las tuberías en pases de viga a taguear:"
                    );
                    if (rectElements != null && rectElements.Count > 0)
                    {
                        pipes.AddRange(rectElements);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch { }

                if (pipes.Count == 0)
                {
                    while (true)
                    {
                        try
                        {
                            Reference pick = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                "Clic en tuberías en pases de viga (presiona ESC cuando termines para taguear):"
                            );
                            if (pick != null)
                            {
                                Element el = doc.GetElement(pick.ElementId);
                                if (el != null && !pipes.Any(x => x.Id == el.Id))
                                {
                                    pipes.Add(el);
                                }
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // Al presionar ESC termina la selección y procede
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }

            if (pipes.Count == 0) return 0;

            return ProcesarTagueoPasesEnViga(doc, view, pipes, sizeTagTypeId);
        }

        /// <summary>
        /// Flujo automático para taguear todos los pases de tuberías en vigas en la vista activa.
        /// </summary>
        public static int TaguearPasesEnVigaTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId sizeTagTypeId = ObtenerTipoPipeTagSoloSize(doc);
            if (sizeTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Pases en Viga",
                    "No se encontró una familia de etiqueta de tubería con parámetro 'Size' cargada en el proyecto.");
                return 0;
            }

            List<Element> pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (pipes.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Pases en Viga", "No se encontraron tuberías en la vista activa.");
                return 0;
            }

            return ProcesarTagueoPasesEnViga(doc, view, pipes, sizeTagTypeId);
        }

        private static bool CalcularPosicionTagSizeOptima(
            Element pipe,
            XYZ pPase,
            Autodesk.Revit.DB.View view,
            MaterialTagCollisionContext ctx,
            out XYZ mejorPos,
            out TagOrientation mejorOrientacion)
        {
            mejorPos = null;
            mejorOrientacion = TagOrientation.Horizontal;

            if (pipe == null || !(pipe.Location is LocationCurve lc) || lc.Curve == null) return false;

            XYZ p0 = lc.Curve.GetEndPoint(0);
            XYZ p1 = lc.Curve.GetEndPoint(1);
            XYZ dir = p1 - p0;
            double len2D = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (len2D < 0.2) return false;

            XYZ dir2D = new XYZ(dir.X / len2D, dir.Y / len2D, 0);
            bool esVertical = Math.Abs(dir2D.Y) > Math.Abs(dir2D.X);
            mejorOrientacion = esVertical ? TagOrientation.Vertical : TagOrientation.Horizontal;

            // Escala de la vista para dimensionar la caja del tag (ej. "Ø4""):
            double scale = (view != null && view.Scale > 0) ? (double)view.Scale : 50.0;
            double textHeightModel = (2.5 / 1000.0 * scale) * 3.28084; // ~0.41 ft
            double textWidthModel = (12.0 / 1000.0 * scale) * 3.28084;  // ~1.97 ft (compacto para Size)

            double halfW = (mejorOrientacion == TagOrientation.Horizontal) ? (textWidthModel * 0.55) : (textHeightModel * 0.85);
            double halfH = (mejorOrientacion == TagOrientation.Horizontal) ? (textHeightModel * 0.85) : (textWidthModel * 0.55);

            // Radio exterior de la tubería
            double radioExterior = 0.18;
            Parameter pDiamExt = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)
                              ?? pipe.LookupParameter("Outside Diameter")
                              ?? pipe.LookupParameter("Diámetro exterior")
                              ?? pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (pDiamExt != null && pDiamExt.HasValue)
            {
                radioExterior = pDiamExt.AsDouble() * 0.5;
            }

            // Distancia mínima ajustada al borde de la tubería (+10 mm de separación hacia afuera)
            double sepMinima = radioExterior + (esVertical ? halfW : halfH) + 0.16;

            XYZ perp2D = new XYZ(-dir2D.Y, dir2D.X, 0);

            // Distancias perpendiculares a explorar (lado derecho, lado izquierdo, y pequeños ajustes si hay colisión)
            double[] distanciasPerp = new double[]
            {
                sepMinima,
                -sepMinima,
                sepMinima + 0.25,
                -(sepMinima + 0.25),
                sepMinima + 0.50,
                -(sepMinima + 0.50),
                sepMinima + 0.85,
                -(sepMinima + 0.85)
            };

            // Pequeños desplazamientos longitudinales si en el cruce hay una etiqueta o texto existente
            double[] shiftsLong = new double[] { 0.0, 0.40, -0.40, 0.80, -0.80 };

            double menorPenalidad = double.MaxValue;

            foreach (double dPerp in distanciasPerp)
            {
                foreach (double sAlong in shiftsLong)
                {
                    XYZ testPos = pPase + (perp2D * dPerp) + (dir2D * sAlong);

                    TagBox2D tagBox = new TagBox2D(
                        testPos.X - halfW,
                        testPos.X + halfW,
                        testPos.Y - halfH,
                        testPos.Y + halfH
                    );

                    double penalidad = 0.0;

                    if (ctx != null)
                    {
                        // 1. Colisión con otras tuberías
                        foreach (var obs in ctx.PipeObstacles)
                        {
                            if (tagBox.Intersects(obs, 0.10)) penalidad += 30000.0;
                        }

                        // 2. Colisión con fittings/accesorios
                        foreach (var fit in ctx.FittingObstacles)
                        {
                            if (tagBox.Intersects(fit, 0.10)) penalidad += 25000.0;
                        }

                        // 3. Colisión con otros tags o textos de vista (ej. "Baño PMR", otros tags)
                        foreach (var tObs in ctx.TagObstacles)
                        {
                            if (tagBox.Intersects(tObs, 0.12)) penalidad += 40000.0;
                        }

                        // 4. Otros obstáculos (aparatos, equipos)
                        foreach (var oth in ctx.OtherObstacles)
                        {
                            if (tagBox.Intersects(oth, 0.08)) penalidad += 20000.0;
                        }
                    }

                    // Penalizar distancia innecesaria (mantenerlo cerca al tubo como en Tag Material)
                    penalidad += Math.Abs(dPerp) * 25.0 + Math.Abs(sAlong) * 15.0;

                    // Preferir lado derecho en verticales o lado superior en horizontales si ambos están libres
                    if (dPerp < 0) penalidad += 2.0;

                    if (penalidad < menorPenalidad)
                    {
                        menorPenalidad = penalidad;
                        mejorPos = testPos;
                    }
                }
            }

            return mejorPos != null;
        }

        private static int ProcesarTagueoPasesEnViga(Document doc, Autodesk.Revit.DB.View view, List<Element> pipes, ElementId sizeTagTypeId)
        {
            if (pipes == null || pipes.Count == 0) return 0;

            // Construir contexto de colisiones de la vista (para no superponer sobre otras tuberías, fittings o tags)
            MaterialTagCollisionContext ctx = MaterialTagCollisionContext.BuildFromView(doc, view);

            // Obtener vigas en la vista para detectar puntos de cruce (pases)
            var vigas = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList();

            if (vigas.Count == 0)
            {
                vigas = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();
            }

            int tagsCreados = 0;
            using (Transaction tx = new Transaction(doc, "Tags Pases en Viga (Size)"))
            {
                tx.Start();

                foreach (var pipe in pipes)
                {
                    try
                    {
                        if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) continue;

                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        XYZ dir = (p1 - p0).Normalize();

                        List<XYZ> puntosPase = new List<XYZ>();

                        // 1. Detectar intersecciones con vigas
                        foreach (var viga in vigas)
                        {
                            if (viga.Location is LocationCurve vLc && vLc.Curve != null)
                            {
                                IntersectionResultArray ira;
                                SetComparisonResult res = lc.Curve.Intersect(vLc.Curve, out ira);
                                if (res == SetComparisonResult.Overlap && ira != null && ira.Size > 0)
                                {
                                    foreach (IntersectionResult ir in ira)
                                    {
                                        puntosPase.Add(ir.XYZPoint);
                                    }
                                }
                            }
                        }

                        // 2. Si no se detectó intersección matemática exacta (e.g. cruce a diferente cota), usar punto medio
                        if (puntosPase.Count == 0)
                        {
                            puntosPase.Add((p0 + p1) * 0.5);
                        }

                        foreach (XYZ pPase in puntosPase)
                        {
                            XYZ tagPos;
                            TagOrientation orientacion;
                            if (!CalcularPosicionTagSizeOptima(pipe, pPase, view, ctx, out tagPos, out orientacion))
                            {
                                bool esVert = Math.Abs(dir.Y) > Math.Abs(dir.X);
                                orientacion = esVert ? TagOrientation.Vertical : TagOrientation.Horizontal;
                                tagPos = esVert ? pPase + new XYZ(0.35, 0, 0) : pPase + new XYZ(0, -0.35, 0);
                            }

                            IndependentTag newTag = IndependentTag.Create(
                                doc,
                                view.Id,
                                new Reference(pipe),
                                false,
                                TagMode.TM_ADDBY_CATEGORY,
                                orientacion,
                                tagPos
                            );

                            if (newTag != null)
                            {
                                if (sizeTagTypeId != ElementId.InvalidElementId)
                                {
                                    try { newTag.ChangeTypeId(sizeTagTypeId); } catch { }
                                }

                                try
                                {
                                    newTag.TagHeadPosition = tagPos;
                                }
                                catch { }

                                tagsCreados++;
                                ctx.AddPlacedTag(tagPos, orientacion, 0.9, 0.3);
                            }
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return tagsCreados;
        }

        #endregion
    }
}

