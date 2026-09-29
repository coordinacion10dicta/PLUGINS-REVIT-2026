using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD.SGH.Services
{
    public class AreaService
    {
        public const string LayerPerimetro = "SH - Perimetro";
        public const string LayerTags = "SH - Tags";

        /// Asegura que la capa "SH - Perimetro" exista en la base de datos con color Magenta.
        public static void EnsureLayersExist(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                if (!lt.Has(LayerPerimetro))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord
                    {
                        Name = LayerPerimetro,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 6) // Magenta
                    };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                if (!lt.Has(LayerTags))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord
                    {
                        Name = LayerTags,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 5) // Azul
                    };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                tr.Commit();
            }
        }

        /// Obtiene el área en m² y el factor de escala a partir de una entidad de curva/polilínea.
        public static (double AreaM2, double Scale, Point3d Centroid) CalculateArea(Curve curve)
        {
            if (curve == null)
                return (0, 1.0, Point3d.Origin);

            double rawArea = curve.Area;
            double scale = 1.0;
            double areaM2 = rawArea;

            // Detección automática de unidades: Si el área es superior a 10,000 unidades, está en mm²
            if (rawArea > 10000.0)
            {
                scale = 1000.0;
                areaM2 = rawArea / 1000000.0;
            }

            // Centroide geométrico estimado por Extents
            var extents = curve.GeometricExtents;
            var centroid = new Point3d(
                (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                0.0
            );

            return (Math.Round(areaM2, 2), scale, centroid);
        }
    }
}
