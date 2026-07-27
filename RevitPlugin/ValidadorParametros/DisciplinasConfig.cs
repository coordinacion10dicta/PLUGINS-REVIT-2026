using System.Collections.Generic;
using Newtonsoft.Json;

namespace MiNamespace.ValidadorParametros
{
    public class ParametroConfig
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; }

        [JsonProperty("alcance")]
        public string Alcance { get; set; } = "Instancia";

        [JsonProperty("obligatorio")]
        public bool Obligatorio { get; set; }

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = "";
    }

    public class PlantillaBloque
    {
        [JsonProperty("tipo")]
        public string Tipo { get; set; }   // "texto" | "keyword"

        [JsonProperty("valor")]
        public string Valor { get; set; }
    }

    public class KeywordConfig
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; }          // alias abstracto: "diametro"

        [JsonProperty("parametros")]
        public List<string> Parametros { get; set; } = new List<string>(); // candidatos en orden de prioridad
    }

    public class CategoriaConfig
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; }

        [JsonProperty("parametros")]
        public List<ParametroConfig> Parametros { get; set; } = new List<ParametroConfig>();

        [JsonProperty("keywords")]
        public List<KeywordConfig> Keywords { get; set; } = new List<KeywordConfig>();

        [JsonProperty("plantilla")]
        public List<PlantillaBloque> Plantilla { get; set; } = new List<PlantillaBloque>();
    }

    public class DisciplinaConfig
    {
        [JsonProperty("nombre")]
        public string Nombre { get; set; }

        [JsonProperty("categorias")]
        public List<CategoriaConfig> Categorias { get; set; } = new List<CategoriaConfig>();
    }

    public class DisciplinasRoot
    {
        [JsonProperty("disciplinas")]
        public List<DisciplinaConfig> Disciplinas { get; set; } = new List<DisciplinaConfig>();
    }
}
