using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_ORG_EJES : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
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

            List<Grid> horizontalGrids = new List<Grid>();
            List<Grid> verticalGrids = new List<Grid>();

            foreach (Element elem in gridElements)
            {
                Grid grid = elem as Grid;
                if (grid == null) continue;

                Curve curve = grid.Curve;
                if (curve is Line line)
                {
                    XYZ direction = line.Direction;

                    // Check if it's horizontal (parallel to X axis)
                    if (Math.Abs(direction.Y) < 0.001 && Math.Abs(direction.Z) < 0.001)
                    {
                        horizontalGrids.Add(grid);
                    }
                    // Check if it's vertical (parallel to Y axis)
                    else if (Math.Abs(direction.X) < 0.001 && Math.Abs(direction.Z) < 0.001)
                    {
                        verticalGrids.Add(grid);
                    }
                }
            }

            // Show UI
            using (OrgEjesWindow window = new OrgEjesWindow(hasPreSelection))
            {
                if (window.ShowDialog() == DialogResult.OK)
                {
                    using (Transaction t = new Transaction(doc, "Organizar Ejes"))
                    {
                        t.Start();

                        try
                        {
                            bool renameHorizontal = !string.IsNullOrEmpty(window.SequenceHorizontal);
                            bool renameVertical = !string.IsNullOrEmpty(window.SequenceVertical);

                            // 1. Ordenar los ejes ANTES de hacer cualquier modificación
                            if (renameHorizontal)
                            {
                                if (window.IsHorizontalAscending)
                                {
                                    horizontalGrids = horizontalGrids.OrderBy(g => g.Curve.GetEndPoint(0).Y).ToList();
                                }
                                else
                                {
                                    horizontalGrids = horizontalGrids.OrderByDescending(g => g.Curve.GetEndPoint(0).Y).ToList();
                                }
                            }

                            if (renameVertical)
                            {
                                if (window.IsVerticalAscending)
                                {
                                    verticalGrids = verticalGrids.OrderBy(g => g.Curve.GetEndPoint(0).Y).ToList();
                                }
                                else
                                {
                                    verticalGrids = verticalGrids.OrderByDescending(g => g.Curve.GetEndPoint(0).Y).ToList();
                                }
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

    }
}
