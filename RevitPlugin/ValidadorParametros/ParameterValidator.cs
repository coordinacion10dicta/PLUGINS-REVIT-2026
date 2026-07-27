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

            var reglas = parametrosRequeridos
                .Where(p => p.Disciplina.Equals(disciplina, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!reglas.Any()) return summary;

            var reglasPorCategoria = reglas
                .Where(r => !string.IsNullOrWhiteSpace(r.Categoria))
                .GroupBy(r => r.Categoria, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var familiaIds = new HashSet<ElementId>();

            foreach (var kvp in reglasPorCategoria)
            {
                if (summary.Issues.Count >= MaxIssues) break;

                string categoria = kvp.Key;
                var reglasCat   = kvp.Value;

                BuiltInCategory bic = MapToBuiltInCategory(categoria);
                if (bic == BuiltInCategory.INVALID) continue;

                ICollection<ElementId> elemIds;
                try
                {
                    elemIds = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                }
                catch
                {
                    continue;
                }

                foreach (ElementId eid in elemIds)
                {
                    if (summary.Issues.Count >= MaxIssues) break;

                    Element elem = null;
                    try { elem = doc.GetElement(eid); }
                    catch { continue; }

                    if (elem == null) continue;
                    if (!elem.IsValidObject) continue;

                    try { summary.TotalElementos++; }
                    catch { }

                    string elemIdStr = null;
                    string familia   = null;

#if REVIT_LEGACY_ELEMENTID
                    try { elemIdStr = elem.Id.IntegerValue.ToString(); }
                    catch { elemIdStr = eid.IntegerValue.ToString(); }
#else
                    try { elemIdStr = elem.Id.Value.ToString(); }
                    catch { elemIdStr = eid.Value.ToString(); }
#endif

                    try
                    {
                        var fi = elem as FamilyInstance;
                        if (fi != null && fi.Symbol != null && fi.Symbol.Family != null)
                        {
                            familia = fi.Symbol.Family.Name;
                            familiaIds.Add(fi.Symbol.Family.Id);
                        }
                        else { familia = categoria; }
                    }
                    catch { familia = categoria; }

                    string tipoElem = "";
                    try { tipoElem = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? ""; }
                    catch { }

                    foreach (var regla in reglasCat)
                    {
                        try
                        {
                            ValidarRegla(summary.Issues, elem, elemIdStr, familia, tipoElem, categoria, disciplina, regla);
                        }
                        catch { }
                    }
                }
            }

            summary.TotalFamilias = familiaIds.Count;
            return summary;
        }

        private static void ValidarRegla(
            List<ValidationIssue> issues,
            Element elem,
            string elemId, string familia, string tipoElem,
            string catName, string disciplina,
            DisciplineParameter regla)
        {
            if (issues.Count >= MaxIssues) return;

            Parameter param = null;
            try { param = elem.LookupParameter(regla.NombreParametro); }
            catch { return; }

            if (param == null)
            {
                if (regla.Obligatorio)
                {
                    issues.Add(CrearIssue(elemId, familia, tipoElem, catName, disciplina, regla,
                        TipoProblema.ParametroFaltante,
                        $"'{regla.NombreParametro}' no existe."));
                }
                return;
            }

            if (!regla.Obligatorio) return;

            if (param.IsReadOnly) return;

            string valor = "";
            try { valor = GetValueString(param); }
            catch { }

            if (string.IsNullOrWhiteSpace(valor))
            {
                issues.Add(CrearIssue(elemId, familia, tipoElem, catName, disciplina, regla,
                    TipoProblema.ValorVacio,
                    $"'{regla.NombreParametro}' está vacío.", valor));
            }
        }

        private static string GetValueString(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:    return p.AsString() ?? "";
                    case StorageType.Double:    return p.AsValueString() ?? "";
                    case StorageType.Integer:   return p.AsInteger().ToString();
                    case StorageType.ElementId:
#if REVIT_LEGACY_ELEMENTID
                        return p.AsElementId()?.IntegerValue.ToString() ?? "";
#else
                        return p.AsElementId()?.Value.ToString() ?? "";
#endif
                    default: return "";
                }
            }
            catch { return ""; }
        }

        private static ValidationIssue CrearIssue(
            string id, string familia, string tipo, string cat, string disciplina,
            DisciplineParameter regla, TipoProblema problema, string desc, string valor = "")
        {
            return new ValidationIssue
            {
                ElementId           = id,
                Familia             = familia,
                TipoElemento        = tipo,
                Categoria           = cat,
                Disciplina          = disciplina,
                Parametro           = regla.NombreParametro,
                Alcance             = "Instancia",
                TipoDeProblema      = problema,
                Severidad           = regla.Obligatorio ? Severidad.Critico : Severidad.Advertencia,
                DescripcionProblema = desc,
                ValorActual         = valor
            };
        }

        private static BuiltInCategory MapToBuiltInCategory(string categoryName)
        {
            switch (categoryName)
            {
                case "Pipes":                 return BuiltInCategory.OST_PipeCurves;
                case "Pipe Fittings":         return BuiltInCategory.OST_PipeFitting;
                case "Pipe Accessories":      return BuiltInCategory.OST_PipeAccessory;
                case "Pipe Insulations":      return BuiltInCategory.OST_PipeInsulations;
                case "Pipe Systems":          return BuiltInCategory.OST_PipingSystem;
                case "Plumbing Fixtures":     return BuiltInCategory.OST_PipeCurves; // closest valid
                case "Flex Pipes":            return BuiltInCategory.OST_FlexPipeCurves;
                case "Sprinklers":            return BuiltInCategory.OST_Sprinklers;
                case "Ducts":                 return BuiltInCategory.OST_DuctCurves;
                case "Duct Fittings":         return BuiltInCategory.OST_DuctFitting;
                case "Duct Accessories":       return BuiltInCategory.OST_DuctAccessory;
                case "Duct Insulations":      return BuiltInCategory.OST_DuctInsulations;
                case "Duct Systems":          return BuiltInCategory.OST_DuctSystem;
                case "Flex Ducts":            return BuiltInCategory.OST_FlexDuctCurves;
                case "Air Terminals":          return BuiltInCategory.OST_DuctTerminal;
                case "Mechanical Equipment":  return BuiltInCategory.OST_MechanicalEquipment;
                case "Electrical Equipment":  return BuiltInCategory.OST_ElectricalEquipment;
                case "Electrical Fixtures":   return BuiltInCategory.OST_ElectricalFixtures;
                case "Conduits":              return BuiltInCategory.OST_Conduit;
                case "Conduit Fittings":      return BuiltInCategory.OST_ConduitFitting;
                case "Cable Trays":           return BuiltInCategory.OST_CableTray;
                case "Cable Tray Fittings":   return BuiltInCategory.OST_CableTrayFitting;
                case "Electrical Circuits":   return BuiltInCategory.OST_ElectricalCircuit;
                case "Lighting Fixtures":     return BuiltInCategory.OST_LightingFixtures;
                case "Lighting Devices":      return BuiltInCategory.OST_LightingDevices;
                case "Communication Devices": return BuiltInCategory.OST_CommunicationDevices;
                case "Data Devices":          return BuiltInCategory.OST_DataDevices;
                case "Nurse Call Devices":    return BuiltInCategory.OST_NurseCallDevices;
                case "Telephone Devices":     return BuiltInCategory.OST_TelephoneDevices;
                case "Security Devices":      return BuiltInCategory.OST_SecurityDevices;
                case "Fire Alarm Devices":    return BuiltInCategory.OST_FireAlarmDevices;
                default:                      return BuiltInCategory.INVALID;
            }
        }
    }
}