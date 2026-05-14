using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MiNamespace.ValidadorParametros
{
    public static class ParameterValidator
    {
        private const int MaxIssues = 2000;

        public static List<ValidationIssue> Validate(
            Document doc,
            string disciplina,
            List<DisciplineParameter> parametrosRequeridos)
        {
            var issues = new List<ValidationIssue>();

            var paramsDisciplina = parametrosRequeridos
                .Where(p => p.Disciplina.Equals(disciplina, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!paramsDisciplina.Any())
                return issues;

            var categoriasFiltro = new HashSet<string>(
                paramsDisciplina
                    .Select(p => p.Categoria)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var elementos = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (Element elem in elementos)
            {
                if (issues.Count >= MaxIssues) break;
                if (elem.Category == null) continue;

                string catName = elem.Category.Name;

                if (categoriasFiltro.Count > 0 && !categoriasFiltro.Contains(catName))
                    continue;

                var reglasParaElem = paramsDisciplina
                    .Where(p => string.IsNullOrWhiteSpace(p.Categoria)
                             || p.Categoria.Equals(catName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!reglasParaElem.Any()) continue;

                var nombresEnElemento = elem.Parameters
                    .Cast<Parameter>()
                    .Select(p => p.Definition.Name)
                    .ToList();

                // Detectar duplicados dentro del elemento
                var duplicados = new HashSet<string>(
                    nombresEnElemento
                        .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key),
                    StringComparer.OrdinalIgnoreCase);

#if REVIT_LEGACY_ELEMENTID
                string elemId = elem.Id.IntegerValue.ToString();
#else
                string elemId = elem.Id.Value.ToString();
#endif
                string familia = (elem as FamilyInstance)?.Symbol?.Family?.Name ?? catName;
                string tipo = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)
                                 ?.AsValueString() ?? string.Empty;

                foreach (var regla in reglasParaElem)
                {
                    bool exacto = nombresEnElemento.Any(n =>
                        n.Equals(regla.NombreParametro, StringComparison.Ordinal));
                    bool insensible = nombresEnElemento.Any(n =>
                        n.Equals(regla.NombreParametro, StringComparison.OrdinalIgnoreCase));

                    if (!insensible)
                    {
                        if (regla.Requerido)
                            issues.Add(NewIssue(elemId, familia, tipo, catName, regla.NombreParametro,
                                TipoProblema.ParametroFaltante,
                                $"El parámetro '{regla.NombreParametro}' no existe en el elemento.", ""));
                        continue;
                    }

                    // Nombre con casing incorrecto
                    if (!exacto)
                    {
                        string nombreReal = nombresEnElemento
                            .First(n => n.Equals(regla.NombreParametro, StringComparison.OrdinalIgnoreCase));
                        issues.Add(NewIssue(elemId, familia, tipo, catName, regla.NombreParametro,
                            TipoProblema.NombreIncorrecto,
                            $"Se esperaba '{regla.NombreParametro}' pero se encontró '{nombreReal}'.",
                            nombreReal));
                    }

                    // Valor vacío en parámetros requeridos
                    if (regla.Requerido)
                    {
                        var param = elem.LookupParameter(regla.NombreParametro)
                                 ?? elem.Parameters.Cast<Parameter>().FirstOrDefault(p =>
                                        p.Definition.Name.Equals(regla.NombreParametro,
                                            StringComparison.OrdinalIgnoreCase));

                        if (param != null && string.IsNullOrWhiteSpace(GetValue(param)))
                            issues.Add(NewIssue(elemId, familia, tipo, catName, regla.NombreParametro,
                                TipoProblema.ValorVacio,
                                $"El parámetro '{regla.NombreParametro}' está vacío.", ""));
                    }
                }

                // Duplicados
                foreach (string dup in duplicados)
                {
                    issues.Add(NewIssue(elemId, familia, tipo, catName, dup,
                        TipoProblema.Duplicado,
                        $"El parámetro '{dup}' aparece más de una vez en el elemento.", ""));
                }
            }

            return issues;
        }

        private static ValidationIssue NewIssue(
            string id, string familia, string tipo, string cat,
            string param, TipoProblema problema, string desc, string valor)
        {
            return new ValidationIssue
            {
                ElementId          = id,
                Familia            = familia,
                Tipo               = tipo,
                Categoria          = cat,
                Parametro          = param,
                TipoDeProblema     = problema,
                DescripcionProblema = desc,
                ValorActual        = valor
            };
        }

        private static string GetValue(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String:    return param.AsString() ?? "";
                case StorageType.Double:    return param.AsValueString() ?? "";
                case StorageType.Integer:   return param.AsInteger().ToString();
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
