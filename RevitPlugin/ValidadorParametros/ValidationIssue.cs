using System.ComponentModel;

namespace MiNamespace.ValidadorParametros
{
    public enum TipoProblema
    {
        ParametroFaltante,
        ValorVacio,
        NombreIncorrecto,
        Duplicado
    }

    public class ValidationIssue : INotifyPropertyChanged
    {
        public string ElementId { get; set; }
        public string Familia { get; set; }
        public string Tipo { get; set; }
        public string Categoria { get; set; }
        public string Parametro { get; set; }
        public TipoProblema TipoDeProblema { get; set; }
        public string DescripcionProblema { get; set; }
        public string ValorActual { get; set; }

        public string TipoProblemaTexto
        {
            get
            {
                switch (TipoDeProblema)
                {
                    case TipoProblema.ParametroFaltante: return "Parámetro Faltante";
                    case TipoProblema.ValorVacio:         return "Valor Vacío";
                    case TipoProblema.NombreIncorrecto:   return "Nombre Incorrecto";
                    case TipoProblema.Duplicado:          return "Duplicado";
                    default:                              return "Desconocido";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
