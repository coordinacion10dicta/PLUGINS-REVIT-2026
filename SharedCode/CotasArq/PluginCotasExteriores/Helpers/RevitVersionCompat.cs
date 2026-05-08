namespace PluginCotasExteriores.Helpers
{
    using Autodesk.Revit.DB;

    internal static class RevitVersionCompat
    {
        public static long GetValueCompat(this ElementId elementId)
        {
#if REVIT_LEGACY_ELEMENTID
            return elementId.IntegerValue;
#else
            return elementId.Value;
#endif
        }
    }
}
