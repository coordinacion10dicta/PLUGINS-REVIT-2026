namespace MiNamespace.ValidadorParametros
{
    // Alcance: "Tipo" = parameter lives on the element type/symbol, "Instancia" = on the element instance
    public class DisciplineParameter
    {
        public string Disciplina     { get; set; }
        public string Categoria      { get; set; }
        public string NombreParametro { get; set; }
        public string Alcance        { get; set; }   // "Tipo" | "Instancia"
        public bool   Obligatorio    { get; set; }
        public string Descripcion    { get; set; }
    }
}
