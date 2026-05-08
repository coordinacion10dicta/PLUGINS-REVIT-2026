namespace PluginCotasExteriores.Commands
{
    using System;
    using System.Linq;
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using Body;
    using Configurations;
    using Helpers;
    using View;
    using Work;

    [Transaction(global::Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Regeneration(global::Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class PluginCotasExterioresCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                
                // Check that we are on a plan view
                var view = doc.ActiveView;
                var isViewPlan = view is ViewPlan;
                if (isViewPlan)
                {
                    var configuration = GetExteriorConfiguration();
                    if (configuration != null)
                    {
                        var insertExteriorDimensions =
                            new InsertExteriorDimensions(configuration, commandData.Application);
                        insertExteriorDimensions.DoWork();
                    }
                    else
                    {
                        var result = TaskDialog.Show(
                            "Cotas",
                            "No se encontró una configuración activa.\n¿Desea abrir las configuraciones?",
                            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                        if (result == TaskDialogResult.Yes)
                        {
                            var settings = new SettingsWindow();
                            settings.ShowDialog();
                        }
                    }
                }
                else
                {
                    TaskDialog.Show("Cotas",
                        "Este comando solo funciona en vistas de planta (ViewPlan).");
                }

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception exception)
            {
                TaskDialog.Show("Error", exception.Message + "\n\n" + exception.StackTrace);
                return Result.Failed;
            }
        }

        private ExteriorConfiguration GetExteriorConfiguration()
        {
            try
            {
                var defConfigStr = UserSettings.GetValue("DefaultExteriorConfiguration");
                var defConfig = Guid.TryParse(defConfigStr, out var g) ? g : Guid.Empty;
                if (defConfig == Guid.Empty)
                    return null;

                var exteriorConfigurations = SettingsFile.LoadExteriorConfigurations();
                
                foreach (var configuration in exteriorConfigurations)
                {
                    if (configuration.Id.Equals(defConfig))
                        return configuration;
                }

                if (exteriorConfigurations.Any())
                    return exteriorConfigurations[0];
                return null;
            }
            catch (Exception exception)
            {
                TaskDialog.Show("Error", exception.Message);
                return null;
            }
        }
    }
}
