using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_ORG_EJES : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View activeView = doc.ActiveView;

            // 1. Detectar si hay selección previa de ejes
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            IList<Element> gridElements = new List<Element>();
            bool hasPreSelection = false;

            if (selectedIds.Count > 0)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element el = doc.GetElement(id);
                    if (el is Grid)
                    {
                        gridElements.Add(el);
                    }
                }
            }

            if (gridElements.Count > 0)
            {
                hasPreSelection = true;
            }
            else
            {
                // Si no hay ejes seleccionados, tomar todos los de la vista activa
                FilteredElementCollector collector = new FilteredElementCollector(doc, activeView.Id);
                gridElements = collector.OfCategory(BuiltInCategory.OST_Grids).WhereElementIsNotElementType().ToElements();
            }

            if (gridElements.Count == 0)
            {
                TaskDialog.Show("ORG.EJES", "No se encontraron ejes (Grids) visibles o seleccionados.");
                return Result.Cancelled;
            }

            List<Grid> horizontalGrids;
            List<Grid> verticalGrids;
            ClassifyGrids(gridElements, activeView, out horizontalGrids, out verticalGrids);

            // Show UI
            using (OrgEjesWindow window = new OrgEjesWindow(hasPreSelection))
            {
                if (window.ShowDialog() == DialogResult.OK)
                {
                    // NUEVO
                    if (window.UseSpecificSelection)
                    {
                        try
                        {
                            IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                                ObjectType.Element,
                                new GridSelectionFilter(),
                                "Selecciona los ejes a organizar y presiona Finalizar");

                            IList<Element> pickedGrids = pickedRefs
                                .Select(r => doc.GetElement(r))
                                .Where(e => e is Grid)
                                .ToList();

                            if (pickedGrids.Count == 0)
                            {
                                TaskDialog.Show("ORG.EJES", "No se seleccionó ningún eje.");
                                return Result.Cancelled;
                            }

                            ClassifyGrids(pickedGrids, activeView, out horizontalGrids, out verticalGrids);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            return Result.Cancelled;
                        }
                    }

                    using (Transaction t = new Transaction(doc, "Organizar Ejes"))
                    {
                        t.Start();

                        try
                        {
                            bool renameHorizontal = !string.IsNullOrEmpty(window.SequenceHorizontal);
                            bool renameVertical = !string.IsNullOrEmpty(window.SequenceVertical);

                            // 1. Ordenar los ejes ANTES de hacer cualquier modificación
                            // 1. Ordenar los ejes ANTES de hacer cualquier modificación
                            if (renameHorizontal)
                            {
                                horizontalGrids = window.IsHorizontalAscending
                                    ? horizontalGrids.OrderBy(g => GetScreenY(g, activeView)).ToList()
                                    : horizontalGrids.OrderByDescending(g => GetScreenY(g, activeView)).ToList();
                            }

                            if (renameVertical)
                            {
                                verticalGrids = window.IsVerticalAscending
                                    ? verticalGrids.OrderBy(g => GetScreenX(g, activeView)).ToList()
                                    : verticalGrids.OrderByDescending(g => GetScreenX(g, activeView)).ToList();
                            }

                            // 2. Renombrar a nombres temporales SOLO los ejes que se van a renombrar (Fase 1)
                            int tempIndex = 1;
                            string sessionGuid = Guid.NewGuid().ToString("N").Substring(0, 8);

                            IEnumerable<Grid> gridsToRename = Enumerable.Empty<Grid>();

                            if (renameHorizontal)
                                gridsToRename = gridsToRename.Concat(horizontalGrids);

                            if (renameVertical)
                                gridsToRename = gridsToRename.Concat(verticalGrids);

                            foreach (Grid g in gridsToRename)
                            {
                                g.Name = $"TEMP_{sessionGuid}_{tempIndex}";
                                tempIndex++;
                            }
                            // 3. Asignar los nombres finales (Fase 2)
                            // Formato: Inicio (estático) + Secuencia (se incrementa)
                            // Ejemplo: Inicio="A", Secuencia="1" → A1, A2, A3, A4...
                            // Ejemplo: Inicio="" , Secuencia="A" → A, B, C, D...

                            // --- Ejes Horizontales ---
                            if (renameHorizontal)
                            {
                                string prefijo = window.StartHorizontal.Trim();
                                string currentHorizSeq = window.SequenceHorizontal;

                                foreach (Grid g in horizontalGrids)
                                {
                                    g.Name = prefijo + currentHorizSeq;
                                    currentHorizSeq = GetNextInSequence(currentHorizSeq);
                                }
                            }

                            // --- Ejes Verticales ---
                            if (renameVertical)
                            {
                                string prefijoVert = window.StartVertical.Trim();
                                string currentVertSeq = window.SequenceVertical;

                                foreach (Grid g in verticalGrids)
                                {
                                    g.Name = prefijoVert + currentVertSeq;
                                    currentVertSeq = GetNextInSequence(currentVertSeq);
                                }
                            }
                            t.Commit();

                        // NUEVO - Aviso de confirmación
                        TaskDialog.Show("ORG.EJES", "Los ejes se organizaron correctamente.");
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            TaskDialog.Show("Error", "Ocurrió un error al organizar los ejes: " + ex.Message);
                            return Result.Failed;
                        }
                    }
                }
                else
                {
                    return Result.Cancelled;
                }
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Incrementa el valor de secuencia. Si es número, suma 1.
        /// Si es texto (letras), incrementa alfabéticamente (A→B, Z→AA).
        /// </summary>
        private string GetNextInSequence(string current)
        {
            // Números
            if (int.TryParse(current, out int num))
                return (num + 1).ToString();

            // Letras
            return IncrementString(current);
        }

        private string IncrementString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "A";
            
            char[] chars = s.ToCharArray();
            int i = chars.Length - 1;

            while (i >= 0)
            {
                if (chars[i] == 'Z')
                {
                    chars[i] = 'A';
                    i--;
                }
                else if (chars[i] == 'z')
                {
                    chars[i] = 'a';
                    i--;
                }
                else if (char.IsLetter(chars[i]))
                {
                    chars[i] = (char)(chars[i] + 1);
                    return new string(chars);
                }
                else
                {
                    return s + "1"; // Fallback para alfanuméricos mixtos no previstos
                }
            }

            if (char.IsUpper(s[0]))
            {
                return "A" + new string(chars);
            }
            else
            {
                return "a" + new string(chars);
            }
        }

        private double GetScreenX(Grid g, Autodesk.Revit.DB.View view)
        {
            return g.Curve.GetEndPoint(0).DotProduct(view.RightDirection);
        }

        private double GetScreenY(Grid g, Autodesk.Revit.DB.View view)
        {
            return g.Curve.GetEndPoint(0).DotProduct(view.UpDirection);
        }

        //private void ClassifyGrids(IList<Element> elements, out List<Grid> horizontalGrids, out List<Grid> verticalGrids)
        private void ClassifyGrids(IList<Element> elements, Autodesk.Revit.DB.View view, out List<Grid> horizontalGrids, out List<Grid> verticalGrids)
        {
            horizontalGrids = new List<Grid>();
            verticalGrids = new List<Grid>();

            XYZ right = view.RightDirection.Normalize(); // eje horizontal EN PANTALLA
            XYZ up = view.UpDirection.Normalize();        // eje vertical EN PANTALLA

            foreach (Element elem in elements)
            {
                Grid grid = elem as Grid;
                if (grid == null) continue;

                Curve curve = grid.Curve;
                if (curve is Line line)
                {
                    XYZ direction = line.Direction;

                    double compRight = Math.Abs(direction.DotProduct(right));
                    double compUp = Math.Abs(direction.DotProduct(up));

                    // La línea corre principalmente a lo largo de "Right" -> es un eje horizontal en pantalla
                    if (compUp < 0.01)
                        horizontalGrids.Add(grid);
                    // La línea corre principalmente a lo largo de "Up" -> es un eje vertical en pantalla
                    else if (compRight < 0.01)
                        verticalGrids.Add(grid);
                }
            }
        }
    }

    // NUEVO
    public class GridSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Grid;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

}

