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
                    if (el is Grid || el is MultiSegmentGrid)
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

            List<GridItem> gridItems = GetUniqueGridItems(gridElements, doc);

            if (gridItems.Count == 0)
            {
                TaskDialog.Show("ORG.EJES", "No se encontraron ejes (Grids) visibles o seleccionados.");
                return Result.Cancelled;
            }

            List<GridItem> horizontalGrids;
            List<GridItem> verticalGrids;
            ClassifyGrids(gridItems, activeView, out horizontalGrids, out verticalGrids);

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
                                .Where(e => e is Grid || e is MultiSegmentGrid)
                                .ToList();

                            if (pickedGrids.Count == 0)
                            {
                                TaskDialog.Show("ORG.EJES", "No se seleccionó ningún eje.");
                                return Result.Cancelled;
                            }

                            gridItems = GetUniqueGridItems(pickedGrids, doc);
                            ClassifyGrids(gridItems, activeView, out horizontalGrids, out verticalGrids);
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
                            if (renameHorizontal)
                            {
                                horizontalGrids = window.IsHorizontalAscending
                                    ? horizontalGrids.OrderBy(g => GetScreenYForHorizontal(g, activeView)).ToList()
                                    : horizontalGrids.OrderByDescending(g => GetScreenYForHorizontal(g, activeView)).ToList();
                            }

                            if (renameVertical)
                            {
                                verticalGrids = window.IsVerticalAscending
                                    ? verticalGrids.OrderBy(g => GetScreenXForVertical(g, activeView)).ToList()
                                    : verticalGrids.OrderByDescending(g => GetScreenXForVertical(g, activeView)).ToList();
                            }

                            // 2. Renombrar a nombres temporales SOLO los ejes que se van a renombrar (Fase 1)
                            int tempIndex = 1;
                            string sessionGuid = Guid.NewGuid().ToString("N").Substring(0, 8);

                            IEnumerable<GridItem> gridsToRename = Enumerable.Empty<GridItem>();

                            if (renameHorizontal)
                                gridsToRename = gridsToRename.Concat(horizontalGrids);

                            if (renameVertical)
                                gridsToRename = gridsToRename.Concat(verticalGrids);

                            foreach (GridItem item in gridsToRename)
                            {
                                item.Element.Name = $"TEMP_{sessionGuid}_{tempIndex}";
                                tempIndex++;
                            }

                            // 3. Asignar los nombres finales (Fase 2)
                            // --- Ejes Horizontales ---
                            if (renameHorizontal)
                            {
                                string prefijo = window.StartHorizontal.Trim();
                                string currentHorizSeq = window.SequenceHorizontal;

                                foreach (GridItem item in horizontalGrids)
                                {
                                    item.Element.Name = currentHorizSeq + prefijo;
                                    currentHorizSeq = GetNextInSequence(currentHorizSeq);
                                }
                            }

                            // --- Ejes Verticales ---
                            if (renameVertical)
                            {
                                string prefijoVert = window.StartVertical.Trim();
                                string currentVertSeq = window.SequenceVertical;

                                foreach (GridItem item in verticalGrids)
                                {
                                    item.Element.Name = currentVertSeq + prefijoVert;
                                    currentVertSeq = GetNextInSequence(currentVertSeq);
                                }
                            }
                            t.Commit();

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

        private List<GridItem> GetUniqueGridItems(IList<Element> rawElements, Document doc)
        {
            Dictionary<ElementId, ElementId> childToParentMap = new Dictionary<ElementId, ElementId>();
            try
            {
                FilteredElementCollector msgCollector = new FilteredElementCollector(doc).OfClass(typeof(MultiSegmentGrid));
                foreach (MultiSegmentGrid msg in msgCollector)
                {
                    ICollection<ElementId> subIds = msg.GetGridIds();
                    if (subIds != null)
                    {
                        foreach (ElementId subId in subIds)
                        {
                            childToParentMap[subId] = msg.Id;
                        }
                    }
                }
            }
            catch { }

            Dictionary<ElementId, Element> uniqueElements = new Dictionary<ElementId, Element>();

            foreach (Element elem in rawElements)
            {
                if (elem is MultiSegmentGrid msg)
                {
                    if (!uniqueElements.ContainsKey(msg.Id))
                        uniqueElements[msg.Id] = msg;
                }
                else if (elem is Grid g)
                {
                    if (childToParentMap.TryGetValue(g.Id, out ElementId parentId))
                    {
                        Element parent = doc.GetElement(parentId);
                        if (parent != null)
                        {
                            if (!uniqueElements.ContainsKey(parent.Id))
                                uniqueElements[parent.Id] = parent;
                        }
                        else if (!uniqueElements.ContainsKey(g.Id))
                        {
                            uniqueElements[g.Id] = g;
                        }
                    }
                    else if (!uniqueElements.ContainsKey(g.Id))
                    {
                        uniqueElements[g.Id] = g;
                    }
                }
            }

            List<GridItem> items = new List<GridItem>();
            foreach (Element elem in uniqueElements.Values)
            {
                items.Add(new GridItem(elem, doc));
            }
            return items;
        }

        /// <summary>
        /// Incrementa el valor de secuencia. Si es número, suma 1.
        /// Si es texto (letras), incrementa alfabéticamente (A→B, Z→AA).
        /// </summary>
        private string GetNextInSequence(string current)
        {
            if (int.TryParse(current, out int num))
                return (num + 1).ToString();

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
                    return s + "1";
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

        private double GetScreenYForHorizontal(GridItem item, Autodesk.Revit.DB.View view)
        {
            XYZ p0 = item.StartPoint;
            XYZ p1 = item.EndPoint;

            double x0 = p0.DotProduct(view.RightDirection);
            double x1 = p1.DotProduct(view.RightDirection);

            XYZ pLeft = (x0 <= x1) ? p0 : p1;
            return pLeft.DotProduct(view.UpDirection);
        }

        private double GetScreenXForVertical(GridItem item, Autodesk.Revit.DB.View view)
        {
            XYZ p0 = item.StartPoint;
            XYZ p1 = item.EndPoint;

            double y0 = p0.DotProduct(view.UpDirection);
            double y1 = p1.DotProduct(view.UpDirection);

            XYZ pTop = (y0 >= y1) ? p0 : p1;
            return pTop.DotProduct(view.RightDirection);
        }

        private void ClassifyGrids(List<GridItem> gridItems, Autodesk.Revit.DB.View view, out List<GridItem> horizontalGrids, out List<GridItem> verticalGrids)
        {
            horizontalGrids = new List<GridItem>();
            verticalGrids = new List<GridItem>();

            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();

            foreach (GridItem item in gridItems)
            {
                XYZ direction = item.EndPoint.Subtract(item.StartPoint);
                if (direction.IsZeroLength()) continue;

                direction = direction.Normalize();

                double compRight = Math.Abs(direction.DotProduct(right));
                double compUp = Math.Abs(direction.DotProduct(up));

                if (compRight >= compUp)
                    horizontalGrids.Add(item);
                else
                    verticalGrids.Add(item);
            }
        }
    }

    public class GridItem
    {
        public Element Element { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }

        public GridItem(Element elem, Document doc)
        {
            Element = elem;
            List<XYZ> endpoints = new List<XYZ>();

            if (elem is Grid g)
            {
                if (g.Curve != null)
                {
                    endpoints.Add(g.Curve.GetEndPoint(0));
                    endpoints.Add(g.Curve.GetEndPoint(1));
                }
            }
            else if (elem is MultiSegmentGrid msg)
            {
                ICollection<ElementId> subGridIds = msg.GetGridIds();
                foreach (ElementId id in subGridIds)
                {
                    if (doc.GetElement(id) is Grid subGrid && subGrid.Curve != null)
                    {
                        endpoints.Add(subGrid.Curve.GetEndPoint(0));
                        endpoints.Add(subGrid.Curve.GetEndPoint(1));
                    }
                }
            }

            if (endpoints.Count >= 2)
            {
                double maxDistSq = -1;
                XYZ bestStart = endpoints[0];
                XYZ bestEnd = endpoints[1];

                for (int i = 0; i < endpoints.Count; i++)
                {
                    for (int j = i + 1; j < endpoints.Count; j++)
                    {
                        double distSq = endpoints[i].DistanceTo(endpoints[j]);
                        if (distSq > maxDistSq)
                        {
                            maxDistSq = distSq;
                            bestStart = endpoints[i];
                            bestEnd = endpoints[j];
                        }
                    }
                }

                StartPoint = bestStart;
                EndPoint = bestEnd;
            }
            else if (endpoints.Count == 1)
            {
                StartPoint = endpoints[0];
                EndPoint = endpoints[0];
            }
            else
            {
                StartPoint = XYZ.Zero;
                EndPoint = XYZ.Zero;
            }
        }
    }

    public class GridSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Grid || elem is MultiSegmentGrid;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
