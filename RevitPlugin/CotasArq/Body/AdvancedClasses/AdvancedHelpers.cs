namespace PluginCotasExteriores.Body.AdvancedClasses
{
    using System.Collections.Generic;
    using System.Linq;
    using Autodesk.Revit.DB;
    using Enumerators;
    using Helpers;

    public static class AdvancedHelpers
    {
        /// <summary>Filter walls by cut plane intersection</summary>
        public static void FilterByCutPlan(List<AdvancedWall> advancedWalls, Document doc)
        {
            var checkedZ = GeometryHelpers.GetViewPlanCutPlaneElevation((ViewPlan)doc.ActiveView);
            for (var i = advancedWalls.Count - 1; i >= 0; i--)
            {
                if (checkedZ < advancedWalls[i].GetMinZ() || checkedZ > advancedWalls[i].GetMaxZ())
                {
                    advancedWalls.RemoveAt(i);
                }
            }
        }
       
        /// <summary>Filter walls by minimum width setting</summary>
        public static void FilterByWallWidth(List<AdvancedWall> advancedWalls)
        {
            var minWallWidthStr = UserSettings.GetValue("MinWallWidth");
            var minWallWidth = int.TryParse(minWallWidthStr, out var m) ? m : 50;
            for (var i = advancedWalls.Count - 1; i >= 0; i--)
            {
                // Skip curtain walls since their width is small but they should be processed
                if (advancedWalls[i].Wall.CurtainGrid != null)
                {
                    continue;
                }

                if (advancedWalls[i].Wall.Width * 304.8 < minWallWidth)
                {
                    advancedWalls.RemoveAt(i);
                }
            }
        }
        
        public static void FindExtremes(
            List<AdvancedWall> walls,
            Document doc,
            out List<AdvancedWall> leftExtreme,
            out List<AdvancedWall> rightExtreme,
            out List<AdvancedWall> topExtreme,
            out List<AdvancedWall> bottomExtreme)
        {
            rightExtreme = new List<AdvancedWall>();
            leftExtreme = new List<AdvancedWall>();
            topExtreme = new List<AdvancedWall>();
            bottomExtreme = new List<AdvancedWall>();

            var outerWalls = GetOuterWalls(walls);
            if (!outerWalls.Any())
            {
                return;
            }

            for (var i = 0; i < outerWalls.Count; i++)
            {
                var checkedWall = outerWalls[i];
                if (checkedWall.Orientation == ElementOrientation.Vertical)
                {
                    var leftPerpendicularLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X - 1000000, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0));
                    var rightPerpendicularLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X + 1000000, checkedWall.MidPoint.Y, 0.0));
                    var hasPerpendicularLeftIntersect = false;
                    var hasPerpendicularRightIntersect = false;
                    for (var j = 0; j < outerWalls.Count; j++)
                    {
                        if (i == j) continue;

                        var checkedCurve = Line.CreateBound(
                            new XYZ(outerWalls[j].StartPoint.X, outerWalls[j].StartPoint.Y, 0.0),
                            new XYZ(outerWalls[j].EndPoint.X, outerWalls[j].EndPoint.Y, 0.0));
                        if (leftPerpendicularLine.IntersectTo(checkedCurve))
                            hasPerpendicularLeftIntersect = true;
                        if (rightPerpendicularLine.IntersectTo(checkedCurve))
                            hasPerpendicularRightIntersect = true;
                    }

                    if (hasPerpendicularRightIntersect && !hasPerpendicularLeftIntersect)
                        leftExtreme.Add(checkedWall);
                    if (hasPerpendicularLeftIntersect && !hasPerpendicularRightIntersect)
                        rightExtreme.Add(checkedWall);
                }

                if (checkedWall.Orientation == ElementOrientation.Horizontal)
                {
                    var topPerpendicularLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y + 1000000, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0));
                    var bottomPerpendicularLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y - 1000000, 0.0));
                    var hasPerpendicularTopIntersect = false;
                    var hasPerpendicularBottomIntersect = false;
                    for (var j = 0; j < outerWalls.Count; j++)
                    {
                        if (i == j) continue;

                        var checkedCurve = Line.CreateBound(
                            new XYZ(outerWalls[j].StartPoint.X, outerWalls[j].StartPoint.Y, 0.0),
                            new XYZ(outerWalls[j].EndPoint.X, outerWalls[j].EndPoint.Y, 0.0));
                        if (topPerpendicularLine.IntersectTo(checkedCurve))
                            hasPerpendicularTopIntersect = true;
                        if (bottomPerpendicularLine.IntersectTo(checkedCurve))
                            hasPerpendicularBottomIntersect = true;
                    }

                    if (hasPerpendicularBottomIntersect && !hasPerpendicularTopIntersect)
                        topExtreme.Add(checkedWall);
                    if (hasPerpendicularTopIntersect && !hasPerpendicularBottomIntersect)
                        bottomExtreme.Add(checkedWall);
                }
            }

            foreach (var checkedAdvancedWall in outerWalls)
            {
                if (checkedAdvancedWall.Orientation == ElementOrientation.Vertical)
                {
                    foreach (var wall in topExtreme)
                    {
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(wall))
                        {
                            topExtreme.Add(checkedAdvancedWall);
                            break;
                        }
                    }

                    foreach (var wall in bottomExtreme)
                    {
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(wall))
                        {
                            bottomExtreme.Add(checkedAdvancedWall);
                            break;
                        }
                    }
                }
                else if (checkedAdvancedWall.Orientation == ElementOrientation.Horizontal)
                {
                    foreach (var wall in leftExtreme)
                    {
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(wall))
                        {
                            leftExtreme.Add(checkedAdvancedWall);
                            break;
                        }
                    }

                    foreach (var wall in rightExtreme)
                    {
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(wall))
                        {
                            rightExtreme.Add(checkedAdvancedWall);
                            break;
                        }
                    }
                }
            }
        }
       
        /// <summary>Find outer walls using perpendicular intersection test</summary>
        public static List<AdvancedWall> GetOuterWalls(IReadOnlyList<AdvancedWall> walls)
        {
            var outerWalls = new List<AdvancedWall>();

            for (var i = 0; i < walls.Count; i++)
            {
                var checkedWall = walls[i];
                if (checkedWall.Orientation == ElementOrientation.Vertical)
                {
                    var leftLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X - 1000000, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0));
                    var rightLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X + 1000000, checkedWall.MidPoint.Y, 0.0));
                    var hasLeftIntersect = false;
                    var hasRightIntersect = false;
                    for (var j = 0; j < walls.Count; j++)
                    {
                        if (i == j) continue;
                        if (walls[j] == null) continue;

                        var checkedCurve = Line.CreateBound(
                            new XYZ(walls[j].StartPoint.X, walls[j].StartPoint.Y, 0.0),
                            new XYZ(walls[j].EndPoint.X, walls[j].EndPoint.Y, 0.0));
                        if (leftLine.IntersectTo(checkedCurve))
                            hasLeftIntersect = true;
                        if (rightLine.IntersectTo(checkedCurve))
                            hasRightIntersect = true;
                    }

                    if (hasLeftIntersect ^ hasRightIntersect)
                        outerWalls.Add(checkedWall);
                }

                if (checkedWall.Orientation == ElementOrientation.Horizontal)
                {
                    var topLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y + 1000000, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0));
                    var bottomLine = Line.CreateBound(
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y, 0.0),
                        new XYZ(checkedWall.MidPoint.X, checkedWall.MidPoint.Y - 1000000, 0.0));
                    var hasTopIntersect = false;
                    var hasBottomIntersect = false;
                    for (var j = 0; j < walls.Count; j++)
                    {
                        if (i == j) continue;

                        var checkedCurve = Line.CreateBound(
                            new XYZ(walls[j].StartPoint.X, walls[j].StartPoint.Y, 0.0),
                            new XYZ(walls[j].EndPoint.X, walls[j].EndPoint.Y, 0.0));
                        if (topLine.IntersectTo(checkedCurve))
                            hasTopIntersect = true;
                        if (bottomLine.IntersectTo(checkedCurve))
                            hasBottomIntersect = true;
                    }

                    if (hasTopIntersect ^ hasBottomIntersect)
                        outerWalls.Add(checkedWall);
                }
            }

            var ids = outerWalls.Select(wall => wall.Wall.Id.GetValueCompat()).ToList();

            var tempWalls = new List<AdvancedWall>();
            foreach (var checkedAdvancedWall in walls)
            {
                if (!ids.Contains(checkedAdvancedWall.Wall.Id.GetValueCompat()))
                {
                    var onFirstEnd = false;
                    var onSecondEnd = false;
                    foreach (var outerWall in outerWalls)
                    {
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(outerWall, 0) &&
                            outerWall.IsAdjoinToByLocationCurveEnds(checkedAdvancedWall))
                            onFirstEnd = true;
                        if (checkedAdvancedWall.IsAdjoinToByLocationCurveEnds(outerWall, 1) &&
                            outerWall.IsAdjoinToByLocationCurveEnds(checkedAdvancedWall))
                            onSecondEnd = true;
                    }

                    if (onFirstEnd && onSecondEnd)
                        tempWalls.Add(checkedAdvancedWall);
                }
            }

            outerWalls.AddRange(tempWalls);
            return outerWalls;
        }

        /// <summary>Check if wall exists in list by Id</summary>
        public static bool HasWallInListById(IEnumerable<AdvancedWall> walls, AdvancedWall advancedWall)
        {
            foreach (var wall in walls)
            {
                if (wall.Wall.Id.GetValueCompat().Equals(advancedWall.Wall.Id.GetValueCompat()))
                    return true;
            }
            return false;
        }
        
        /// <summary>Check if wall is adjoined at location curve ends</summary>
        public static bool IsAdjoinToByLocationCurveEnds(this AdvancedWall wall, AdvancedWall checkedWall)
        {
            var joinIds = new List<long>();
            var elementsAtJoinAtStart = ((LocationCurve)wall.Wall.Location).get_ElementsAtJoin(0);
            var elementsAtJoinAtEnd = ((LocationCurve)wall.Wall.Location).get_ElementsAtJoin(1);
            if (!elementsAtJoinAtEnd.IsEmpty)
            {
                foreach (Element e in elementsAtJoinAtEnd)
                {
                    if (e is Wall && !wall.Wall.Id.GetValueCompat().Equals(e.Id.GetValueCompat()))
                        joinIds.Add(e.Id.GetValueCompat());
                }
            }

            if (!elementsAtJoinAtStart.IsEmpty)
            {
                foreach (Element e in elementsAtJoinAtStart)
                {
                    if (e is Wall && !wall.Wall.Id.GetValueCompat().Equals(e.Id.GetValueCompat()))
                        joinIds.Add(e.Id.GetValueCompat());
                }
            }

            return joinIds.Contains(checkedWall.Wall.Id.GetValueCompat());
        }
        
        internal static bool IsAdjoinToByLocationCurveEnds(this AdvancedWall wall, AdvancedWall checkedWall, int end)
        {
            var joinIds = new List<long>();
            var elementsAtJoin = ((LocationCurve)wall.Wall.Location).get_ElementsAtJoin(end);
            if (!elementsAtJoin.IsEmpty)
            {
                foreach (Element e in elementsAtJoin)
                {
                    if (e is Wall && !wall.Wall.Id.GetValueCompat().Equals(e.Id.GetValueCompat()))
                        joinIds.Add(e.Id.GetValueCompat());
                }
            }

            return joinIds.Contains(checkedWall.Wall.Id.GetValueCompat());
        }

        /// <summary>Get dimension line for chain based on extreme direction</summary>
        public static Line GetDimensionLineForChain(
            Document doc,
            List<AdvancedWall> sideWalls,
            ExtremeWallVariant extremeWallVariant,
            double offset)
        {
            var cutPlanZ = GeometryHelpers.GetViewPlanCutPlaneElevation((ViewPlan)doc.ActiveView);
            var points = new List<XYZ>();
            foreach (var wall in sideWalls)
            {
                points.Add(wall.EndPoint);
                points.Add(wall.StartPoint);
                points.AddRange(wall.GetPointsFromGeometry(doc.ActiveView));
            }

            if (!points.Any())
                return null;

            switch (extremeWallVariant)
            {
                case ExtremeWallVariant.Right:
                    {
                        points.Sort((x, y) => x.X.CompareTo(y.X));
                        var maxX = points.Last().X;
                        points.Sort((x, y) => x.Y.CompareTo(y.Y));
                        var minY = points.First().Y;
                        var maxY = points.Last().Y;
                        return TryCreateBound(
                            new XYZ(maxX + offset, minY, cutPlanZ),
                            new XYZ(maxX + offset, maxY, cutPlanZ));
                    }

                case ExtremeWallVariant.Left:
                    {
                        points.Sort((x, y) => x.X.CompareTo(y.X));
                        var minX = points.First().X;
                        points.Sort((x, y) => x.Y.CompareTo(y.Y));
                        var minY = points.First().Y;
                        var maxY = points.Last().Y;
                        return TryCreateBound(
                            new XYZ(minX - offset, minY, cutPlanZ),
                            new XYZ(minX - offset, maxY, cutPlanZ));
                    }

                case ExtremeWallVariant.Top:
                    {
                        points.Sort((x, y) => x.X.CompareTo(y.X));
                        var minX = points.First().X;
                        var maxX = points.Last().X;
                        points.Sort((x, y) => x.Y.CompareTo(y.Y));
                        var maxY = points.Last().Y;
                        return TryCreateBound(
                            new XYZ(minX, maxY + offset, cutPlanZ),
                            new XYZ(maxX, maxY + offset, cutPlanZ));
                    }

                case ExtremeWallVariant.Bottom:
                    {
                        points.Sort((x, y) => x.X.CompareTo(y.X));
                        var minX = points.First().X;
                        var maxX = points.Last().X;
                        points.Sort((x, y) => x.Y.CompareTo(y.Y));
                        var minY = points.First().Y;
                        return TryCreateBound(
                            new XYZ(minX, minY - offset, cutPlanZ),
                            new XYZ(maxX, minY - offset, cutPlanZ));
                    }
            }

            return null;
        }
        
        public static Line TryCreateBound(XYZ pt1, XYZ pt2)
        {
            if (pt1.DistanceTo(pt2) < MmToFt(1))
                return null;
            return Line.CreateBound(pt1, pt2);
        }
       
        /// <summary>Get advanced wall from list by Id</summary>
        public static AdvancedWall GetAdvancedWallFromListById(IEnumerable<AdvancedWall> walls, long id)
        {
            foreach (var advancedWall in walls)
            {
                if (advancedWall.Wall.Id.GetValueCompat().Equals(id))
                    return advancedWall;
            }
            return null;
        }

        /// <summary>Get max wall width from list</summary>
        public static double GetMaxWallWidthFromList(IEnumerable<AdvancedWall> walls)
        {
            return walls.Select(w => w.Wall.Width).Max();
        }

        /// <summary>Convert millimeters to feet (Revit internal units).
        /// Uses direct math to avoid version-specific API (DisplayUnitType vs UnitTypeId).</summary>
        public static double MmToFt(this double mm)
        {
            return mm / 304.8;
        }

        /// <summary>Convert millimeters to feet (Revit internal units).</summary>
        public static double MmToFt(this int mm)
        {
            return mm / 304.8;
        }

        private static List<XYZ> GetPointsFromGeometry(this AdvancedWall advancedWall, View view)
        {
            var points = new List<XYZ>();
            foreach (var geometryObject in advancedWall.Wall.get_Geometry(new Options()
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                View = view
            }))
            {
                if (geometryObject is Solid solid)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        foreach (var xyz in edge.Tessellate())
                        {
                            if (!points.Contains(xyz))
                                points.Add(xyz);
                        }
                    }
                }
            }

            return points;
        }
    }
}
