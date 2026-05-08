namespace PluginCotasExteriores.Work
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using Body;
    using Body.AdvancedClasses;
    using Body.Enumerators;
    using Body.SelectionFilters;
    using Configurations;
    using Helpers;

    public class InsertExteriorDimensions
    {
        private const string TransactionName = "Cotas";
        private readonly ExteriorConfiguration _exteriorConfiguration;
        private readonly UIApplication _uiApplication;
        private readonly List<AdvancedWall> _advancedWalls;
        private readonly List<AdvancedGrid> _advancedGrids;
        private double _cutPlanZ;

        public InsertExteriorDimensions(ExteriorConfiguration configuration, UIApplication uiApplication)
        {
            _exteriorConfiguration = configuration;
            _uiApplication = uiApplication;
            _advancedGrids = new List<AdvancedGrid>();
            _advancedWalls = new List<AdvancedWall>();
        }

        /// <summary>Place exterior dimensions</summary>
        public void DoWork()
        {
            var doc = _uiApplication.ActiveUIDocument.Document;
            _cutPlanZ = GeometryHelpers.GetViewPlanCutPlaneElevation((ViewPlan)doc.ActiveView);

            // select
            var selectedElements = SelectElements();
            if (selectedElements == null)
                return;

            // get list of advanced elements
            foreach (var element in selectedElements)
            {
                switch (element)
                {
                    case Wall wall:
                        var advancedWall = new AdvancedWall(wall);
                        if (advancedWall.IsDefined)
                            _advancedWalls.Add(advancedWall);
                        break;
                    case Grid grid:
                        var advancedGrid = new AdvancedGrid(grid);
                        if (advancedGrid.IsDefined)
                            _advancedGrids.Add(advancedGrid);
                        break;
                }
            }

            if (!_advancedWalls.Any())
            {
                TaskDialog.Show("Cotas", "No se encontraron muros válidos en la selección.");
                return;
            }

            // Filter walls by width
            AdvancedHelpers.FilterByWallWidth(_advancedWalls);
            if (!_advancedWalls.Any())
            {
                TaskDialog.Show("Cotas", "No quedan muros después del filtro por espesor mínimo.");
                return;
            }

            // Filter walls by cut plane
            AdvancedHelpers.FilterByCutPlan(_advancedWalls, _uiApplication.ActiveUIDocument.Document);

            if (!_advancedWalls.Any())
            {
                TaskDialog.Show("Cotas", "No quedan muros después del filtro por plano de corte.");
                return;
            }

            var selectedWalls = _advancedWalls.ToList();

            using (var transactionGroup = new TransactionGroup(doc, TransactionName))
            {
                transactionGroup.Start();

                // create dimensions
                if (_exteriorConfiguration.RightDimensions)
                    CreateSideDimensions(selectedWalls, _advancedWalls, ExtremeWallVariant.Right);
                if (_exteriorConfiguration.LeftDimensions)
                    CreateSideDimensions(selectedWalls, _advancedWalls, ExtremeWallVariant.Left);
                if (_exteriorConfiguration.TopDimensions)
                    CreateSideDimensions(selectedWalls, _advancedWalls, ExtremeWallVariant.Top);
                if (_exteriorConfiguration.BottomDimensions)
                    CreateSideDimensions(selectedWalls, _advancedWalls, ExtremeWallVariant.Bottom);

                transactionGroup.Assimilate();
            }
        }

        /// <summary>User element selection (walls and grids)</summary>
        private IList<Element> SelectElements()
        {
            try
            {
                var selection = _uiApplication.ActiveUIDocument.Selection;
                while (true)
                {
                    var result = selection.PickElementsByRectangle(new WallAndGridsFilter(), "Seleccione muros y ejes con un rectángulo");
                    if (result.Count <= 1)
                        TaskDialog.Show("Cotas", "Seleccione más de un elemento.");
                    else
                        return result;
                }
            }
            catch
            {
                return null;
            }
        }

        private List<Dimension> CreateSideDimensions(
            List<AdvancedWall> sideAdvancedWalls, List<AdvancedWall> allWalls, ExtremeWallVariant extremeWallVariant)
        {
            var createdDimensions = new List<Dimension>();
            var doc = _uiApplication.ActiveUIDocument.Document;

            var chainOffsetSumm = 0;

            foreach (var chain in _exteriorConfiguration.Chains)
            {
                chainOffsetSumm += chain.ElementOffset;

                var chainDimensionLine = AdvancedHelpers.GetDimensionLineForChain(
                    doc, sideAdvancedWalls, extremeWallVariant,
                    chainOffsetSumm.MmToFt() * doc.ActiveView.Scale);

                if (chainDimensionLine == null)
                {
                    TaskDialog.Show("Cotas", "No se pudo crear la línea de acotación para una cadena.");
                    continue;
                }

                // Extreme grids
                if (chain.ExtremeGrids)
                {
                    createdDimensions.Add(CreateDimensionByExtremeGrids(doc, chainDimensionLine, extremeWallVariant));
                }
                else if (chain.Overall)
                {
                    createdDimensions.Add(CreateDimensionByOverallWalls(doc, chainDimensionLine, sideAdvancedWalls, extremeWallVariant));
                }
                else
                {
                    var referenceArray = new ReferenceArray();

                    if (chain.Walls)
                        GetWallsReferences(sideAdvancedWalls, allWalls, extremeWallVariant, chain, ref referenceArray);

                    if (chain.Grids)
                        GetGridsReferences(extremeWallVariant, ref referenceArray);

                    if (!referenceArray.IsEmpty)
                    {
                        using (var transaction = new Transaction(doc, TransactionName))
                        {
                            transaction.Start();
                            var dimension = doc.Create.NewDimension(doc.ActiveView, chainDimensionLine, referenceArray);
                            if (dimension != null)
                                createdDimensions.Add(dimension);
                            transaction.Commit();
                        }
                    }
                }
            }

            return createdDimensions;
        }

        /// <summary>Create dimensions for extreme grids</summary>
        private Dimension CreateDimensionByExtremeGrids(Document doc, Line chainDimensionLine, ExtremeWallVariant extremeWallVariant)
        {
            if (!_advancedGrids.Any())
                return null;
            Dimension returnedDimension = null;
            var referenceArray = new ReferenceArray();
            var opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                View = _uiApplication.ActiveUIDocument.Document.ActiveView
            };

            if (extremeWallVariant == ExtremeWallVariant.Left || extremeWallVariant == ExtremeWallVariant.Right)
            {
                var verticalGrids = _advancedGrids.Where(g => g.Orientation == ElementOrientation.Horizontal).ToList();
                verticalGrids.Sort((g1, g2) => g1.StartPoint.Y.CompareTo(g2.StartPoint.Y));
                var grids = new List<AdvancedGrid> { verticalGrids.First(), verticalGrids.Last() };
                foreach (var grid in grids)
                {
                    foreach (var o in grid.Grid.get_Geometry(opt))
                    {
                        var line = o as Line;
                        if (line != null)
                            referenceArray.Append(line.Reference);
                    }
                }
            }
            else
            {
                var horizontalGrids = _advancedGrids.Where(g => g.Orientation == ElementOrientation.Vertical).ToList();
                horizontalGrids.Sort((g1, g2) => g1.StartPoint.X.CompareTo(g2.StartPoint.X));
                var grids = new List<AdvancedGrid> { horizontalGrids.First(), horizontalGrids.Last() };
                foreach (var grid in grids)
                {
                    foreach (var o in grid.Grid.get_Geometry(opt))
                    {
                        var line = o as Line;
                        if (line != null)
                            referenceArray.Append(line.Reference);
                    }
                }
            }

            if (!referenceArray.IsEmpty)
            {
                using (var transaction = new Transaction(doc, TransactionName))
                {
                    transaction.Start();
                    returnedDimension = doc.Create.NewDimension(doc.ActiveView, chainDimensionLine, referenceArray);
                    transaction.Commit();
                }
            }

            return returnedDimension;
        }

        private void GetGridsReferences(ExtremeWallVariant extremeWallVariant, ref ReferenceArray referenceArray)
        {
            var verticalGrids = _advancedGrids.Where(g => g.Orientation == ElementOrientation.Vertical).ToList();
            var horizontalGrids = _advancedGrids.Where(g => g.Orientation == ElementOrientation.Horizontal).ToList();
            var opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                View = _uiApplication.ActiveUIDocument.Document.ActiveView
            };

            if (extremeWallVariant == ExtremeWallVariant.Right || extremeWallVariant == ExtremeWallVariant.Left)
            {
                foreach (var grid in horizontalGrids)
                {
                    foreach (var o in grid.Grid.get_Geometry(opt))
                    {
                        var line = o as Line;
                        if (line != null)
                            referenceArray.Append(line.Reference);
                    }
                }
            }

            if (extremeWallVariant == ExtremeWallVariant.Bottom || extremeWallVariant == ExtremeWallVariant.Top)
            {
                foreach (var grid in verticalGrids)
                {
                    foreach (var o in grid.Grid.get_Geometry(opt))
                    {
                        var line = o as Line;
                        if (line != null)
                            referenceArray.Append(line.Reference);
                    }
                }
            }
        }

        /// <summary>Get wall references based on chain settings</summary>
        private void GetWallsReferences(
            List<AdvancedWall> sideWalls,
            List<AdvancedWall> allWalls,
            ExtremeWallVariant extremeWallVariant,
            ExteriorDimensionChain chain, ref ReferenceArray referenceArray)
        {
            var faces = new List<AdvancedPlanarFace>();
            var verticalWalls = sideWalls.Where(w => w.Orientation == ElementOrientation.Vertical).ToList();
            var horizontalWalls = sideWalls.Where(w => w.Orientation == ElementOrientation.Horizontal).ToList();

            if (!verticalWalls.Any() && !horizontalWalls.Any())
                return;

            if (!chain.IntersectingWalls)
            {
                if (extremeWallVariant == ExtremeWallVariant.Right || extremeWallVariant == ExtremeWallVariant.Left)
                {
                    foreach (var wall in sideWalls)
                    {
                        var horizontalTempFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsHorizontal)
                                horizontalTempFaces.Add(face);
                        }

                        horizontalTempFaces.Sort((f1, f2) => f1.PlanarFace.Origin.Y.CompareTo(f2.PlanarFace.Origin.Y));
                        faces.Add(horizontalTempFaces.First());
                        faces.Add(horizontalTempFaces.Last());
                    }
                }

                if (extremeWallVariant == ExtremeWallVariant.Top || extremeWallVariant == ExtremeWallVariant.Bottom)
                {
                    foreach (var wall in sideWalls)
                    {
                        var verticalTempFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsVertical)
                                verticalTempFaces.Add(face);
                        }

                        verticalTempFaces.Sort((f1, f2) => f1.PlanarFace.Origin.X.CompareTo(f2.PlanarFace.Origin.X));
                        faces.Add(verticalTempFaces.First());
                        faces.Add(verticalTempFaces.Last());
                    }
                }
            }
            else
            {
                if (extremeWallVariant == ExtremeWallVariant.Right || extremeWallVariant == ExtremeWallVariant.Left)
                {
                    foreach (var wall in horizontalWalls)
                    {
                        var wallNeededFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsHorizontal)
                                wallNeededFaces.Add(face);
                        }

                        wallNeededFaces.Sort((f1, f2) => f1.PlanarFace.Origin.Y.CompareTo(f2.PlanarFace.Origin.Y));
                        faces.Add(wallNeededFaces.First());
                        faces.Add(wallNeededFaces.Last());
                    }

                    var iw = FindIntersectionWalls(sideWalls, allWalls)
                        .Where(w => w.Orientation == ElementOrientation.Horizontal).ToList();
                    foreach (var wall in iw)
                    {
                        var wallNeededFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsHorizontal)
                                wallNeededFaces.Add(face);
                        }

                        wallNeededFaces.Sort((f1, f2) => f1.PlanarFace.Origin.Y.CompareTo(f2.PlanarFace.Origin.Y));
                        faces.Add(wallNeededFaces.First());
                        faces.Add(wallNeededFaces.Last());
                    }
                }

                if (extremeWallVariant == ExtremeWallVariant.Bottom || extremeWallVariant == ExtremeWallVariant.Top)
                {
                    foreach (var wall in verticalWalls)
                    {
                        var wallNeededFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsVertical)
                                wallNeededFaces.Add(face);
                        }

                        wallNeededFaces.Sort((f1, f2) => f1.PlanarFace.Origin.X.CompareTo(f2.PlanarFace.Origin.X));
                        faces.Add(wallNeededFaces.First());
                        faces.Add(wallNeededFaces.Last());
                    }

                    var iw = FindIntersectionWalls(sideWalls, allWalls)
                        .Where(w => w.Orientation == ElementOrientation.Vertical).ToList();

                    foreach (var wall in iw)
                    {
                        var wallNeededFaces = new List<AdvancedPlanarFace>();
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsVertical)
                                wallNeededFaces.Add(face);
                        }

                        wallNeededFaces.Sort((f1, f2) => f1.PlanarFace.Origin.Y.CompareTo(f2.PlanarFace.Origin.Y));
                        faces.Add(wallNeededFaces.First());
                        faces.Add(wallNeededFaces.Last());
                    }
                }
            }

            // Openings
            if (chain.Openings)
            {
                if (extremeWallVariant == ExtremeWallVariant.Right || extremeWallVariant == ExtremeWallVariant.Left)
                {
                    foreach (var wall in verticalWalls)
                    {
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsHorizontal)
                                faces.Add(face);
                        }
                    }
                }

                if (extremeWallVariant == ExtremeWallVariant.Bottom || extremeWallVariant == ExtremeWallVariant.Top)
                {
                    foreach (var wall in horizontalWalls)
                    {
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsVertical)
                                faces.Add(face);
                        }
                    }
                }
            }

            // filtered
            var filteredFaces = FilterFaces(extremeWallVariant, sideWalls, faces);
            foreach (var face in filteredFaces)
            {
                referenceArray.Append(face.PlanarFace.Reference);
            }
        }

        private Dimension CreateDimensionByOverallWalls(
            Document doc, Line chainDimensionLine,
            IReadOnlyCollection<AdvancedWall> sideWalls,
            ExtremeWallVariant extremeWallVariant)
        {
            Dimension returnedDimension = null;
            var verticalWalls = sideWalls.Where(w => w.Orientation == ElementOrientation.Vertical).ToList();
            var horizontalWalls = sideWalls.Where(w => w.Orientation == ElementOrientation.Horizontal).ToList();

            if (!verticalWalls.Any() && !horizontalWalls.Any())
                return null;
            var referenceArray = new ReferenceArray();

            if (extremeWallVariant == ExtremeWallVariant.Right || extremeWallVariant == ExtremeWallVariant.Left)
            {
                var faces = new List<AdvancedPlanarFace>();

                foreach (var verticalWall in verticalWalls)
                {
                    foreach (var face in verticalWall.AdvancedPlanarFaces)
                    {
                        if (face.IsHorizontal)
                            faces.Add(face);
                    }

                    var adjoinElements1 = ((LocationCurve)verticalWall.Wall.Location).get_ElementsAtJoin(0);
                    var adjoinElements2 = ((LocationCurve)verticalWall.Wall.Location).get_ElementsAtJoin(1);
                    var adjoinWalls = new List<AdvancedWall>();
                    foreach (Element element in adjoinElements1)
                    {
                        var w = AdvancedHelpers.GetAdvancedWallFromListById(horizontalWalls, element.Id.GetValueCompat());
                        if (w != null)
                            adjoinWalls.Add(w);
                    }

                    foreach (Element element in adjoinElements2)
                    {
                        var w = AdvancedHelpers.GetAdvancedWallFromListById(horizontalWalls, element.Id.GetValueCompat());
                        if (w != null)
                            adjoinWalls.Add(w);
                    }

                    foreach (var wall in adjoinWalls)
                    {
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsHorizontal)
                                faces.Add(face);
                        }
                    }
                }

                faces.Sort((f1, f2) => f1.MinY.CompareTo(f2.MinY));
                referenceArray.Append(faces.First().PlanarFace.Reference);
                referenceArray.Append(faces.Last().PlanarFace.Reference);
            }

            if (extremeWallVariant == ExtremeWallVariant.Top || extremeWallVariant == ExtremeWallVariant.Bottom)
            {
                var faces = new List<AdvancedPlanarFace>();

                foreach (var horizontalWall in horizontalWalls)
                {
                    foreach (var face in horizontalWall.AdvancedPlanarFaces)
                    {
                        if (face.IsVertical)
                            faces.Add(face);
                    }

                    var adjoinElements1 = ((LocationCurve)horizontalWall.Wall.Location).get_ElementsAtJoin(0);
                    var adjoinElements2 = ((LocationCurve)horizontalWall.Wall.Location).get_ElementsAtJoin(1);
                    var adjoinWalls = new List<AdvancedWall>();
                    foreach (Element element in adjoinElements1)
                    {
                        var w = AdvancedHelpers.GetAdvancedWallFromListById(verticalWalls, element.Id.GetValueCompat());
                        if (w != null)
                            adjoinWalls.Add(w);
                    }

                    foreach (Element element in adjoinElements2)
                    {
                        var w = AdvancedHelpers.GetAdvancedWallFromListById(verticalWalls, element.Id.GetValueCompat());
                        if (w != null)
                            adjoinWalls.Add(w);
                    }

                    foreach (var wall in adjoinWalls)
                    {
                        foreach (var face in wall.AdvancedPlanarFaces)
                        {
                            if (face.IsVertical)
                                faces.Add(face);
                        }
                    }
                }

                faces.Sort((f1, f2) => f1.MinX.CompareTo(f2.MinX));
                referenceArray.Append(faces.First().PlanarFace.Reference);
                referenceArray.Append(faces.Last().PlanarFace.Reference);
            }

            if (!referenceArray.IsEmpty)
            {
                using (var transaction = new Transaction(doc, TransactionName))
                {
                    transaction.Start();
                    returnedDimension = doc.Create.NewDimension(doc.ActiveView, chainDimensionLine, referenceArray);
                    transaction.Commit();
                }
            }

            return returnedDimension;
        }

        /// <summary>Filter faces by conditions</summary>
        private IEnumerable<AdvancedPlanarFace> FilterFaces(
            ExtremeWallVariant extremeWallVariant,
            List<AdvancedWall> sideWalls,
            IEnumerable<AdvancedPlanarFace> selectedFaces)
        {
            var tolerance = 0.0001;
            var faces = new List<AdvancedPlanarFace>();

            // Remove faces not intersected by cut plane
            foreach (var face in selectedFaces)
            {
                if (face.MinZ <= _cutPlanZ && face.MaxZ >= _cutPlanZ)
                    faces.Add(face);
            }

            // Remove faces that coincide by direction
            var returnedFaces = new List<AdvancedPlanarFace>();

            bool hasFaces;
            do
            {
                hasFaces = faces.Any(f => f != null);
                for (var i = 0; i < faces.Count; i++)
                {
                    var face = faces[i];
                    if (face != null)
                    {
                        returnedFaces.Add(face);
                        for (var j = 0; j < faces.Count; j++)
                        {
                            if (i == j) continue;
                            if (faces[j] == null) continue;

                            if (extremeWallVariant == ExtremeWallVariant.Left ||
                                extremeWallVariant == ExtremeWallVariant.Right)
                            {
                                if (System.Math.Abs(face.PlanarFace.Origin.Y - faces[j].PlanarFace.Origin.Y) < tolerance)
                                    faces[j] = null;
                            }
                            else if (extremeWallVariant == ExtremeWallVariant.Top ||
                                     extremeWallVariant == ExtremeWallVariant.Bottom)
                            {
                                if (System.Math.Abs(face.PlanarFace.Origin.X - faces[j].PlanarFace.Origin.X) < tolerance)
                                    faces[j] = null;
                            }
                        }

                        faces[i] = null;
                    }
                }
            }
            while (hasFaces);

            // Depth filtering
            var depth = AdvancedHelpers.GetMaxWallWidthFromList(sideWalls) * 2;

            if (extremeWallVariant == ExtremeWallVariant.Bottom)
            {
                foreach (var wall in sideWalls.Where(w => w.Orientation == ElementOrientation.Horizontal))
                {
                    for (var i = returnedFaces.Count - 1; i >= 0; i--)
                    {
                        var face = returnedFaces[i];
                        if (face.MinX > wall.GetMinX() - wall.Wall.Width &&
                            face.MinX < wall.GetMaxX() + wall.Wall.Width)
                        {
                            if (face.MinY > wall.GetMinY() + depth)
                                returnedFaces.RemoveAt(i);
                        }
                    }
                }
            }

            if (extremeWallVariant == ExtremeWallVariant.Top)
            {
                foreach (var wall in sideWalls.Where(w => w.Orientation == ElementOrientation.Horizontal))
                {
                    for (var i = returnedFaces.Count - 1; i >= 0; i--)
                    {
                        var face = returnedFaces[i];
                        if (face.MinX > wall.GetMinX() - wall.Wall.Width &&
                            face.MinX < wall.GetMaxX() + wall.Wall.Width)
                        {
                            if (face.MaxY < wall.GetMaxY() - depth)
                                returnedFaces.RemoveAt(i);
                        }
                    }
                }
            }

            if (extremeWallVariant == ExtremeWallVariant.Left)
            {
                foreach (var wall in sideWalls.Where(w => w.Orientation == ElementOrientation.Vertical))
                {
                    for (var i = returnedFaces.Count - 1; i >= 0; i--)
                    {
                        var face = returnedFaces[i];
                        if (face.MinY > wall.GetMinY() - wall.Wall.Width &&
                            face.MinY < wall.GetMaxY() + wall.Wall.Width)
                        {
                            if (face.MinX > wall.GetMinX() + depth)
                                returnedFaces.RemoveAt(i);
                        }
                    }
                }
            }

            if (extremeWallVariant == ExtremeWallVariant.Right)
            {
                foreach (var wall in sideWalls.Where(w => w.Orientation == ElementOrientation.Vertical))
                {
                    for (var i = returnedFaces.Count - 1; i >= 0; i--)
                    {
                        var face = returnedFaces[i];
                        if (face.MinY > wall.GetMinY() - wall.Wall.Width &&
                            face.MinY < wall.GetMaxY() + wall.Wall.Width)
                        {
                            if (face.MaxX < wall.GetMaxX() - depth)
                                returnedFaces.RemoveAt(i);
                        }
                    }
                }
            }

            // Filter nearby faces
            var minWidthStr = UserSettings.GetValue("ExteriorFaceMinWidthBetween");
            var minWidthSetting = int.TryParse(minWidthStr, out var m) ? m : 100;
            var minWidthBetween = minWidthSetting.MmToFt();

            var removeVariantStr = UserSettings.GetValue("ExteriorMinWidthFaceRemove");
            var removeVariant = int.TryParse(removeVariantStr, out m) ? m : 0;

            if (extremeWallVariant == ExtremeWallVariant.Bottom || extremeWallVariant == ExtremeWallVariant.Top)
            {
                returnedFaces.Sort((f1, f2) => f1.MinX.CompareTo(f2.MinX));
                var wasRemoved = false;
                do
                {
                    for (var i = 0; i < returnedFaces.Count - 1; i++)
                    {
                        var face1 = returnedFaces[i];
                        var face2 = returnedFaces[i + 1];
                        var distance = System.Math.Abs(face1.MinX - face2.MinX);
                        if (distance < minWidthBetween)
                        {
                            wasRemoved = true;
                            var face1Length = System.Math.Abs(face1.MaxY - face1.MinY);
                            var face2Length = System.Math.Abs(face2.MaxY - face2.MinY);
                            if (removeVariant == 0)
                            {
                                if (face1Length < face2Length)
                                    returnedFaces.RemoveAt(i);
                                else
                                    returnedFaces.RemoveAt(i + 1);
                            }
                            else
                            {
                                if (face1Length > face2Length)
                                    returnedFaces.RemoveAt(i);
                                else
                                    returnedFaces.RemoveAt(i + 1);
                            }

                            break;
                        }

                        wasRemoved = false;
                    }
                }
                while (wasRemoved);
            }

            if (extremeWallVariant == ExtremeWallVariant.Left || extremeWallVariant == ExtremeWallVariant.Right)
            {
                returnedFaces.Sort((f1, f2) => f1.MinY.CompareTo(f2.MinY));
                var wasRemoved = false;
                do
                {
                    for (var i = 0; i < returnedFaces.Count - 1; i++)
                    {
                        var face1 = returnedFaces[i];
                        var face2 = returnedFaces[i + 1];
                        var distance = System.Math.Abs(face1.MinY - face2.MinY);
                        if (distance < minWidthBetween)
                        {
                            wasRemoved = true;
                            var face1Length = System.Math.Abs(face1.MaxX - face1.MinX);
                            var face2Length = System.Math.Abs(face2.MaxX - face2.MinX);
                            if (removeVariant == 0)
                            {
                                if (face1Length < face2Length)
                                    returnedFaces.RemoveAt(i);
                                else
                                    returnedFaces.RemoveAt(i + 1);
                            }
                            else
                            {
                                if (face1Length > face2Length)
                                    returnedFaces.RemoveAt(i);
                                else
                                    returnedFaces.RemoveAt(i + 1);
                            }

                            break;
                        }

                        wasRemoved = false;
                    }
                }
                while (wasRemoved);
            }

            return returnedFaces;
        }

        private IEnumerable<AdvancedWall> FindIntersectionWalls(List<AdvancedWall> sideWalls, List<AdvancedWall> allWalls)
        {
            var intersectionWalls = new List<AdvancedWall>();

            foreach (var wall in allWalls)
            {
                foreach (var sideWall in sideWalls)
                {
                    if (wall.IsAdjoinToByLocationCurveEnds(sideWall) &&
                        !AdvancedHelpers.HasWallInListById(sideWalls, wall))
                    {
                        intersectionWalls.Add(wall);
                    }
                }
            }

            return intersectionWalls;
        }
    }
}
