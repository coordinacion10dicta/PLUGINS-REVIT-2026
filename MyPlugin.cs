using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

public class MyPlugin : IExternalCommand
{
    // Este método se ejecutará cuando invoques el comando en Revit.
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Muestra un mensaje en Revit para verificar que el plugin funciona.
        TaskDialog.Show("Mi Plugin", "¡Hola! Mi plugin de Revit está funcionando.");
        
        // Indica a Revit que el comando se ejecutó correctamente
        return Result.Succeeded;
    }
}
