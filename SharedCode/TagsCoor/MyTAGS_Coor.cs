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

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;


namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAG_Coor : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeView = doc.ActiveView;

            if (activeView == null)
            {
                TaskDialog.Show("MyTAG_Coor", "No hay una vista activa.");
                return Result.Cancelled;
            }

            // Menú principal
            TaskDialog td = new TaskDialog("TAG COOR");
            td.MainInstruction = "¿Qué deseas taguear en la vista actual?";
            td.MainContent = "Se taguearán todos los elementos visibles en la vista activa.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Cable Trays");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conduits");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Ducts");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Pipes");
            td.CommonButtons = TaskDialogCommonButtons.Close;
            td.DefaultButton = TaskDialogResult.Close;

            TaskDialogResult result = td.Show();

            try
            {
                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        TagElementsOfCategory(doc, activeView,
                            BuiltInCategory.OST_CableTray, "Cable Trays");
                        break;

                    case TaskDialogResult.CommandLink2:
                        TagElementsOfCategory(doc, activeView,
                            BuiltInCategory.OST_Conduit, "Conduits");
                        break;

                    case TaskDialogResult.CommandLink3:
                        TagElementsOfCategory(doc, activeView,
                            BuiltInCategory.OST_DuctCurves, "Ducts");
                        break;

                    case TaskDialogResult.CommandLink4:
                        TagElementsOfCategory(doc, activeView,
                            BuiltInCategory.OST_PipeCurves, "Pipes");
                        break;

                    default:
                        return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Taguea todos los elementos de una categoría en la vista activa.
        /// </summary>
        private void TagElementsOfCategory(
            Document doc,
            View view,
            BuiltInCategory category,
            string friendlyName)
        {
            // Solo elementos visibles en la vista actual
            FilteredElementCollector collector =
                new FilteredElementCollector(doc, view.Id)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            IList<Element> elems = collector.ToList();

            if (elems == null || elems.Count == 0)
            {
                TaskDialog.Show("MyTAG_Coor",
                    $"No se encontraron {friendlyName} visibles en la vista actual.");
                return;
            }

            int createdTags = 0;

            using (Transaction t = new Transaction(doc,
                $"Tag {friendlyName} en vista {view.Name}"))
            {
                t.Start();

                foreach (Element e in elems)
                {
                    try
                    {
                        XYZ tagPoint = GetElementCenterInView(e, view);
                        if (tagPoint == null)
                            continue;

                        Reference reference = new Reference(e);

                        // Crea el tag usando el tipo de tag por defecto para la categoría
                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            view.Id,
                            reference,
                            false, // hasLeader
                            TagMode.TM_ADDBY_CATEGORY,
                            TagOrientation.Horizontal,
                            tagPoint);

                        if (newTag != null)
                            createdTags++;
                    }
                    catch
                    {
                        // Si falla un elemento, continuamos con el resto
                        continue;
                    }
                }

                t.Commit();
            }

            TaskDialog.Show("MyTAG_Coor",
                $"Se crearon {createdTags} tags para {friendlyName} en la vista \"{view.Name}\".");
        }

        /// <summary>
        /// Calcula un punto "central" del elemento en la vista para posicionar el tag.
        /// Primero intenta usar LocationCurve, si no, usa el BoundingBox en la vista.
        /// </summary>
        private XYZ GetElementCenterInView(Element elem, View view)
        {
            LocationCurve locCurve = elem.Location as LocationCurve;
            if (locCurve != null && locCurve.Curve != null)
            {
                Curve c = locCurve.Curve;
                XYZ p0 = c.GetEndPoint(0);
                XYZ p1 = c.GetEndPoint(1);
                return (p0 + p1) / 2.0;
            }

            BoundingBoxXYZ bb = elem.get_BoundingBox(view);
            if (bb != null)
            {
                return (bb.Min + bb.Max) / 2.0;
            }

            return null;
        }
    }
}