namespace PluginCotasExteriores.Body.AdvancedClasses
{
    using Autodesk.Revit.DB;
    using Enumerators;

    /// <summary>
    /// Advanced Grid
    /// </summary>
    public class AdvancedGrid
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="grid">Revit grid</param>
        public AdvancedGrid(Grid grid)
        {
            Grid = grid;
            DefineAdvancedGridFields();
        }

        /// <summary>
        /// Revit Grid
        /// </summary>
        public Grid Grid { get; }

        /// <summary>False si no se pudieron determinar los valores del elemento</summary>
        public bool IsDefined { get; set; } = true;

        /// <summary>Orientación del elemento</summary>
        public ElementOrientation Orientation { get; set; }

        /// <summary>Tipo de curva base</summary>
        public ElementCurveType CurveType { get; set; }

        /// <summary>
        /// Start Point
        /// </summary>
        public XYZ StartPoint { get; set; }

        /// <summary>
        /// End Point
        /// </summary>
        public XYZ EndPoint { get; set; }
        
        private void DefineAdvancedGridFields()
        {
            // get location curve
            var curve = Grid.Curve;
            if (curve == null)
            {
                IsDefined = false;
                return;
            }

            switch (curve)
            {
                // get curve type
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

            // points
            StartPoint = curve.GetEndPoint(0);
            EndPoint = curve.GetEndPoint(1);
            
            // get orientation
            Orientation = GeometryHelpers.GetElementOrientation(curve);
            if (Orientation == ElementOrientation.CloseToHorizontal ||
                Orientation == ElementOrientation.CloseToVertical ||
                Orientation == ElementOrientation.Undefined)
                IsDefined = false;
        }
    }
}
