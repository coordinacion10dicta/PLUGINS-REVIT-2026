using System;
using Autodesk.Revit.DB;

namespace MiNamespace.ValidadorParametros
{
    // Determina si un parámetro es de instancia (elemento) o de tipo (símbolo/tipo).
    public static class InstanceParameterHelper
    {
        public static bool IsInstanceParameter(Element element, string parameterName)
        {
            if (element == null) return false;

            Parameter param = element.LookupParameter(parameterName);
            if (param == null)
                param = GetParameterByName(element, parameterName);

            if (param == null) return false;

            BuiltInParameter bip = param.Definition.BuiltInParameter;
            return !IsTypeParameter(bip);
        }

        private static bool IsTypeParameter(BuiltInParameter bip)
        {
            // Parámetros típicamente de tipo (no de instancia)
            return bip == BuiltInParameter.ELEM_TYPE_PARAM
                || bip == BuiltInParameter.SYMBOL_NAME_PARAM
                || bip == BuiltInParameter.FAMILY_NAME_PARAM;
        }

        private static Parameter GetParameterByName(Element element, string name)
        {
            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }
    }
}
