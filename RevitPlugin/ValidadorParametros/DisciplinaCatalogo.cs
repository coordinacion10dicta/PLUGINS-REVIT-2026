using System.Collections.Generic;

namespace MiNamespace.ValidadorParametros
{
    // Catálogo predefinido de disciplinas y sus categorías Revit disponibles.
    // Las categorías usan el nombre exacto de la API (Category.Name) para que
    // ParameterValidator pueda comparar contra elem.Category.Name.
    public static class DisciplinaCatalogo
    {
        public static readonly List<string> Disciplinas = new List<string>
        {
            "Hidráulico",
            "Eléctrico",
            "Electrónico",
            "HVAC",
            "Iluminación",
            "Seguridad Humana"
        };

        private static readonly Dictionary<string, List<string>> _categoriasPorDisciplina =
            new Dictionary<string, List<string>>
        {
            {
                "Hidráulico", new List<string>
                {
                    "Pipes",
                    "Pipe Fittings",
                    "Pipe Accessories",
                    "Pipe Insulations",
                    "Pipe Systems",
                    "Plumbing Fixtures",
                    "Mechanical Equipment",
                    "Flex Pipes",
                    "Valves"
                }
            },
            {
                "Eléctrico", new List<string>
                {
                    "Electrical Equipment",
                    "Electrical Fixtures",
                    "Conduits",
                    "Conduit Fittings",
                    "Cable Trays",
                    "Cable Tray Fittings",
                    "Electrical Circuits",
                    "Panels",
                    "Switchboards"
                }
            },
            {
                "Electrónico", new List<string>
                {
                    "Communication Devices",
                    "Data Devices",
                    "Nurse Call Devices",
                    "Telephone Devices",
                    "Security Devices",
                    "Fire Alarm Devices",
                    "Intercoms"
                }
            },
            {
                "HVAC", new List<string>
                {
                    "Mechanical Equipment",
                    "Air Terminals",
                    "Ducts",
                    "Duct Fittings",
                    "Duct Accessories",
                    "Duct Insulations",
                    "Duct Systems",
                    "Flex Ducts",
                    "Mechanical Control Devices",
                    "Air Systems"
                }
            },
            {
                "Iluminación", new List<string>
                {
                    "Lighting Fixtures",
                    "Lighting Devices",
                    "Electrical Fixtures",
                    "Electrical Equipment"
                }
            },
            {
                "Seguridad Humana", new List<string>
                {
                    "Fire Alarm Devices",
                    "Sprinklers",
                    "Pipes",
                    "Pipe Fittings",
                    "Pipe Accessories",
                    "Pipe Systems",
                    "Communication Devices",
                    "Security Devices"
                }
            }
        };

        public static List<string> GetCategorias(string disciplina)
        {
            if (_categoriasPorDisciplina.TryGetValue(disciplina, out var cats))
                return new List<string>(cats);
            return new List<string>();
        }
    }
}
