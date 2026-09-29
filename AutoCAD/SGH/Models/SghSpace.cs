using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutoCAD.SGH.Models
{
    public class SghSpace
    {
        public string Numero { get; set; } = "1";
        public double Area { get; set; }
        public string GrupoOcupacion { get; set; } = "A";
        public string GrupoOcupacionNfpa { get; set; } = string.Empty;
        public string CargaOcupacion { get; set; } = "1";
        public string CargaOcupacionNfpa { get; set; } = string.Empty;
        public string Espacio { get; set; } = string.Empty;
        public string Piso { get; set; } = string.Empty;

        private string _grupoOcupacionNsr = string.Empty;
        public string GrupoOcupacionNsr
        {
            get => !string.IsNullOrWhiteSpace(_grupoOcupacionNsr) ? _grupoOcupacionNsr : GrupoOcupacion;
            set => _grupoOcupacionNsr = value;
        }

        private string _cargaOcupacionNsr = string.Empty;
        public string CargaOcupacionNsr
        {
            get => !string.IsNullOrWhiteSpace(_cargaOcupacionNsr) ? _cargaOcupacionNsr : CargaOcupacion;
            set => _cargaOcupacionNsr = value;
        }

        public string Ubicacion { get => Espacio; set => Espacio = value; }
        public string Nivel { get => Piso; set => Piso = value; }

        public Point3d InsertionPoint { get; set; } = Point3d.Origin;
        public double DrawingScale { get; set; } = 1.0;
        public ObjectId PolylineId { get; set; } = ObjectId.Null;
        public string PolylineHandle { get; set; } = string.Empty;
        public double? AnchoPasillosMm { get; set; }
        public double? AnchoEscalerasMm { get; set; }
        public string FormattedArea => $"A: {Area.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}m2";
        public string FormattedUso => $"U:  ({GrupoOcupacion})";
        public string FormattedCo => $"CO:  {CargaOcupacion}";
        public string FormattedEspacio => string.IsNullOrWhiteSpace(Espacio) ? "" : $"ESP: {Espacio}";
        public string FormattedPiso => string.IsNullOrWhiteSpace(Piso) ? "" : $"PISO: {Piso}";
        public string FormattedUbicacion => FormattedEspacio;
        public string FormattedNivel => FormattedPiso;
    }
}
