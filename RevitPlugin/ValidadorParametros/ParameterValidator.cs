using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MiNamespace.ValidadorParametros
{
    public static class ParameterValidator
    {
        private const int MaxIssues = 3000;

        public static ValidationSummary Validate(
            Document doc,
            string disciplina,
            List<DisciplineParameter> parametrosRequeridos)
        {
            var summary = new ValidationSummary();
            var issues = summary.Issues;

            var reglas = parametrosRequeridos
                .Where(p => p.Disciplina.Equals(disciplina, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!reglas.Any()) return summary;

            var categoriasFiltro = new HashSet<string>(
                reglas.Select(p => p.Categoria)
                      .Where(c => !string.IsNullOrWhiteSpace(c))
                      .Distinct(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var elementos = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            var familiaIds = new HashSet<ElementId>();

            foreach (Element elem in elementos)
            {
                if (issues.Count >= MaxIssues) break;
                if (elem.Category == null) continue;

                string catName = elem.Category.Name;
                if (categoriasFiltro.Count > 0 && !categoriasFiltro.Contains(catName))
                    continue;

                var reglasCat = reglas.Where(p =>
                    string.IsNullOrWhiteSpace(p.Categoria) ||
                    p.Categoria.Equals(catName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!reglasCat.Any()) continue;

                summary.TotalElementos++;

                var fi = elem as FamilyInstance;
                if (fi?.Symbol?.Family != null)
                    familiaIds.Add(fi.Symbol.Family.Id);

#if REVIT_LEGACY_ELEMENTID
                string elemId = elem.Id.IntegerValue.ToString();
#else
                string elemId = elem.Id.Value.ToString();
#endif
                string familia  = fi?.Symbol?.Family?.Name ?? catName;
                string tipoElem = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "";

                Element elemType = doc.GetElement(elem.GetTypeId());

                var nombresInstancia = elem.Parameters.Cast<Parameter>()
                    .Select(p => p.Definition.Name).ToList();
                var nombresTipo = elemType != null
                    ? elemType.Parameters.Cast<Parameter>().Select(p => p.Definition.Name).ToList()
                    : new List<string>();

                var duplicados = new HashSet<string>(
                    nombresInstancia
                        .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var regla in reglasCat)
                {
                    bool esAlcanceTipo  = regla.Alcance?.Equals("Tipo", StringComparison.OrdinalIgnoreCase) == true;
                    var nombresRef      = esAlcanceTipo ? nombresTipo : nombresInstancia;
                    var nombresOpuesto  = esAlcanceTipo ? nombresInstancia : nombresTipo;

                    bool exacto        = nombresRef.Any(n => n.Equals(regla.NombreParametro, StringComparison.Ordinal));
                    bool insensible    = nombresRef.Any(n => n.Equals(regla.NombreParametro, StringComparison.OrdinalIgnoreCase));
                    bool enScopeOpuesto = nombresOpuesto.Any(n => n.Equals(regla.NombreParametro, StringComparison.OrdinalIgnoreCase));

                    if (!insensible)
                    {
                        if (enScopeOpuesto)
                        {
                            string scopeReal = esAlcanceTipo ? "Instancia" : "Tipo";
                            issues.Add(Nuevo(elemId, familia, tipoElem, catName, disciplina, regla,
                                TipoProblema.AlcanceIncorrecto,
                                $"'{regla.NombreParametro}' está en '{scopeReal}' pero la regla pide '{regla.Alcance}'.",
                                ""));
                        }
                        else if (regla.Obligatorio)
                        {
                            issues.Add(Nuevo(elemId, familia, tipoElem, catName, disciplina, regla,
                                TipoProblema.ParametroFaltante,
                                $"'{regla.NombreParametro}' no existe en scope '{regla.Alcance}'.",
                                ""));
                        }
                        continue;
                    }

                    if (!exacto)
                    {
                        string nombreReal = nombresRef.First(n =>
                            n.Equals(regla.NombreParametro, StringComparison.OrdinalIgnoreCase));
                        issues.Add(Nuevo(elemId, familia, tipoElem, catName, disciplina, regla,
                            TipoProblema.NombreIncorrecto,
                            $"Se esperaba '{regla.NombreParametro}', encontrado '{nombreReal}'.",
                            nombreReal));
                    }

                    if (regla.Obligatorio)
                    {
                        Element scopeElem = esAlcanceTipo ? elemType : elem;
                        Parameter param   = scopeElem?.LookupParameter(regla.NombreParametro)
                                         ?? scopeElem?.Parameters.Cast<Parameter>().FirstOrDefault(p =>
                                                p.Definition.Name.Equals(regla.NombreParametro,
                                                    StringComparison.OrdinalIgnoreCase));

                        if (param != null && string.IsNullOrWhiteSpace(GetValue(param)))
                            issues.Add(Nuevo(elemId, familia, tipoElem, catName, disciplina, regla,
                                TipoProblema.ValorVacio,
                                $"'{regla.NombreParametro}' está vacío.", ""));
                    }
                }

                foreach (string dup in duplicados)
                    issues.Add(NewDuplicate(elemId, familia, tipoElem, catName, disciplina, dup));
            }

            summary.TotalFamilias = familiaIds.Count;
            return summary;
        }

        private static ValidationIssue Nuevo(
            string id, string familia, string tipo, string cat, string disciplina,
            DisciplineParameter regla, TipoProblema problema, string desc, string valor)
        {
            return new ValidationIssue
            {
                ElementId           = id,
                Familia             = familia,
                TipoElemento        = tipo,
                Categoria           = cat,
                Disciplina          = disciplina,
                Parametro           = regla.NombreParametro,
                Alcance             = regla.Alcance ?? "Instancia",
                TipoDeProblema      = problema,
                Severidad           = regla.Obligatorio ? Severidad.Critico : Severidad.Advertencia,
                DescripcionProblema = desc,
                ValorActual         = valor
            };
        }

        private static ValidationIssue NewDuplicate(
            string id, string familia, string tipo, string cat, string disciplina, string paramName)
        {
            return new ValidationIssue
            {
                ElementId           = id,
                Familia             = familia,
                TipoElemento        = tipo,
                Categoria           = cat,
                Disciplina          = disciplina,
                Parametro           = paramName,
                Alcance             = "Instancia",
                TipoDeProblema      = TipoProblema.Duplicado,
                Severidad           = Severidad.Advertencia,
                DescripcionProblema = $"'{paramName}' aparece más de una vez.",
                ValorActual         = ""
            };
        }

        private static string GetValue(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String:  return param.AsString() ?? "";
                case StorageType.Double:  return param.AsValueString() ?? "";
                case StorageType.Integer: return param.AsInteger().ToString();
                case StorageType.ElementId:
#if REVIT_LEGACY_ELEMENTID
                    return param.AsElementId()?.IntegerValue.ToString() ?? "";
#else
                    return param.AsElementId()?.Value.ToString() ?? "";
#endif
                default: return "";
            }
        }
    }
}
