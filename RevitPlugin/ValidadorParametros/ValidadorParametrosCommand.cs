using System;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MiNamespace.ValidadorParametros
{
    [Transaction(TransactionMode.Manual)]
    public class ValidadorParametrosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;

                if (uiApp.ActiveUIDocument == null)
                {
                    message = "No hay un documento activo.";
                    return Result.Failed;
                }

                string dllDir   = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string jsonPath = Path.Combine(dllDir, "disciplinas_config.json");
                DisciplinasRepository.Initialize(jsonPath);

                var handler      = new ValidacionEventHandler();
                var extEvent     = ExternalEvent.Create(handler);

                var agregarHandler = new AgregarParametroEventHandler { DllDir = dllDir };
                var agregarEvent   = ExternalEvent.Create(agregarHandler);

                var window = new UI.UiValidadorParametros(
                    uiApp.ActiveUIDocument.Document,
                    handler,   extEvent,
                    agregarHandler, agregarEvent);

                // Ancla la ventana al proceso de Revit para que no quede detrás
                var helper = new WindowInteropHelper(window);
                helper.Owner = uiApp.MainWindowHandle;

                // Show() — modeless: Execute() retorna, la ventana permanece abierta
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
