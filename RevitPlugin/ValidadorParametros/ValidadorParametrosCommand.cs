using System;
using System.IO;
using System.Reflection;
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
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    message = "No hay un documento activo.";
                    return Result.Failed;
                }

                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string defaultExcelPath = Path.Combine(dllDir, "ParametrosRequeridos.xlsx");

                var window = new UI.UiValidadorParametros(uidoc.Document, defaultExcelPath);
                window.ShowDialog();

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
