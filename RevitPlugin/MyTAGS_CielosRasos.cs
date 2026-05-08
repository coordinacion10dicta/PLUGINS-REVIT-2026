
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
                var selectedEdges = selectedCeilings
                    .SelectMany(ceiling => GetPlanEdgeReferences(ceiling, view, doc))
                    .GroupBy(edge => edge.StableId)
                    .Select(group => group.First())
                    .ToList();

                var groups = BuildConnectedGroups(selectedEdges);
                foreach (var group in groups)
                {
                    var verticalBoundaries = MergeBoundaries(
                        group.Where(edge => edge.Orientation == EdgeOrientation.Vertical),
                        EdgeOrientation.Vertical);
                    var horizontalBoundaries = MergeBoundaries(
                        group.Where(edge => edge.Orientation == EdgeOrientation.Horizontal),
                        EdgeOrientation.Horizontal);

                    if (verticalBoundaries.Count < 2 || horizontalBoundaries.Count < 2)
                        continue;

                    var verticalDimensions = BuildDimensionsForBands(
                        doc,
                        view,
                        verticalBoundaries,
                        horizontalBoundaries,
                        EdgeOrientation.Horizontal);

                    var horizontalDimensions = BuildDimensionsForBands(
                        doc,
                        view,
                        horizontalBoundaries,
                        verticalBoundaries,
                        EdgeOrientation.Vertical);

                    acotados += verticalDimensions;
                    acotados += horizontalDimensions;
                }

                tx.Commit();

                if (acotados == 0)
                {
                    TaskDialog.Show(
                        "Cielos Rasos",
                        "No se pudieron crear cotas internas válidas para los grupos seleccionados.");
                }
                else
                {
                    TaskDialog.Show("Resultado", $"Cotas creadas: {acotados}");
                }
            }
        }

        private int BuildDimensionsForBands(
            Document doc,
            View view,
            List<BoundaryReference> primaryBoundaries,
            List<BoundaryReference> secondaryBoundaries,
            EdgeOrientation dimensionOrientation)
        {
            int created = 0;
            double z = GetDimensionZ(view);
            var orderedCuts = secondaryBoundaries
                .Select(boundary => boundary.FixedCoordinate)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            if (orderedCuts.Count < 2)
                return 0;

            for (int i = 0; i < orderedCuts.Count - 1; i++)
            {
                double start = orderedCuts[i];
                double end = orderedCuts[i + 1];
                if (end - start < GeometryTolerance)
                    continue;

                double probe = (start + end) * 0.5;
                var activeBoundaries = primaryBoundaries
                    .Where(boundary => boundary.Min <= probe + GeometryTolerance &&
                                       boundary.Max >= probe - GeometryTolerance)
                    .OrderBy(boundary => boundary.FixedCoordinate)
                    .ToList();

                if (activeBoundaries.Count < 2)
                    continue;

                for (int j = 0; j < activeBoundaries.Count - 1; j++)
                {
                    var first = activeBoundaries[j];
                    var second = activeBoundaries[j + 1];
                    if (second.FixedCoordinate - first.FixedCoordinate < GeometryTolerance)
                        continue;

                    var referenceArray = new ReferenceArray();
                    referenceArray.Append(first.Reference);
                    referenceArray.Append(second.Reference);

                    Line dimLine;
                    if (dimensionOrientation == EdgeOrientation.Horizontal)
                    {
                        dimLine = Line.CreateBound(
                            new XYZ(first.FixedCoordinate, probe, z),
                            new XYZ(second.FixedCoordinate, probe, z));
                    }
                    else
                    {
                        dimLine = Line.CreateBound(
                            new XYZ(probe, first.FixedCoordinate, z),
                            new XYZ(probe, second.FixedCoordinate, z));
                    }

                    try
                    {
                        if (doc.Create.NewDimension(view, dimLine, referenceArray) != null)
                            created++;
                    }
                    catch
                    {
                        // Keep processing other dimension pairs even if one is not valid in Revit.
                    }
                }
            }

            return created;
        }

        private static List<List<EdgeReferenceInfo>> BuildConnectedGroups(List<EdgeReferenceInfo> edges)
        {
            var groups = new List<List<EdgeReferenceInfo>>();
            var visited = new HashSet<string>();

            foreach (var edge in edges)
            {
                if (visited.Contains(edge.StableId))
                    continue;

                var stack = new Stack<EdgeReferenceInfo>();
                var group = new List<EdgeReferenceInfo>();
                stack.Push(edge);
                visited.Add(edge.StableId);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    group.Add(current);

                    foreach (var candidate in edges)
                    {
                        if (visited.Contains(candidate.StableId))
                            continue;

                        if (!AreConnected(current, candidate))
                            continue;

                        visited.Add(candidate.StableId);
                        stack.Push(candidate);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private static bool AreConnected(EdgeReferenceInfo first, EdgeReferenceInfo second)
        {
            return first.EndPoints.Any(p1 => second.EndPoints.Any(p2 => p1.DistanceTo(p2) < 0.02));
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
                var ordered = group
                    .OrderBy(edge => edge.MinAlong)
                    .ToList();

                if (!ordered.Any())
                    continue;

                double currentMin = ordered[0].MinAlong;
                double currentMax = ordered[0].MaxAlong;
                Reference currentReference = ordered[0].Reference;
                double fixedCoordinate = ordered[0].FixedCoordinate;

                foreach (var edge in ordered.Skip(1))
                {
                    if (edge.MinAlong <= currentMax + 0.02)
                    {
                        currentMax = Math.Max(currentMax, edge.MaxAlong);
                        continue;
                    }

                    merged.Add(new BoundaryReference(orientation, fixedCoordinate, currentMin, currentMax, currentReference));
                    currentMin = edge.MinAlong;
                    currentMax = edge.MaxAlong;
                    currentReference = edge.Reference;
                }

                merged.Add(new BoundaryReference(orientation, fixedCoordinate, currentMin, currentMax, currentReference));
            }

            return merged;
        }

        private List<Element> SelectCeilingsForDimension()
        {
            var selectedCeilings = new Dictionary<int, Element>();

            try
            {
                var preselectedCeilings = _uidoc.Selection
                    .GetElementIds()
                    .Select(id => _uidoc.Document.GetElement(id))
                    .Where(IsCeilingElement)
                    .ToList();

                if (preselectedCeilings.Any())
                    return preselectedCeilings;

                while (true)
                {
                    var pickedRef = _uidoc.Selection.PickObject(
                        ObjectType.PointOnElement,
                        new CeilingSelectionFilter(),
                        "Selecciona cielos rasos uno a uno. Presiona ESC para terminar.");

                    var element = _uidoc.Document.GetElement(pickedRef.ElementId);
                    if (!IsCeilingElement(element))
                        continue;

                    int elementId = element.Id.IntegerValue;
                    if (!selectedCeilings.ContainsKey(elementId))
                    {
                        selectedCeilings[elementId] = element;
                        _uidoc.Selection.SetElementIds(selectedCeilings.Values.Select(e => e.Id).ToList());
                    }
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
            Reference = reference;
        }

        public EdgeOrientation Orientation { get; }
        public double FixedCoordinate { get; }
        public double Min { get; }
        public double Max { get; }
        public Reference Reference { get; }
    }
}

