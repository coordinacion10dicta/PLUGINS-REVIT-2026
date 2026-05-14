using System.ComponentModel;

namespace MiNamespace.ValidadorParametros
{
    public enum TipoProblema
    {
        ParametroFaltante,
        ValorVacio,
        NombreIncorrecto,
        Duplicado,
        AlcanceIncorrecto   // parameter exists but on wrong scope (Tipo vs Instancia)
    }

    public enum Severidad
    {
        Critico,
        Advertencia
    }

    public class ValidationIssue : INotifyPropertyChanged
    {
        public string      ElementId          { get; set; }
        public string      Familia            { get; set; }
        public string      TipoElemento       { get; set; }   // element type name
        public string      Categoria          { get; set; }
        public string      Disciplina         { get; set; }
        public string      Parametro          { get; set; }
        public string      Alcance            { get; set; }   // "Tipo" | "Instancia" (expected)
        public TipoProblema TipoDeProblema    { get; set; }
        public Severidad   Severidad          { get; set; }
        public string      DescripcionProblema { get; set; }
        public string      ValorActual        { get; set; }

        public bool EsCritico => Severidad == Severidad.Critico;

        public string TipoProblemaTexto
        {
            get
            {
                switch (TipoDeProblema)
                {
                    case TipoProblema.ParametroFaltante:  return "Faltante";
                    case TipoProblema.ValorVacio:          return "Vacío";
                    case TipoProblema.NombreIncorrecto:    return "Nombre Incorrecto";
                    case TipoProblema.Duplicado:           return "Duplicado";
                    case TipoProblema.AlcanceIncorrecto:   return "Alcance Incorrecto";
                    default:                               return "Desconocido";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
