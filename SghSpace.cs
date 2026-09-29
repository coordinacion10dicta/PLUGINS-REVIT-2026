using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

public class SghSpace
{
    public string Numero { get; set; }
    public double Area { get; set; }
    public string GrupoOcupacion { get; set; }
    public string GrupoOcupacionNfpa { get; set; }
    public string CargaOcupacion { get; set; }
    public string CargaOcupacionNfpa { get; set; }
    public string Espacio { get; set; }
    public string Piso { get; set; }
    public Autodesk.AutoCAD.Geometry.Point3d InsertionPoint { get; set; }
    public double DrawingScale { get; set; }
    public Autodesk.AutoCAD.DatabaseServices.ObjectId PolylineId { get; set; }
    public double? AnchoPasillosMm { get; set; }
    public double? AnchoEscalerasMm { get; set; }

    // Propiedades formateadas
    public string FormattedArea { get; set; }
    public string FormattedUso { get; set; }
    public string FormattedCo { get; set; }
    public string FormattedEspacio { get; set; }
    public string FormattedPiso { get; set; }
}
