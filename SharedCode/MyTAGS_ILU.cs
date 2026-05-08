#if REVIT2020
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2021
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2022
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2023
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2024
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2025
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif

// Espacios de nombres necesarios para funcionalidades del sistema, Revit y la interfaz gráfica
using System.Linq; //LINQ(Language Integrated Query) es una característica que permite realizar consultas sobre colecciones de datos de manera sencilla y eficiente.
using Autodesk.Revit.Attributes;// Permite usar atributos como [Transaction]
using Autodesk.Revit.DB;// Acceso a la base de datos de Revit (Elementos, Document, etc.)
using Autodesk.Revit.UI;// Permite crear comandos externos y mostrar ventanas como TaskDialog
using System;
using System.Collections.Generic;
using System.Windows.Forms; // Para mostrar ventanas de Windows Forms
//using RevitPlugin; // Namespace donde está definida la ventana personalizada de selección de tag
using Autodesk.Revit.UI;


namespace MiNamespace
{
    //Indica cuándo se inicia y se finaliza una transacción.
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_ILU : IExternalCommand
    {
        // Método principal que se ejecuta cuando se llama el plugin desde Revit
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Referencias al documento activo de Revit
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Obtener todos los tipos de tags disponibles en el modelo para las luminarias
            var availableTags = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_LightingFixtureTags)  // Categoriza el tag necesario en este caso, tag de luminarias
                .Cast<FamilySymbol>()
                .ToList();

            ////Obtiene todos los tags cargados
            //var availableTags = new FilteredElementCollector(doc)
            //   .WhereElementIsElementType()
            //   .OfClass(typeof(FamilySymbol))
            //   .Cast<FamilySymbol>()
            //   .Where(fs => fs.Category != null && fs.Category.Name.ToLower().Contains("tag"))
            //   .ToList();

            // Si no hay ningún tag cargado, mostrar error y terminar
            if (!availableTags.Any())
            {
                TaskDialog.Show("Error", "There are no lighting fixture tag types in the model.");
                return Result.Failed;
            }

            // Obtener los nombres de los tipos de tags encontrados para mostrarlos en la ventana
            List<string> tagNames = availableTags.Select(t => t.Name).ToList();

            // Mostrar la ventana para selección alguno de los tags cargados
            TagSelectionWindow tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != DialogResult.OK)
            {
                TaskDialog.Show("Cancelled", "No tag was selected.");
                return Result.Cancelled;
            }

            // Obtener el nombre del tag seleccionado por el usuario
            string selectedTag = tagWindow.SelectedTagType;

            // Buscar el tipo de tag correspondiente en la lista de tags disponibles
            FamilySymbol tagType = availableTags.FirstOrDefault(t => t.Name == selectedTag);

            // Condicional si no se encuentra el tipo de tag seleccionado, mostrar error
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"The tag type '{selectedTag}' was not found.");
                return Result.Failed;
            }

            // Se obtiene todas las luminarias visibles en la vista activa
            var luces = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .Cast<FamilyInstance>()
                .ToList();

            // Condicional para mostrar aviso de si no hay luminarias
            if (!luces.Any())
            {
                TaskDialog.Show("Warning", "There are no lighting fixtures in the active view to tag.");
                return Result.Cancelled;
            }
            // Iniciar una transacción para hacer cambios en el modelo
            using (Transaction tx = new Transaction(doc, "Tag Lighting Fixtures"))
            {
                tx.Start();

                // Condicional para activar el tipo de tag si no está activado
                if (!tagType.IsActive)
                {
                    tagType.Activate();
                    doc.Regenerate(); // Actualiza el modelo para reflejar el cambio
                }

                // Obtener dirección del "lado derecho" de la vista (para mover horizontalmente)
                XYZ rightDir = uidoc.ActiveView.RightDirection;
                // Obtener dirección "hacia abajo" de la vista (para mover verticalmente)
                XYZ downDir = -uidoc.ActiveView.UpDirection;

                // Recorrer cada luminaria encontrada
                foreach (var luz in luces)
                {
                    // Asegurarse de que la luminaria tiene una ubicación puntual
                    if (luz.Location is LocationPoint location)
                    {
                        Reference luzRef = new Reference(luz);      // Crear referencia a la luminaria
                        XYZ luzPos = location.Point;                // Obtener posición de la luminaria

                        // Calcular desplazamiento adaptable: 1m a la derecha y 0.5m hacia abajo, relativo a la vista
                        XYZ offset = (rightDir * 1.0) + (downDir * 0.5);
                        XYZ tagHeadPos = luzPos + offset;

                        // Crear el tag con la posición desplazada
                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            tagType.Id,
                            uidoc.ActiveView.Id,
                            luzRef,
                            false,
                            TagOrientation.Horizontal,
                            tagHeadPos);

                        // Configurar el tag para que tenga una línea de líder (como en la luminaria 1)
                        newTag.HasLeader = true;
                        newTag.LeaderEndCondition = LeaderEndCondition.Free;
                        newTag.TagHeadPosition = tagHeadPos;
                    }


                    // Si quieres mostrarla o incluirla como parámetro en el tag, aquí puedes usar luminariaAltura

                }
                // Confirmar todos los cambios en el modelo
                tx.Commit();
            }
            // Mostrar mensaje final cuando termina correctamente
            TaskDialog.Show("Completed", "Lighting fixtures were successfully tagged.");
            return Result.Succeeded;
        }
    }
}


