
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PluginCotasExteriores.Body;
using RevitPlugin.UI;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_CielosRasos : IExternalCommand
    {
        private UIDocument _uidoc;
        private const double GeometryTolerance = 1e-6;
        private const double ConnectionTolerance = 0.02;  // ~6 mm vertex snap
        private const double MergeGapTolerance = 0.02;    // ~6 mm collinear merge gap

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            Document doc = _uidoc.Document;
            View view = doc.ActiveView;

            try
            {
                if (view.ViewType != ViewType.CeilingPlan)
                {
                    TaskDialog.Show("Error", "Usa una vista de cielo raso.");
                    return Result.Failed;
                }

                var ceilings = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType()
                    .ToElements();

                if (!ceilings.Any())
                {
                    TaskDialog.Show("Info", "No hay cielos rasos.");
                    return Result.Succeeded;
                }

                var tagTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_CeilingTags)
                    .Cast<FamilySymbol>()
                    .ToList();

                TagSelectorCielos window = new TagSelectorCielos(tagTypes);
                bool? result = window.ShowDialog();

                if (result != true)
                    return Result.Cancelled;

                string action = window.SelectedAction;
                FamilySymbol tagType = window.SelectedTag;

                if (action == "Acotar")
                {
                    var selectedCeilings = SelectCeilingsForDimension();

                    if (!selectedCeilings.Any())
                    {
                        TaskDialog.Show("Info", "No seleccionaste cielos válidos.");
                        return Result.Cancelled;
                    }

                    AcotarCielos(doc, view, selectedCeilings);

                    // Mantener selección visible después de crear cotas
                    _uidoc.Selection.SetElementIds(selectedCeilings.Select(e => e.Id).ToList());
                    return Result.Succeeded;
                }

                TaggearCielos(doc, view, ceilings, tagType);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", ex.ToString());
                return Result.Failed;
            }
        }

        // =========================================================
        // TAGS (SIN CAMBIOS)
        // =========================================================
        private void TaggearCielos(Document doc, View view,
            IEnumerable<Element> ceilings, FamilySymbol tagType)
        {
            using (Transaction tx = new Transaction(doc, "Tag Cielos"))
            {
                tx.Start();

                foreach (var ceiling in ceilings)
                {
                    BoundingBoxXYZ b = ceiling.get_BoundingBox(view);
                    if (b == null) continue;

                    XYZ center = (b.Min + b.Max) * 0.5;

                    IndependentTag tag = IndependentTag.Create(
                        doc, view.Id, new Reference(ceiling),
                        false, TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal, center);

                    if (tag != null && tagType != null)
                    {
                        tag.ChangeTypeId(tagType.Id);
                    }
                }

                tx.Commit();
            }
        }

        // =========================================================
        // MÉTODO DE COTAS
        // =========================================================
        private void AcotarCielos(Document doc, View view, IEnumerable<Element> ceilings)
        {
            var selectedCeilings = ceilings.ToList();
            if (!selectedCeilings.Any())
            {
                TaskDialog.Show("Cielos Rasos", "No hay cielos rasos seleccionados para acotar.");
                return;
            }

            using (Transaction tx = new Transaction(doc, "Acotar Cielos"))
            {
                tx.Start();

                int acotados = 0;
                try
                {
                    var selectedEdges = selectedCeilings
                        .SelectMany(ceiling => GetPlanEdgeReferences(ceiling, view, doc))
                        .GroupBy(edge => edge.StableId)
                        .Select(group => group.First())
                        .ToList();

                    if (!selectedEdges.Any())
                    {
                        tx.RollBack();
                        TaskDialog.Show("Cielos Rasos",
                            "No se encontraron bordes en los cielos seleccionados.\n" +
                            "Verifica que sean cielos válidos en esta vista.");
                        return;
                    }

                    // Shared sets across ALL groups so duplicates from near-identical
                    // boundaries (same edge, different reference paths) are suppressed globally.
                    var globalSeenH = new HashSet<string>();
                    var globalSeenV = new HashSet<string>();

                    var groups = BuildConnectedGroups(selectedEdges);
                    foreach (var group in groups)
                    {
                        var verticalBoundaries = CollapseNearDuplicates(MergeBoundaries(
                            group.Where(edge => edge.Orientation == EdgeOrientation.Vertical),
                            EdgeOrientation.Vertical));
                        var horizontalBoundaries = CollapseNearDuplicates(MergeBoundaries(
                            group.Where(edge => edge.Orientation == EdgeOrientation.Horizontal),
                            EdgeOrientation.Horizontal));

                        if (verticalBoundaries.Count < 2 || horizontalBoundaries.Count < 2)
                            continue;

                        acotados += BuildDimensionsForBands(
                            doc, view, verticalBoundaries, horizontalBoundaries,
                            EdgeOrientation.Horizontal, globalSeenH);

                        acotados += BuildDimensionsForBands(
                            doc, view, horizontalBoundaries, verticalBoundaries,
                            EdgeOrientation.Vertical, globalSeenV);
                    }

                    tx.Commit();

                    if (acotados == 0)
                        TaskDialog.Show("Cielos Rasos",
                            "No se crearon cotas.\nPosibles causas:\n" +
                            "• Los cielos no tienen geometría de bordes capturada\n" +
                            "• Faltan aristas verticales u horizontales\n" +
                            "• Los bordes están desconectados");
                    else
                        TaskDialog.Show("Resultado", $"✓ Cotas creadas: {acotados}");
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Error en AcotarCielos", ex.ToString());
                }
            }
        }

        private int BuildDimensionsForBands(
            Document doc,
            View view,
            List<BoundaryReference> primaryBoundaries,
            List<BoundaryReference> secondaryBoundaries,
            EdgeOrientation dimensionOrientation,
            HashSet<string> globalSeenPairs = null)
        {
            double z = GetDimensionZ(view);

            var orderedCuts = secondaryBoundaries
                .Select(b => b.FixedCoordinate)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            if (orderedCuts.Count < 2) return 0;

            double secMin = secondaryBoundaries.Min(b => b.FixedCoordinate);
            double secMax = secondaryBoundaries.Max(b => b.FixedCoordinate);

            var localSeen = new HashSet<string>();
            var seenPairs = globalSeenPairs ?? localSeen;

            // minSep converts 3.75 mm at paper scale to model feet so parallel dim lines don't overlap.
            double minSep = (3.75 / 304.8) * view.Scale;
            var usedPositions = new List<double>();

            int created = 0;

            for (int i = 0; i < orderedCuts.Count - 1; i++)
            {
                double bandStart = orderedCuts[i];
                double bandEnd   = orderedCuts[i + 1];
                if (bandEnd - bandStart < GeometryTolerance) continue;

                double bandCenter = (bandStart + bandEnd) * 0.5;

                var active = primaryBoundaries
                    .Where(b => b.Min <= bandCenter + GeometryTolerance &&
                                b.Max >= bandCenter - GeometryTolerance)
                    .OrderBy(b => b.FixedCoordinate)
                    .ToList();

                if (active.Count < 2) continue;

                // chain key = all active edge coordinates — deduplicates whole band across groups.
                string chainKey = string.Join("|", active.Select(b => b.FixedCoordinate.ToString("F4")));
                if (!seenPairs.Add(chainKey)) continue;

                // common overlap = range where ALL active edges coexist.
                double commonOverlapMin = active.Max(b => b.Min);
                double commonOverlapMax = active.Min(b => b.Max);
                double clampMin = Math.Max(commonOverlapMin, bandStart);
                double clampMax = Math.Min(commonOverlapMax, bandEnd);

                bool hasCommonOverlap = clampMax - clampMin >= GeometryTolerance;

                if (hasCommonOverlap)
                {
                    var candidates = new[]
                    {
                        (secMin + secMax) * 0.50,
                        (secMin + secMax) * 0.33,
                        (secMin + secMax) * 0.67,
                        bandCenter,
                    };

                    double pos = FindStaggeredPosition(candidates, clampMin, clampMax,
                                                       usedPositions, minSep, bandCenter);
                    usedPositions.Add(pos);

                    // Build refs array with all active edges for a single chain dim.
                    var refs = new ReferenceArray();
                    foreach (var b in active)
                        refs.Append(b.GetReferenceAt(pos));

                    Line dimLine = BuildDimLine(active[0], active[active.Count - 1],
                                               pos, z, dimensionOrientation);
                    try
                    {
                        if (doc.Create.NewDimension(view, dimLine, refs) != null)
                        {
                            created++;
                            // Block all consecutive sub-pairs so no later band
                            // re-creates a segment already covered by this chain.
                            for (int j = 0; j < active.Count - 1; j++)
                                seenPairs.Add($"{active[j].FixedCoordinate:F4}|{active[j + 1].FixedCoordinate:F4}");
                            continue;
                        }
                    }
                    catch { }
                }

                // Fallback: emit individual 2-point dims per consecutive pair.
                for (int j = 0; j < active.Count - 1; j++)
                {
                    var first  = active[j];
                    var second = active[j + 1];
                    if (second.FixedCoordinate - first.FixedCoordinate < GeometryTolerance)
                        continue;

                    string pairKey = $"{first.FixedCoordinate:F4}|{second.FixedCoordinate:F4}";
                    if (!seenPairs.Add(pairKey)) continue;

                    double pos = FindInteriorPosition(first, second, bandStart, bandEnd,
                        new[] {
                            (secMin + secMax) * 0.50,
                            (secMin + secMax) * 0.33,
                            (secMin + secMax) * 0.67,
                            bandCenter,
                        });

                    var refs = new ReferenceArray();
                    refs.Append(first.GetReferenceAt(pos));
                    refs.Append(second.GetReferenceAt(pos));

                    Line dimLine = BuildDimLine(first, second, pos, z, dimensionOrientation);
                    try
                    {
                        if (doc.Create.NewDimension(view, dimLine, refs) != null)
                            created++;
                    }
                    catch { }
                }
            }

            return created;
        }

        private static double FindStaggeredPosition(
            IEnumerable<double> candidates,
            double clampMin, double clampMax,
            List<double> usedPositions,
            double minSep,
            double fallbackCenter)
        {
            double Clamp(double v) => Math.Max(clampMin, Math.Min(clampMax, v));

            bool HasConflict(double p) =>
                usedPositions.Any(u => Math.Abs(u - p) < minSep);

            double bestConflicting = double.NaN;

            // Pass 1: candidate valid in geometry AND no stagger conflict.
            foreach (double c in candidates)
            {
                if (c < clampMin - GeometryTolerance || c > clampMax + GeometryTolerance)
                    continue;
                double p = Clamp(c);
                if (!HasConflict(p)) return p;
                if (double.IsNaN(bestConflicting)) bestConflicting = p;
            }

            // Pass 2: nearest used ± minSep nudged into range.
            if (usedPositions.Count > 0)
            {
                double nearest = usedPositions.OrderBy(u => Math.Abs(u - fallbackCenter)).First();
                foreach (double nudge in new[] { nearest + minSep, nearest - minSep })
                {
                    if (nudge >= clampMin - GeometryTolerance && nudge <= clampMax + GeometryTolerance)
                        return Clamp(nudge);
                }
            }

            // Fallback: accept conflict rather than fail.
            return double.IsNaN(bestConflicting) ? Clamp(fallbackCenter) : bestConflicting;
        }

        private static Line BuildDimLine(
            BoundaryReference first, BoundaryReference last,
            double pos, double z, EdgeOrientation orientation)
        {
            return orientation == EdgeOrientation.Horizontal
                ? Line.CreateBound(new XYZ(first.FixedCoordinate, pos, z),
                                   new XYZ(last.FixedCoordinate,  pos, z))
                : Line.CreateBound(new XYZ(pos, first.FixedCoordinate, z),
                                   new XYZ(pos, last.FixedCoordinate,  z));
        }

        // Returns the best position for the dimension line, constrained to:
        // 1. The overlap range where both primary edges exist.
        // 2. The current band [bandStart, bandEnd] — keeps the line inside the ceiling.
        private static double FindInteriorPosition(
            BoundaryReference first,
            BoundaryReference second,
            double bandStart,
            double bandEnd,
            IEnumerable<double> candidates)
        {
            double overlapMin = Math.Max(first.Min, second.Min);
            double overlapMax = Math.Min(first.Max, second.Max);

            bool Valid(double p) =>
                p >= overlapMin  - GeometryTolerance &&
                p <= overlapMax  + GeometryTolerance &&
                p >= bandStart   - GeometryTolerance &&
                p <= bandEnd     + GeometryTolerance;

            foreach (double c in candidates)
                if (Valid(c)) return c;

            // Fallback: center of the intersection of overlap and band.
            double clampMin = Math.Max(overlapMin, bandStart);
            double clampMax = Math.Min(overlapMax, bandEnd);
            if (clampMax - clampMin >= GeometryTolerance)
                return (clampMin + clampMax) * 0.5;

            // Last resort: band center (always in the band and active for this pair).
            return (bandStart + bandEnd) * 0.5;
        }

        private static List<List<EdgeReferenceInfo>> BuildConnectedGroups(List<EdgeReferenceInfo> edges)
        {
            // Spatial bucket: map rounded grid cell → edges with an endpoint there.
            // Reduces connectivity check from O(n²) to O(n·k) where k ≈ edges per cell.
            var buckets = new Dictionary<long, List<EdgeReferenceInfo>>();
            foreach (var edge in edges)
                foreach (var pt in edge.EndPoints)
                    AddToBucket(buckets, pt, edge);

            var groups = new List<List<EdgeReferenceInfo>>();
            var visited = new HashSet<string>();

            foreach (var edge in edges)
            {
                if (visited.Contains(edge.StableId)) continue;

                var stack = new Stack<EdgeReferenceInfo>();
                var group = new List<EdgeReferenceInfo>();
                stack.Push(edge);
                visited.Add(edge.StableId);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    group.Add(current);

                    foreach (var candidate in GetNeighborEdges(current, buckets))
                    {
                        if (visited.Contains(candidate.StableId)) continue;
                        visited.Add(candidate.StableId);
                        stack.Push(candidate);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private static void AddToBucket(
            Dictionary<long, List<EdgeReferenceInfo>> buckets,
            XYZ point,
            EdgeReferenceInfo edge)
        {
            long key = BucketKey(point);
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<EdgeReferenceInfo>();
            list.Add(edge);
        }

        private static long BucketKey(XYZ pt)
        {
            int gx = (int)Math.Round(pt.X / ConnectionTolerance);
            int gy = (int)Math.Round(pt.Y / ConnectionTolerance);
            return ((long)(gx + 100000)) * 1000000L + (gy + 100000);
        }

        private static IEnumerable<EdgeReferenceInfo> GetNeighborEdges(
            EdgeReferenceInfo edge,
            Dictionary<long, List<EdgeReferenceInfo>> buckets)
        {
            foreach (var pt in edge.EndPoints)
            {
                int bx = (int)Math.Round(pt.X / ConnectionTolerance);
                int by = (int)Math.Round(pt.Y / ConnectionTolerance);

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    long key = ((long)(bx + dx + 100000)) * 1000000L + (by + dy + 100000);
                    if (!buckets.TryGetValue(key, out var candidates)) continue;
                    foreach (var candidate in candidates)
                        if (AreConnected(edge, candidate))
                            yield return candidate;
                }
            }
        }

        private static bool AreConnected(EdgeReferenceInfo first, EdgeReferenceInfo second)
        {
            return first.EndPoints.Any(p1 => second.EndPoints.Any(p2 => p1.DistanceTo(p2) < ConnectionTolerance));
        }

        // Removes boundaries whose FixedCoordinate is within ConnectionTolerance of a
        // previously kept boundary. Handles FP noise when the same physical edge is
        // collected via multiple geometry paths with slightly different coordinates.
        private static List<BoundaryReference> CollapseNearDuplicates(List<BoundaryReference> boundaries)
        {
            var result = new List<BoundaryReference>();
            foreach (var b in boundaries.OrderBy(x => x.FixedCoordinate))
            {
                if (result.Count == 0 ||
                    Math.Abs(result[result.Count - 1].FixedCoordinate - b.FixedCoordinate) >= ConnectionTolerance)
                    result.Add(b);
            }
            return result;
        }

        private static List<BoundaryReference> MergeBoundaries(
            IEnumerable<EdgeReferenceInfo> edges,
            EdgeOrientation orientation)
        {
            var grouped = edges
                .GroupBy(edge => Math.Round(edge.FixedCoordinate, 6))
                .OrderBy(group => group.Key);

            var merged = new List<BoundaryReference>();
            foreach (var group in grouped)
            {
                var ordered = group.OrderBy(edge => edge.MinAlong).ToList();
                if (!ordered.Any()) continue;

                var current = new BoundaryReference(
                    orientation,
                    ordered[0].FixedCoordinate,
                    ordered[0].MinAlong,
                    ordered[0].MaxAlong,
                    ordered[0].Reference);

                foreach (var edge in ordered.Skip(1))
                {
                    if (edge.MinAlong <= current.Max + MergeGapTolerance)
                    {
                        // Keep all sub-segment references so GetReferenceAt finds the right one.
                        current.ExtendWith(edge.MinAlong, edge.MaxAlong, edge.Reference);
                        continue;
                    }

                    merged.Add(current);
                    current = new BoundaryReference(
                        orientation, edge.FixedCoordinate, edge.MinAlong, edge.MaxAlong, edge.Reference);
                }

                merged.Add(current);
            }

            return merged;
        }

        private List<Element> SelectCeilingsForDimension()
        {
            // Usar preselección si el usuario ya tenía cielos seleccionados antes del comando.
            var preselected = _uidoc.Selection
                .GetElementIds()
                .Select(id => _uidoc.Document.GetElement(id))
                .Where(IsCeilingElement)
                .ToList();

            if (preselected.Any())
                return preselected;

            var selectedCeilings = new Dictionary<int, Element>();

            try
            {
                while (true)
                {
                    var pickedRef = _uidoc.Selection.PickObject(
                        ObjectType.PointOnElement,
                        new CeilingSelectionFilter(),
                        "Selecciona cielos rasos uno a uno. ESC para terminar.");

                    var element = _uidoc.Document.GetElement(pickedRef.ElementId);
                    if (!IsCeilingElement(element)) continue;

                    int id = element.Id.IntegerValue;
                    if (!selectedCeilings.ContainsKey(id))
                        selectedCeilings[id] = element;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return selectedCeilings.Values.ToList();
            }
        }

        private static bool IsCeilingElement(Element element)
        {
            return element?.Category != null &&
                   element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings;
        }

        private static double GetDimensionZ(View view)
        {
            if (view.GenLevel != null)
                return view.GenLevel.Elevation;

            return 0.0;
        }

        private static List<EdgeReferenceInfo> GetPlanEdgeReferences(Element ceiling, View view, Document doc)
        {
            var references = new Dictionary<string, EdgeReferenceInfo>();
            CollectSolidEdgeReferences(ceiling, view, doc, references);
            CollectHorizontalFaceEdgeReferences(ceiling, view, doc, references);
            CollectDependentCurveReferences(ceiling, doc, references);
            return references.Values.ToList();
        }

        private static void CollectSolidEdgeReferences(
            Element ceiling,
            View view,
            Document doc,
            IDictionary<string, EdgeReferenceInfo> references)
        {
            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                View = view
            };

            var lines = new List<Line>();
            GeometryHelpers.GetLinesFromGeometryElement(ceiling.get_Geometry(options), ref lines);

            foreach (var line in lines)
            {
                if (line.Reference == null)
                    continue;

                AddReferenceFromLine(line, line.Reference, doc, references);
            }
        }

        // Iterates solid face loops directly so we get edge.Reference for every
        // boundary edge — more reliable than GeometryHelpers for complex shapes.
        private static void CollectHorizontalFaceEdgeReferences(
            Element ceiling,
            View view,
            Document doc,
            IDictionary<string, EdgeReferenceInfo> references)
        {
            try
            {
                var options = new Options { ComputeReferences = true, View = view };
                var geomElement = ceiling.get_Geometry(options);
                if (geomElement == null) return;

                foreach (GeometryObject gObj in geomElement)
                {
                    if (gObj is Solid solid)
                        CollectFaceLoopEdges(solid, doc, references);
                }
            }
            catch
            {
                // Fallback: if face-edge collection fails, the sketch/GeometryHelpers
                // collection will provide coverage. Don't propagate.
            }
        }

        private static void CollectFaceLoopEdges(
            Solid solid,
            Document doc,
            IDictionary<string, EdgeReferenceInfo> references)
        {
            if (solid == null || solid.Faces.IsEmpty) return;

            foreach (Face face in solid.Faces)
            {
                XYZ normal;
                try { normal = face.ComputeNormal(new UV(0.5, 0.5)); }
                catch { continue; }

                if (Math.Abs(normal.Z) < 0.9) continue;

                try
                {
                    foreach (EdgeArray loop in face.EdgeLoops)
                    {
                        foreach (Edge edge in loop)
                        {
                            if (edge.Reference == null) continue;
                            if (edge.AsCurveFollowingFace(face) is Line line)
                                AddReferenceFromLine(line, edge.Reference, doc, references);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private static void CollectDependentCurveReferences(
            Element ceiling,
            Document doc,
            IDictionary<string, EdgeReferenceInfo> references)
        {
            var curveIds = ceiling.GetDependentElements(new ElementClassFilter(typeof(CurveElement)));
            foreach (var curveId in curveIds)
            {
                if (!(doc.GetElement(curveId) is CurveElement curveElement))
                    continue;

                if (!(curveElement.GeometryCurve is Line line))
                    continue;

                Reference reference = curveElement.GeometryCurve.Reference;
                if (reference == null)
                    continue;

                AddReferenceFromLine(line, reference, doc, references);
            }

            var sketchIds = ceiling.GetDependentElements(new ElementClassFilter(typeof(Sketch)));
            foreach (var sketchId in sketchIds)
            {
                if (!(doc.GetElement(sketchId) is Sketch sketch))
                    continue;

                foreach (CurveArray curveArray in sketch.Profile)
                {
                    foreach (Curve curve in curveArray)
                    {
                        if (!(curve is Line line))
                            continue;

                        if (curve.Reference == null)
                            continue;

                        AddReferenceFromLine(line, curve.Reference, doc, references);
                    }
                }
            }
        }

        private static void AddReferenceFromLine(
            Line line,
            Reference reference,
            Document doc,
            IDictionary<string, EdgeReferenceInfo> references)
        {
            var orientation = GeometryHelpers.GetElementOrientation(line);
            if (orientation != PluginCotasExteriores.Body.Enumerators.ElementOrientation.Horizontal &&
                orientation != PluginCotasExteriores.Body.Enumerators.ElementOrientation.Vertical)
                return;

            string stableId;
            try
            {
                stableId = reference.ConvertToStableRepresentation(doc);
            }
            catch
            {
                stableId = $"{line.GetEndPoint(0).X:F6}|{line.GetEndPoint(0).Y:F6}|{line.GetEndPoint(1).X:F6}|{line.GetEndPoint(1).Y:F6}|{orientation}";
            }

            if (references.ContainsKey(stableId))
                return;

            references[stableId] = new EdgeReferenceInfo(
                reference,
                stableId,
                orientation == PluginCotasExteriores.Body.Enumerators.ElementOrientation.Horizontal
                    ? EdgeOrientation.Horizontal
                    : EdgeOrientation.Vertical,
                new[] { line.GetEndPoint(0), line.GetEndPoint(1) });
        }
    }

    // =========================================================
    // FILTRO DE SELECCIÓN (AGREGADO CORRECTAMENTE)
    // =========================================================
    class CeilingSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem.Category != null &&
                   elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Ceilings;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    internal enum EdgeOrientation
    {
        Horizontal,
        Vertical
    }

    internal sealed class EdgeReferenceInfo
    {
        public EdgeReferenceInfo(
            Reference reference,
            string stableId,
            EdgeOrientation orientation,
            IReadOnlyList<XYZ> endPoints)
        {
            Reference = reference;
            StableId = stableId;
            Orientation = orientation;
            EndPoints = endPoints;
        }

        public Reference Reference { get; }
        public string StableId { get; }
        public EdgeOrientation Orientation { get; }
        public IReadOnlyList<XYZ> EndPoints { get; }
        public double FixedCoordinate => Orientation == EdgeOrientation.Vertical ? EndPoints[0].X : EndPoints[0].Y;
        public double MinAlong => Orientation == EdgeOrientation.Vertical
            ? Math.Min(EndPoints[0].Y, EndPoints[1].Y)
            : Math.Min(EndPoints[0].X, EndPoints[1].X);
        public double MaxAlong => Orientation == EdgeOrientation.Vertical
            ? Math.Max(EndPoints[0].Y, EndPoints[1].Y)
            : Math.Max(EndPoints[0].X, EndPoints[1].X);
    }

    internal sealed class BoundaryReference
    {
        private readonly List<(double Min, double Max, Reference Ref)> _segments;

        public BoundaryReference(
            EdgeOrientation orientation,
            double fixedCoordinate,
            double min,
            double max,
            Reference reference)
        {
            Orientation = orientation;
            FixedCoordinate = fixedCoordinate;
            Min = min;
            Max = max;
            _segments = new List<(double, double, Reference)> { (min, max, reference) };
        }

        public EdgeOrientation Orientation { get; }
        public double FixedCoordinate { get; }
        public double Min { get; private set; }
        public double Max { get; private set; }

        // Backward-compat: returns first segment's reference.
        public Reference Reference => _segments[0].Ref;

        // Called by MergeBoundaries when a contiguous segment is appended.
        public void ExtendWith(double min, double max, Reference reference)
        {
            _segments.Add((min, max, reference));
            if (min < Min) Min = min;
            if (max > Max) Max = max;
        }

        // Returns the reference whose sub-segment actually spans the probe position,
        // so NewDimension gets a reference that intersects the dimension line.
        public Reference GetReferenceAt(double probe)
        {
            const double tol = 1e-6;
            foreach (var seg in _segments)
                if (seg.Min <= probe + tol && seg.Max >= probe - tol)
                    return seg.Ref;
            return _segments[0].Ref;
        }
    }
}

