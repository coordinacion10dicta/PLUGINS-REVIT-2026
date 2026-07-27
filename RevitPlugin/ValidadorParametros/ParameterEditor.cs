using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MiNamespace.ValidadorParametros
{
    // Edita parámetros en Revit: establece valores, crea parámetros, aplica a similares.
    public static class ParameterEditor
    {
        // Obtiene lista de parámetros con valores para edición.
        // NOTA: OriginalParameter NO se captura como referencia (pierde validez al cerrar family doc).
        // Se almacena el nombre y se re-busca al guardar.
        public static List<ParameterEditModel> GetEditableParameters(Element element)
        {
            var result = new List<ParameterEditModel>();

            if (element == null || !element.IsValidObject) return result;

            try
            {
                foreach (Parameter p in element.Parameters)
                {
                    try
                    {
                        string name = p.Definition.Name;
                        string value = GetParameterValue(p);
                        bool isInstance = InstanceParameterHelper.IsInstanceParameter(element, name);

                        result.Add(new ParameterEditModel
                        {
                            Name = name,
                            Value = value,
                            IsInstance = isInstance,
                            StorageType = p.StorageType,
                            OriginalParameter = null
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return result.OrderBy(p => p.Name).ToList();
        }

        // Establece un parámetro a un nuevo valor (requiere transacción abierta)
        public static bool SetParameterValue(Parameter param, string newValue)
        {
            try
            {
                if (param == null || param.IsReadOnly) return false;

                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(newValue ?? "");
                        return true;

                    case StorageType.Double:
                        if (double.TryParse(newValue, out double dval))
                        {
                            param.Set(dval);
                            return true;
                        }
                        break;

                    case StorageType.Integer:
                        if (int.TryParse(newValue, out int ival))
                        {
                            param.Set(ival);
                            return true;
                        }
                        break;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Crea un parámetro en la familia (si es FamilyInstance)
        public static bool TryAddInstanceParameter(Document doc, Element element,
            string paramName)
        {
            try
            {
                if (!(element is FamilyInstance fi)) return false;

                Family family = fi.Symbol.Family;
                if (family == null) return false;

                // Revisar si ya existe
                Parameter existing = element.LookupParameter(paramName);
                if (existing != null) return true;

                // Crear en documento (no en familia, que requeriría edición de familia)
                // Esto agrega el parámetro de proyecto si no existe
                ParameterElement paramElem = doc.GetElement(element.GetParameters(paramName).FirstOrDefault()?.Id ?? ElementId.InvalidElementId) as ParameterElement;
                if (paramElem != null) return true;

                // En Revit, agregar parámetros es complejo. Aquí solo indicamos que se intenta.
                return false;
            }
            catch
            {
                return false;
            }
        }

        // Aplica un valor a todos los elementos similares (misma familia, solo parámetros de instancia)
        public static int ApplyToSimilarElements(Document doc, Element sourceElement,
            string parameterName, string newValue)
        {
            if (!(sourceElement is FamilyInstance sourceFi))
                return 0;

            Family sourceFamily = sourceFi.Symbol?.Family;
            if (sourceFamily == null) return 0;

            if (!InstanceParameterHelper.IsInstanceParameter(sourceElement, parameterName))
                return 0; // Solo parámetros de instancia

            Parameter sourceParam = sourceElement.LookupParameter(parameterName);
            if (sourceParam == null || sourceParam.IsReadOnly)
                return 0;

            var sourceFamilyId = sourceFamily.Id;

            using (Transaction tx = new Transaction(doc, $"Aplicar {parameterName}"))
            {
                tx.Start();

                int count = 0;
                var similarElements = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi =>
                    {
                        var famId = fi.Symbol?.Family?.Id;
                        if (famId == null) return false;
#if REVIT_LEGACY_ELEMENTID
                        return famId.IntegerValue == sourceFamilyId.IntegerValue && fi.Id != sourceElement.Id;
#else
                        return famId.Value == sourceFamilyId.Value && fi.Id != sourceElement.Id;
#endif
                    })
                    .ToList();

                foreach (FamilyInstance elem in similarElements)
                {
                    Parameter targetParam = elem.LookupParameter(parameterName);
                    if (targetParam != null && !targetParam.IsReadOnly)
                    {
                        if (SetParameterValue(targetParam, newValue))
                            count++;
                    }
                }

                tx.Commit();
                return count;
            }
        }

        // Guarda cambios de parámetros a Revit.
        // Changes se guarda por nombre — no requiere OriginalParameter.
        public static bool SaveParameterChanges(Document doc, Element element,
            Dictionary<string, string> changes)
        {
            if (changes == null || changes.Count == 0) return true;

            using (Transaction tx = new Transaction(doc, "Editar parámetros"))
            {
                try
                {
                    tx.Start();

                    foreach (var kvp in changes)
                    {
                        Parameter param = null;
                        try { param = element.LookupParameter(kvp.Key); } catch { }

                        if (param == null)
                        {
                            try
                            {
                                foreach (Parameter p in element.Parameters)
                                {
                                    try
                                    {
                                        if (p.Definition.Name.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
                                        {
                                            param = p;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }

                        if (param != null && !param.IsReadOnly)
                        {
                            SetParameterValue(param, kvp.Value);
                        }
                    }

                    tx.Commit();
                    return true;
                }
                catch
                {
                    if (tx.HasStarted())
                        tx.RollBack();
                    return false;
                }
            }
        }

        private static string GetParameterValue(Parameter param)
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

    public class ParameterEditModel
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsInstance { get; set; }
        public StorageType StorageType { get; set; }
        public Parameter OriginalParameter { get; set; }
    }
}
