namespace PluginCotasExteriores.Body.AdvancedClasses
{
    using System.Collections.Generic;
    using System.Linq;
    using Autodesk.Revit.DB;
    using Enumerators;
    using Helpers;

    /// <summary>
    /// Clase avanzada para trabajo con muros
    /// </summary>
    public class AdvancedWall
    {
        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="wall">Revit wall</param>
        public AdvancedWall(Wall wall)
        {
            Wall = wall;
            Solids = new List<Solid>();
            Edges = new List<Edge>();
            AdvancedPlanarFaces = new List<AdvancedPlanarFace>();
            DefineAdvancedWallFields();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Revit wall
        /// </summary>
        public Wall Wall { get; }

        /// <summary>False si no se pudieron determinar los valores del elemento</summary>
        public bool IsDefined { get; private set; } = true;

        /// <summary>Orientación del elemento</summary>
        public ElementOrientation Orientation { get; private set; }

        /// <summary>Tipo de curva base</summary>
        public ElementCurveType CurveType { get; private set; }

        /// <summary>LocationCurve's Start Point</summary>
        public XYZ StartPoint { get; private set; }

        /// <summary>LocationCurve's End Point</summary>
        public XYZ EndPoint { get; private set; }

        /// <summary>LocationCurve's Middle Point</summary>
        public XYZ MidPoint { get; private set; }

        /// <summary>LocationCurve's Curve</summary>
        public Curve LocationCurveCurve { get; private set; }

        /// <summary>Wall's solids</summary>
        public List<Solid> Solids { get; }

        /// <summary>Wall's edges</summary>
        public List<Edge> Edges { get; }

        /// <summary>Wall's advanced faces</summary>
        public List<AdvancedPlanarFace> AdvancedPlanarFaces { get; }

        #endregion

        #region Private Methods

        private void DefineAdvancedWallFields()
        {
            // get location curve
            if (!(Wall.Location is LocationCurve locationCurve))
            {
                IsDefined = false;
                return;
            }
            
            // Get curve from location curve
            LocationCurveCurve = locationCurve.Curve;

            // get curve type
            switch (locationCurve.Curve)
            {
                case Line _:
                    CurveType = ElementCurveType.Line;
                    break;
                case Arc _:
                    CurveType = ElementCurveType.Arc;
                    break;
                default:
                    IsDefined = false;
                    return;
            }

            // get ends points
            StartPoint = locationCurve.Curve.GetEndPoint(0);
            EndPoint = locationCurve.Curve.GetEndPoint(1);
            MidPoint = 0.5 * (StartPoint + EndPoint);
            
            // get solids
            GetSolids();
            
            // get faces and edges
            GetFacesAndEdges();
            
            if (Edges.Count == 0 || AdvancedPlanarFaces.Count == 0)
            {
                IsDefined = false;
                return;
            }

            // get orientation
            Orientation = GeometryHelpers.GetElementOrientation(locationCurve.Curve);
            if (Orientation == ElementOrientation.CloseToHorizontal ||
                Orientation == ElementOrientation.CloseToVertical ||
                Orientation == ElementOrientation.Undefined)
                IsDefined = false;
        }

        private void GetSolids()
        {
            var options = new Options
            {
                ComputeReferences = true // Obligatorio true para construir dimensiones por Reference
            };
            foreach (var geometryObject in Wall.get_Geometry(options).GetTransformed(Transform.Identity))
            {
                var solid = geometryObject as Solid;
                if (solid != null && solid.Volume > 0.0)
                {
                    Solids.Add(solid);
                }
            }
        }

        private void GetFacesAndEdges()
        {
            /*
             * Si un muro tiene un hueco (Opening), las caras generadas por ese hueco no tienen Reference.
             * Por eso se agregan las caras del hueco en lugar de las del muro.
             */
            var inserts = Wall.FindInserts(true, false, false, false);
            var options = new Options
            {
                IncludeNonVisibleObjects = true,
                ComputeReferences = true // Obligatorio true para construir dimensiones por Reference
            };

            var skipOpeningIds = new List<long>();

            foreach (var solid in Solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    Edges.Add(edge);
                }

                foreach (Face face in solid.Faces)
                {
                    var planarFace = face as PlanarFace;
                    if (planarFace != null)
                    {
                        if (planarFace.Reference != null)
                        {
                            var advancedPlanarFace = new AdvancedPlanarFace(Wall.Id.GetValueCompat(), planarFace);
                            if (advancedPlanarFace.IsDefined)
                                AdvancedPlanarFaces.Add(advancedPlanarFace);
                        }
                        else
                        {
                            foreach (var elementId in Wall.GetGeneratingElementIds(planarFace))
                            {
                                if (elementId == Wall.Id)
                                    continue;
                                if (skipOpeningIds.Contains(elementId.GetValueCompat()))
                                    continue;
                                if (!inserts.Contains(elementId))
                                    continue;

                                var element = Wall.Document.GetElement(elementId);
                                foreach (var geometryObject in element.get_Geometry(options).GetTransformed(Transform.Identity))
                                {
                                    if (!(geometryObject is Solid s)) 
                                        continue;

                                    foreach (Face f in s.Faces)
                                    {
                                        if (!(f is PlanarFace pf) || pf.Reference == null) 
                                            continue;

                                        var apf = new AdvancedPlanarFace(Wall.Id.GetValueCompat(), pf);
                                        if (!apf.IsDefined) 
                                            continue;

                                        AdvancedPlanarFaces.Add(apf);
                                        skipOpeningIds.Add(elementId.GetValueCompat());
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Public Methods
        
        /// <summary>Obtener el valor máximo de Z de las caras del muro</summary>
        public double GetMaxZ()
        {
            var zList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                foreach (var edge in face.Edges)
                {
                    var pt1 = edge.AsCurve().GetEndPoint(0);
                    var pt2 = edge.AsCurve().GetEndPoint(1);
                    if (!zList.Contains(pt1.Z))
                        zList.Add(pt1.Z);
                    if (!zList.Contains(pt2.Z))
                        zList.Add(pt2.Z);
                }
            }

            return zList.Max();
        }
        
        /// <summary>Obtener el valor mínimo de Z de las caras del muro</summary>
        public double GetMinZ()
        {
            var zList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                foreach (var edge in face.Edges)
                {
                    var pt1 = edge.AsCurve().GetEndPoint(0);
                    var pt2 = edge.AsCurve().GetEndPoint(1);
                    if (!zList.Contains(pt1.Z))
                        zList.Add(pt1.Z);
                    if (!zList.Contains(pt2.Z))
                        zList.Add(pt2.Z);
                }
            }

            return zList.Min();
        }

        public double GetMinX()
        {
            var xList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                xList.Add(face.MinX);
            }

            return xList.Min();
        }

        public double GetMaxX()
        {
            var xList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                xList.Add(face.MaxX);
            }

            return xList.Max();
        }

        public double GetMinY()
        {
            var yList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                yList.Add(face.MinY);
            }

            return yList.Min();
        }
        
        public double GetMaxY()
        {
            var yList = new List<double>();
            
            foreach (var face in AdvancedPlanarFaces)
            {
                yList.Add(face.MaxY);
            }

            return yList.Max();
        }
        
        #endregion
    }
}
