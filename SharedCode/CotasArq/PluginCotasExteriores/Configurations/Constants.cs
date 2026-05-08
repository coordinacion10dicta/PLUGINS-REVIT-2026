namespace PluginCotasExteriores.Configurations
{
    using System.Collections.Generic;

    public class Constants
    {
#pragma warning disable SA1600
#pragma warning disable SA1310
        public const string XElementName_Root = "Configurations";
        public const string XElementName_Chain = "Chain";
        public const string XElementName_ExteriorConfiguration = "ExteriorConfiguration";
        public const string XElementName_ExteriorConfigurations = "ExteriorConfigurations";
#pragma warning restore SA1310
#pragma warning restore SA1600

        static Constants()
        {
            ElementOffsets = new List<int>();
            for (var i = 1; i <= 50; i++)
            {
                ElementOffsets.Add(1 * i);
            }
        }

        /// <summary>Valores de desplazamiento permitidos en mm</summary>
        public static List<int> ElementOffsets;
    }
}
