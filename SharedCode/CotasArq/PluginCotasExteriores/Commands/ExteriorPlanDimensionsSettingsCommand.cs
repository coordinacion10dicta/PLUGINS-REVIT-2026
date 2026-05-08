namespace PluginCotasExteriores.Commands
{
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using View;

    [Transaction(global::Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Regeneration(global::Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class PluginCotasExterioresSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                SettingsWindow settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Error - Configuraciones", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
