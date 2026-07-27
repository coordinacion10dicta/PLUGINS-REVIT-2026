using System;
using Autodesk.Revit.DB;

namespace MiNamespace.ValidadorParametros
{
    // Determina si un parámetro es de instancia (elemento) o de tipo (símbolo/tipo).
    public static class InstanceParameterHelper
    {
        public static bool IsInstanceParameter(Element element, string parameterName)
        {
            if (element == null || !element.IsValidObject) return false;

            try
            {
                Parameter param = element.LookupParameter(parameterName);
                if (param != null)
                {
                    var internalDef = param.Definition as InternalDefinition;
                    BuiltInParameter bip = internalDef?.BuiltInParameter ?? BuiltInParameter.INVALID;
                    return !IsTypeOnlyBuiltIn(bip);
                }

                Element elemType = element.Document.GetElement(element.GetTypeId());
                if (elemType != null && elemType.IsValidObject)
                {
                    Parameter typeParam = elemType.LookupParameter(parameterName);
                    if (typeParam != null)
                    {
                        var internalDef = typeParam.Definition as InternalDefinition;
                        BuiltInParameter bip = internalDef?.BuiltInParameter ?? BuiltInParameter.INVALID;
                        return !IsTypeOnlyBuiltIn(bip);
                    }
                }

                return false;
            }
            catch { return false; }
        }

        private static bool IsTypeOnlyBuiltIn(BuiltInParameter bip)
        {
            return bip == BuiltInParameter.ELEM_TYPE_PARAM
                || bip == BuiltInParameter.SYMBOL_NAME_PARAM;
        }

        private static Parameter GetParameterByName(Element element, string name)
        {
            if (element == null || !element.IsValidObject) return null;
            try
            {
                foreach (Parameter p in element.Parameters)
                {
                    try
                    {
                        if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return p;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
