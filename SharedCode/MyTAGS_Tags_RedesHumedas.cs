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

            // 2. Si no había preselección, permitir selección con PickObjects (+, -, Finish)
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

                        TagOrientation orientacion = TagOrientation.Horizontal;

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
        /// Valida si el elemento es una tubería apta para taguear,
        /// ignorando tramos nulos o menores a 6 cm (0.2 ft).
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
                return lc.Curve.Length >= 0.2;
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
        /// Flujo por selección (recuadro, preselección o clics sucesivos) para etiquetar el material y diámetro de tuberías
        /// mediante la familia 'DC - Tag tubería 2 mm' (Type Name + Size).
        /// </summary>
        public static int TaguearMaterialPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            ElementId materialTagTypeId = ObtenerTipoPipeTagMaterial(doc);
            if (materialTagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material",
                    "No se encontró ninguna familia de etiqueta de tubería ('DC - Tag tubería 2 mm' / 'Type Name + Size' / 'Pipe Tag') cargada en el proyecto.");
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

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (pipes.Count == 0)
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
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !pipes.Any(x => x.Id == el.Id))
                            {
                                pipes.Add(el);
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

            if (pipes.Count == 0) return 0;

            // Agrupar tuberías seleccionadas que pertenezcan a la misma red y tengan el mismo diámetro y material
            List<List<Element>> clusters = new List<List<Element>>();
            foreach (var p in pipes)
            {
                if (p == null || !EsTuberiaValidaParaTag(p)) continue;
                string diam = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsValueString() ?? "";
                string mat = p.LookupParameter("Material")?.AsString() ?? p.LookupParameter("Comments")?.AsString() ?? "";

                bool agregado = false;
                foreach (var cluster in clusters)
                {
                    var first = cluster[0];
                    string fDiam = first.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsValueString() ?? "";
                    string fMat = first.LookupParameter("Material")?.AsString() ?? first.LookupParameter("Comments")?.AsString() ?? "";

                    if (string.Equals(diam, fDiam, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(mat, fMat, StringComparison.OrdinalIgnoreCase))
                    {
                        // Verificar si están conectados o cercanos (< 7 ft)
                        XYZ pMidCurrent = (p.Location is LocationCurve lcp && lcp.Curve != null) ? (lcp.Curve.GetEndPoint(0) + lcp.Curve.GetEndPoint(1)) * 0.5 : XYZ.Zero;
                        bool conectado = cluster.Any(cp =>
                        {
                            XYZ pMidC = (cp.Location is LocationCurve lcc && lcc.Curve != null) ? (lcc.Curve.GetEndPoint(0) + lcc.Curve.GetEndPoint(1)) * 0.5 : XYZ.Zero;
                            return pMidCurrent.DistanceTo(pMidC) < 7.0 || EstanConectadas(cp, p);
                        });

                        if (conectado)
                        {
                            cluster.Add(p);
                            agregado = true;
                            break;
                        }
                    }
                }

                if (!agregado)
                {
                    clusters.Add(new List<Element> { p });
                }
            }

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

                foreach (var cluster in clusters)
                {
                    try
                    {
                        // Elegir la tubería más larga del grupo
                        Element pipe = cluster.OrderByDescending(cp =>
                        {
                            if (cp.Location is LocationCurve lc && lc.Curve != null)
                            {
                                XYZ p0 = lc.Curve.GetEndPoint(0);
                                XYZ p1 = lc.Curve.GetEndPoint(1);
                                XYZ dir = p1 - p0;
                                double len2D = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                                return len2D;
                            }
                            return 0.0;
                        }).FirstOrDefault() ?? cluster[0];

                        if (pipe == null || !EsTuberiaValidaParaTag(pipe)) continue;

                        if (!ObtenerPuntoMedioYDireccion(pipe, out XYZ puntoMitad, out XYZ dirCurva)) continue;

                        if (!CalcularPosicionTagMaterialOptima(pipe, view, ctx, out XYZ tagPos, out TagOrientation orientacion, out bool usarLeader, out _))
                        {
                            orientacion = TagOrientation.Horizontal;
                            bool esVert = Math.Abs(dirCurva.Z) > 0.8 || (Math.Abs(dirCurva.Y) > Math.Abs(dirCurva.X) * 1.5);
                            tagPos = esVert ? puntoMitad + new XYZ(0.85, 0, 0) : puntoMitad + new XYZ(0, -0.85, 0);
                            usarLeader = false;
                        }

                        IndependentTag tag = null;
                        try
                        {
                            tag = IndependentTag.Create(
                                doc,
                                materialTagTypeId,
                                view.Id,
                                new Reference(pipe),
                                usarLeader,
                                TagOrientation.Horizontal,
                                tagPos
                            );
                        }
                        catch
                        {
                            try
                            {
                                tag = IndependentTag.Create(
                                    doc,
                                    view.Id,
                                    new Reference(pipe),
                                    usarLeader,
                                    TagMode.TM_ADDBY_CATEGORY,
                                    TagOrientation.Horizontal,
                                    tagPos
                                );
                                if (tag != null && materialTagTypeId != ElementId.InvalidElementId)
                                {
                                    try { tag.ChangeTypeId(materialTagTypeId); } catch { }
                                }
                            }
                            catch { }
                        }

                        if (tag != null)
                        {
                            tag.TagOrientation = TagOrientation.Horizontal;
                            tag.HasLeader = usarLeader;
                            if (usarLeader)
                            {
                                tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            }
                            tag.TagHeadPosition = tagPos;

                            // Verificación geométrica para evitar que el tag quede vertical
                            try
                            {
                                doc.Regenerate();
                                BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                                if (bb != null)
                                {
                                    double w = Math.Abs(bb.Max.X - bb.Min.X);
                                    double h = Math.Abs(bb.Max.Y - bb.Min.Y);
                                    if (h > w * 1.25)
                                    {
                                        Line axis = Line.CreateBound(tagPos, tagPos + XYZ.BasisZ);
                                        ElementTransformUtils.RotateElement(doc, tag.Id, axis, -Math.PI / 2.0);
                                    }
                                }
                            }
                            catch { }

                            colocados++;
                            ctx.AddPlacedTag(tagPos, TagOrientation.Horizontal);
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            if (colocados > 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material", $"Se colocaron exitosamente {colocados} etiquetas de material.");
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
                    "No se encontró ninguna familia de etiqueta de tubería ('DC - Tag tubería 2 mm' / 'Type Name + Size' / 'Pipe Tag') cargada en el proyecto.");
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

                // Construir contexto de colisiones de la vista para encontrar zona en blanco
                MaterialTagCollisionContext ctx = MaterialTagCollisionContext.BuildFromView(doc, view, new[] { pipe.Id });

                if (!CalcularPosicionTagMaterialOptima(pipe, view, ctx, out XYZ tagPos, out TagOrientation orientacion, out bool usarLeader, out _))
                {
                    orientacion = TagOrientation.Horizontal;
                    bool esVert = Math.Abs(dirCurva.Z) > 0.8 || (Math.Abs(dirCurva.Y) > Math.Abs(dirCurva.X) * 1.5);
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

                    IndependentTag tag = null;
                    try
                    {
                        tag = IndependentTag.Create(
                            doc,
                            materialTagTypeId,
                            view.Id,
                            new Reference(pipe),
                            usarLeader,
                            orientacion,
                            tagPos
                        );
                    }
                    catch
                    {
                        try
                        {
                            tag = IndependentTag.Create(
                                doc,
                                view.Id,
                                new Reference(pipe),
                                usarLeader,
                                TagMode.TM_ADDBY_CATEGORY,
                                orientacion,
                                tagPos
                            );
                            if (tag != null && materialTagTypeId != ElementId.InvalidElementId)
                            {
                                try { tag.ChangeTypeId(materialTagTypeId); } catch { }
                            }
                        }
                        catch { }
                    }

                    if (tag != null)
                    {
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
                Autodesk.Revit.UI.TaskDialog.Show("Tag Material", $"Se colocaron exitosamente {colocados} etiquetas de material.");
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
            mejorOrientacion = TagOrientation.Horizontal;

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

                // Filtrar tuberías planares (excluyendo subidas/bajadas verticales puras en Z)
                var tuberiasPlanares = redCompleta.Where(t =>
                {
                    if (t.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        XYZ dir = p1 - p0;
                        double len2D = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                        return len2D >= 0.4 && Math.Abs(dir.Normalize().Z) < 0.85;
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
                        IndependentTag newTag = null;
                        try
                        {
                            newTag = IndependentTag.Create(
                                doc,
                                materialTagTypeId,
                                view.Id,
                                new Reference(tuberia),
                                usarLeader,
                                orientacion,
                                tagPos
                            );
                        }
                        catch
                        {
                            try
                            {
                                newTag = IndependentTag.Create(
                                    doc,
                                    view.Id,
                                    new Reference(tuberia),
                                    usarLeader,
                                    TagMode.TM_ADDBY_CATEGORY,
                                    orientacion,
                                    tagPos
                                );
                                if (newTag != null && materialTagTypeId != ElementId.InvalidElementId)
                                {
                                    try { newTag.ChangeTypeId(materialTagTypeId); } catch { }
                                }
                            }
                            catch { }
                        }

                        if (newTag != null)
                        {
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

        private static bool EstanConectadas(Element e1, Element e2)
        {
            if (e1 == null || e2 == null || e1.Id == e2.Id) return true;
            try
            {
                ConnectorSet cSet1 = GetConnectors(e1);
                if (cSet1 == null) return false;
                foreach (Connector c in cSet1)
                {
                    if (c == null || !c.IsConnected) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        if (other != null && other.Owner != null && other.Owner.Id == e2.Id)
                            return true;
                    }
                }
            }
            catch { }
            return false;
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

                if (pipeTagTypes.Count == 0)
                {
                    pipeTagTypes = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .Where(fs => fs.Category != null && (
#if REVIT2024_OR_LATER
                            fs.Category.Id.Value == (long)BuiltInCategory.OST_PipeTags ||
#else
                            fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeTags ||
#endif
                            fs.Category.Name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fs.Category.Name.IndexOf("Tuber", StringComparison.OrdinalIgnoreCase) >= 0))
                        .Cast<Element>()
                        .ToList();
                }

                if (pipeTagTypes.Count > 0)
                {
                    // 1. PRIORIDAD ABSOLUTA: Tipo que contenga "Type Name + Size" o combinaciones de Nombre + Tamaño/Diámetro
                    var sizeAndTypeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? string.Empty;
                        return name.IndexOf("Type Name + Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("TypeName + Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Nombre de tipo + Tamaño", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Material + Diámetro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Material + Diametro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (sizeAndTypeMatch != null) return sizeAndTypeMatch.Id;

                    // 2. Coincidencia por Familia "DC - Tag tubería" y Tipo con "Size", "Diámetro" o "+"
                    var famSizeMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string famName = ObtenerNombreFamilia(t);
                        return (famName.IndexOf("DC - Tag tubería", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                famName.IndexOf("DC. tag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                famName.IndexOf("TAG - Tuberia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                famName.IndexOf("Tag Tubería", StringComparison.OrdinalIgnoreCase) >= 0) &&
                               (t.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("+", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                t.Name.IndexOf("Diametro", StringComparison.OrdinalIgnoreCase) >= 0);
                    });
                    if (famSizeMatch != null) return famSizeMatch.Id;

                    // 3. Coincidencia por "Size", "Tamaño", "Diámetro" o "Diametro"
                    var sizeOnlyMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string name = t.Name ?? string.Empty;
                        return name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Tamaño", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Diametro", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (sizeOnlyMatch != null) return sizeOnlyMatch.Id;

                    // 4. Fallback: coincidencia por familia DC - Tag tubería o Pipe Tag
                    var famMatch = pipeTagTypes.FirstOrDefault(t =>
                    {
                        string famName = ObtenerNombreFamilia(t);
                        return famName.IndexOf("DC - Tag tubería", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               famName.IndexOf("DC. tag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               famName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               famName.IndexOf("Tuber", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (famMatch != null) return famMatch.Id;

                    // 5. Fallback: primer tipo disponible
                    return pipeTagTypes.First().Id;
                }

                // 6. Si no hay OST_PipeTags, buscar en MultiCategoryTags
                var multiTags = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .WhereElementIsElementType()
                    .ToList();
                if (multiTags.Count > 0)
                {
                    var multiMatch = multiTags.FirstOrDefault(t =>
                    {
                        string name = (t.Name ?? string.Empty) + " " + ObtenerNombreFamilia(t);
                        return name.IndexOf("Tuber", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    return (multiMatch ?? multiTags.First()).Id;
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

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (pipes.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tuberías en piso/afinado a acotar y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !pipes.Any(x => x.Id == el.Id))
                            {
                                pipes.Add(el);
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

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (pipes.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tuberías en placa a acotar y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !pipes.Any(x => x.Id == el.Id))
                            {
                                pipes.Add(el);
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

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (pipes.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tuberías elevadas / cielo raso a acotar y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !pipes.Any(x => x.Id == el.Id))
                            {
                                pipes.Add(el);
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

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (pipes.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona las tuberías en pases de viga a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !pipes.Any(x => x.Id == el.Id))
                            {
                                pipes.Add(el);
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
            mejorOrientacion = TagOrientation.Horizontal;

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
                                orientacion = TagOrientation.Horizontal;
                                bool esVert = Math.Abs(dir.Y) > Math.Abs(dir.X);
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

        #region 7. TAGS DE DUCTOS (HVAC / REDES HÚMEDAS Y MECÁNICAS)

        public class DuctSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
#if REVIT2024_OR_LATER
                long catId = elem.Category.Id.Value;
                return catId == (long)BuiltInCategory.OST_DuctCurves || catId == (long)BuiltInCategory.OST_DuctFitting;
#else
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_DuctCurves || catId == (int)BuiltInCategory.OST_DuctFitting;
#endif
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        public static int TaguearDuctosSoloDimensionesPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            return EjecutarFlujoSeleccionDuctos(uidoc, doc, view, false);
        }

        public static int TaguearDuctosSoloDimensionesTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            return EjecutarFlujoAutoDuctos(uidoc, doc, view, false);
        }

        public static int TaguearDuctosDimensionesMaterialPorSeleccion(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            return EjecutarFlujoSeleccionDuctos(uidoc, doc, view, true);
        }

        public static int TaguearDuctosDimensionesMaterialTodoEnVista(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view)
        {
            return EjecutarFlujoAutoDuctos(uidoc, doc, view, true);
        }

        private static int EjecutarFlujoSeleccionDuctos(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, bool incluirMaterial)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> ductos = new List<Element>();
            var filter = new DuctSelectionFilter();

            // 1. Revisar si el usuario ya tenía ductos seleccionados en Revit
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        ductos.Add(el);
                    }
                }
            }

            // 2. Si no había preselección, permitir selección interactiva con cursor (+) y (-) y botón Finish
            if (ductos.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        "Selecciona los ductos y accesorios a taguear y haz clic en 'Finish':"
                    );
                    if (pickedRefs != null && pickedRefs.Count > 0)
                    {
                        foreach (var r in pickedRefs)
                        {
                            Element el = doc.GetElement(r.ElementId);
                            if (el != null && !ductos.Any(x => x.Id == el.Id))
                            {
                                ductos.Add(el);
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

            if (ductos.Count == 0) return 0;

            return ProcesarTagueoDuctos(doc, view, ductos, incluirMaterial);
        }

        private static int EjecutarFlujoAutoDuctos(UIDocument uidoc, Document doc, Autodesk.Revit.DB.View view, bool incluirMaterial)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Element> curves = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .ToList();

            List<Element> fittings = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DuctFitting)
                .WhereElementIsNotElementType()
                .ToList();

            List<Element> todos = new List<Element>();
            todos.AddRange(curves);
            todos.AddRange(fittings);

            if (todos.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos", "No se encontraron ductos en la vista activa.");
                return 0;
            }

            return ProcesarTagueoDuctos(doc, view, todos, incluirMaterial);
        }

        private static int ProcesarTagueoDuctos(Document doc, Autodesk.Revit.DB.View view, List<Element> ductos, bool incluirMaterial)
        {
            if (ductos == null || ductos.Count == 0) return 0;

            ElementId tagTypeId = ObtenerTipoDuctTag(doc, incluirMaterial);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos",
                    "No se encontró ninguna familia de etiqueta de ducto cargada en el proyecto (OST_DuctTags).");
                return 0;
            }

            var tagSymbol = doc.GetElement(tagTypeId) as FamilySymbol;
            int tagsCreados = 0;

            using (Transaction tx = new Transaction(doc, incluirMaterial ? "Taguear Ductos (Dimensiones + Material)" : "Taguear Ductos (Solo Dimensiones)"))
            {
                tx.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                double scale = (view.Scale > 0) ? (double)view.Scale : 50.0;
                double offsetBaseFt = (4.0 / 1000.0 * scale) * 3.28084; // ~4mm en escala

                foreach (var ducto in ductos)
                {
                    if (ducto == null) continue;

                    XYZ pHost = null;
                    XYZ perp = XYZ.BasisY;
                    bool esVertical = false;

                    if (ducto.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ p0 = lc.Curve.GetEndPoint(0);
                        XYZ p1 = lc.Curve.GetEndPoint(1);
                        XYZ v2D = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                        if (v2D.GetLength() < 0.25)
                        {
                            // Ducto vertical (montante o bajante)
                            esVertical = true;
                            pHost = new XYZ(p0.X, p0.Y, (p0.Z + p1.Z) * 0.5);
                            perp = -XYZ.BasisX;
                        }
                        else
                        {
                            XYZ dir = v2D.Normalize();
                            perp = new XYZ(-dir.Y, dir.X, 0);
                            pHost = (p0 + p1) * 0.5;
                        }
                    }
                    else if (ducto.Location is LocationPoint lp)
                    {
                        pHost = lp.Point;
                    }

                    if (pHost == null) continue;

                    // Extraer dimensión y materialidad
                    string sizeStr = ObtenerDimensionDucto(ducto);
                    string matStr = ObtenerMaterialidadDucto(doc, ducto);

                    string valorComentario = incluirMaterial ? $"{sizeStr} - {matStr}" : sizeStr;

                    // Inyectar en parámetro Comments/Comentarios para dinamismo
                    try
                    {
                        Parameter pCom = ducto.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                      ?? ducto.LookupParameter("Comentarios")
                                      ?? ducto.LookupParameter("Comments");
                        if (pCom != null && !pCom.IsReadOnly)
                        {
                            pCom.Set(valorComentario);
                        }
                    }
                    catch { }

                    // Calcular semiancho o semialto del ducto
                    double halfDuctFt = 0.6;
                    Parameter pWidth = ducto.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)
                                    ?? ducto.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                    ?? ducto.LookupParameter("Width")
                                    ?? ducto.LookupParameter("Ancho");
                    if (pWidth != null && pWidth.HasValue && pWidth.AsDouble() > 0)
                    {
                        halfDuctFt = pWidth.AsDouble() * 0.5;
                    }

                    // Posicionar la cabeza de la etiqueta fuera del contorno del ducto con directriz
                    XYZ tagHeadPos;
                    if (esVertical)
                    {
                        tagHeadPos = pHost - new XYZ(halfDuctFt + offsetBaseFt + 0.5, 0, 0);
                    }
                    else
                    {
                        if (perp.Y < -0.1) perp = -perp; // Preferir arriba (+Y)
                        tagHeadPos = pHost + (perp * (halfDuctFt + offsetBaseFt)) + new XYZ(0.3, 0, 0);
                    }

                    try
                    {
                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagTypeId,
                            view.Id,
                            new Reference(ducto),
                            true, // con directriz
                            TagOrientation.Horizontal,
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

                            tagsCreados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return tagsCreados;
        }

        private static string ObtenerDimensionDucto(Element ducto)
        {
            try
            {
                Parameter pCalcSize = ducto.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)
                                   ?? ducto.LookupParameter("Size")
                                   ?? ducto.LookupParameter("Tamaño");
                if (pCalcSize != null && pCalcSize.HasValue && !string.IsNullOrWhiteSpace(pCalcSize.AsString()))
                {
                    return pCalcSize.AsString();
                }

                Parameter pW = ducto.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)
                            ?? ducto.LookupParameter("Width")
                            ?? ducto.LookupParameter("Ancho");
                Parameter pH = ducto.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)
                            ?? ducto.LookupParameter("Height")
                            ?? ducto.LookupParameter("Alto");
                Parameter pDiam = ducto.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                               ?? ducto.LookupParameter("Diameter")
                               ?? ducto.LookupParameter("Diámetro");

                if (pW != null && pH != null && pW.HasValue && pH.HasValue)
                {
                    double wInches = Math.Round(pW.AsDouble() * 12.0);
                    double hInches = Math.Round(pH.AsDouble() * 12.0);
                    return $"{wInches}\"x{hInches}\"";
                }
                else if (pDiam != null && pDiam.HasValue)
                {
                    double dInches = Math.Round(pDiam.AsDouble() * 12.0);
                    return $"Ø{dInches}\"";
                }
            }
            catch { }

            return "6\"x5\"";
        }

        private static string ObtenerMaterialidadDucto(Document doc, Element ducto)
        {
            try
            {
                // 1. Parámetro de material
                Parameter pMat = ducto.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                              ?? ducto.LookupParameter("Material")
                              ?? ducto.LookupParameter("Materialidad")
                              ?? ducto.LookupParameter("Duct Material");
                if (pMat != null && pMat.HasValue)
                {
                    if (pMat.StorageType == StorageType.String && !string.IsNullOrWhiteSpace(pMat.AsString()))
                    {
                        return pMat.AsString();
                    }
                    else if (pMat.StorageType == StorageType.ElementId && pMat.AsElementId() != ElementId.InvalidElementId)
                    {
                        Element matEl = doc.GetElement(pMat.AsElementId());
                        if (matEl != null && !string.IsNullOrWhiteSpace(matEl.Name))
                        {
                            return matEl.Name;
                        }
                    }
                }

                // 2. Nombre de tipo de ducto (ej. Lamina galvanizada / Rectangular Duct)
                Element typeEl = doc.GetElement(ducto.GetTypeId());
                if (typeEl != null && !string.IsNullOrWhiteSpace(typeEl.Name))
                {
                    string tName = typeEl.Name;
                    if (tName.IndexOf("Galvaniz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tName.IndexOf("Lamina", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tName.IndexOf("Chapa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tName.IndexOf("Fibra", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tName.IndexOf("Flexible", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return tName;
                    }
                }
            }
            catch { }

            return "Lamina galvanizada";
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

        private static ElementId ObtenerTipoDuctTag(Document doc, bool incluirMaterial)
        {
            try
            {
                var tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTags)
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
                    if (incluirMaterial)
                    {
                        // 1. Prioridad: Etiquetas que incluyan Size + Material / Tipo / Name
                        var matType = tagTypes.FirstOrDefault(t =>
                        {
                            string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                            return (n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Mat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Tipo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Lamina", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   (n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Dim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0);
                        });
                        if (matType != null) return matType.Id;

                        // 2. Fallback de material
                        var matFallback = tagTypes.FirstOrDefault(t =>
                        {
                            string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                            return n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   n.IndexOf("Tipo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   n.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                        if (matFallback != null) return matFallback.Id;
                    }
                    else
                    {
                        // Solo Dimensiones: Prioridad etiquetas de solo Size / Dimensión
                        var sizeType = tagTypes.FirstOrDefault(t =>
                        {
                            string n = (t.Name ?? "") + " " + ObtenerNombreFamiliaTag(t);
                            return (n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Dimension", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Tamaño", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Solo", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   n.IndexOf("Material", StringComparison.OrdinalIgnoreCase) < 0;
                        });
                        if (sizeType != null) return sizeType.Id;
                    }

                    // Fallback genérico: Primer tag disponible
                    return tagTypes.First().Id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        #endregion
    }
}

