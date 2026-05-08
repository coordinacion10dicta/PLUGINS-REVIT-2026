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

using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Windows.Forms; // si te da ambigüedad con View, usa el alias WinForms abajo
using WinForms = System.Windows.Forms;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_Alumbrado : IExternalCommand
    {
        // --- Parámetros para separación/anti-duplicados (pies) ---
        const double MIN_CLEAR_TAG_TO_ELEMENT_FT = 1.5;
        const double MIN_CLEAR_TAG_TO_TAG_FT = 2.0;

        // Menú principal
        private enum MenuOption { Alumbrado, Codos, ConduitsAuto, ConduitsManual, Cancelar }
        private enum SubChoice { Aceptar, Regresar, Cancelar }

        #region Entrada principal y menús

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            while (true)
            {
                MenuOption opt = MostrarMenuPrincipal();
                if (opt == MenuOption.Cancelar) return Result.Cancelled;

                SubChoice sub = MostrarSubmenu(opt);
                if (sub == SubChoice.Cancelar) return Result.Cancelled;
                if (sub == SubChoice.Regresar) continue;

                switch (opt)
                {
                    case MenuOption.Alumbrado: return EjecutarTagAlumbrado(commandData);
                    case MenuOption.Codos: return EjecutarTagCodos(commandData);
                    case MenuOption.ConduitsAuto: return EjecutarTagConduits_Automatico(commandData);
                    case MenuOption.ConduitsManual: return EjecutarTagConduits_Manual(commandData);
                }
            }
        }

        private MenuOption MostrarMenuPrincipal()
        {
            TaskDialog td = new TaskDialog("TAGS Alumbrado - Menú principal");
            td.MainInstruction = "¿Qué deseas taguear?";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Taguear Alumbrado");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Taguear Codos");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Taguear FNT (texto de usuario) – Automático");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Taguear FNT (texto de usuario) – Manual (clic en el elemento)");

            TaskDialogResult r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return MenuOption.Alumbrado;
            if (r == TaskDialogResult.CommandLink2) return MenuOption.Codos;
            if (r == TaskDialogResult.CommandLink3) return MenuOption.ConduitsAuto;
            if (r == TaskDialogResult.CommandLink4) return MenuOption.ConduitsManual;
            return MenuOption.Cancelar;
        }

        private SubChoice MostrarSubmenu(MenuOption opt)
        {
            string titulo =
                opt == MenuOption.Alumbrado ? "Alumbrado" :
                opt == MenuOption.Codos ? "Codos" :
                opt == MenuOption.ConduitsAuto ? "Conduits (texto de usuario – Automático)" :
                "Conduits (texto de usuario – Manual)";

            TaskDialog td = new TaskDialog("TAGS ELE - " + titulo);
            td.MainInstruction = "¿Deseas continuar con el tagueo de " + titulo.ToLower() + "?";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Aceptar");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Regresar al menú principal");

            TaskDialogResult r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return SubChoice.Aceptar;
            if (r == TaskDialogResult.CommandLink2) return SubChoice.Regresar;
            return SubChoice.Cancelar;
        }

        #endregion

        // -------------------- Alumbrado --------------------

        private Result EjecutarTagAlumbrado(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<FamilySymbol> availableTags = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Name.ToLower().Contains("tag"))
                .ToList();

            if (availableTags.Count == 0)
            {
                TaskDialog.Show("Error", "No hay tipos de tag disponibles en el modelo.");
                return Result.Failed;
            }

            List<string> tagNames = new List<string>();
            foreach (FamilySymbol t in availableTags) tagNames.Add(t.Name);

            TagSelectionWindow tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != DialogResult.OK)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }

            string selectedTag = tagWindow.SelectedTagType;
            FamilySymbol tagType = availableTags.FirstOrDefault(t => t.Name == selectedTag);
            if (tagType == null)
            {
                TaskDialog.Show("Error", "El tipo de tag '" + selectedTag + "' no fue encontrado.");
                return Result.Failed;
            }

            List<FamilyInstance> elementos = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_LightingDevices)
                .Cast<FamilyInstance>()
                .ToList();

            if (elementos.Count == 0)
            {
                TaskDialog.Show("Aviso", "No se encontraron puntos de derivación conectados en la vista activa.");
                return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "Taguear Puntos de Derivación"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                XYZ rightDir = uidoc.ActiveView.RightDirection;
                XYZ downDir = -uidoc.ActiveView.UpDirection;

                foreach (FamilyInstance elem in elementos)
                {
                    LocationPoint lp = elem.Location as LocationPoint;
                    if (lp == null) continue;

                    XYZ basePoint = lp.Point;
                    XYZ tagPos = basePoint + (rightDir * 0.8) + (downDir * 0.4);

                    IndependentTag newTag = IndependentTag.Create(
                        doc, tagType.Id, uidoc.ActiveView.Id, new Reference(elem),
                        false, TagOrientation.Horizontal, tagPos);

                    newTag.HasLeader = true;
                    newTag.LeaderEndCondition = LeaderEndCondition.Free;
                    newTag.TagHeadPosition = tagPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon los puntos de derivación conectados.");
            return Result.Succeeded;
        }

        // -------------------- Conduits (Automático) --------------------

        private Result EjecutarTagConduits_Automatico(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View view = uidoc.ActiveView;

            if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.DraftingView)
            {
                TaskDialog.Show("Vista no soportada", "No se pueden crear tags en 3D ni en Drafting View.");
                return Result.Cancelled;
            }

            string userText;
            if (!PromptForText("Texto del tag", "Escribe el texto que deseas mostrar (ej. 12 3/4\"):", out userText))
                return Result.Cancelled;
            if (string.IsNullOrWhiteSpace(userText)) return Result.Cancelled;

            double cellFt = PedirTamanoCelda();
            if (cellFt <= 0) return Result.Cancelled;

            List<FamilySymbol> conduitTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();
            if (conduitTagTypes.Count == 0)
            {
                TaskDialog.Show("Error", "No hay tipos de tag de Conduit (OST_ConduitTags) en el modelo.");
                return Result.Failed;
            }

            List<string> names = new List<string>();
            foreach (FamilySymbol t in conduitTagTypes) names.Add(t.FamilyName + " : " + t.Name);
            TagSelectionWindow pick = new TagSelectionWindow(names);
            if (pick.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedDisplay = pick.SelectedTagType;
            FamilySymbol tagType = conduitTagTypes.FirstOrDefault(t => (t.FamilyName + " : " + t.Name) == selectedDisplay);
            if (tagType == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de tag '" + selectedDisplay + "'.");
                return Result.Failed;
            }

            List<Element> conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            if (conduits.Count == 0)
            {
                TaskDialog.Show("Aviso", "No se encontraron Conduits en la vista activa.");
                return Result.Cancelled;
            }

            List<IndependentTag> existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(t => t.Category != null &&
                            (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags ||
                             t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                .ToList();

            List<XYZ> tagHeadPositions = new List<XYZ>();
            foreach (IndependentTag t in existingTags) tagHeadPositions.Add(t.TagHeadPosition);

            HashSet<string> occupiedCells = new HashSet<string>();

            using (Transaction tx = new Transaction(doc, "Tag Conduits (automático)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                XYZ rightDir = view.RightDirection;
                XYZ downDir = -view.UpDirection;
                double dRight = 0.8, dDown = 0.4;

                foreach (Element e in conduits)
                {
                    XYZ basePoint = XYZ.Zero;

                    LocationCurve lc = e.Location as LocationCurve;
                    if (lc != null && lc.Curve != null)
                    {
                        try { basePoint = lc.Curve.Evaluate(0.5, true); }
                        catch
                        {
                            BoundingBoxXYZ bb = e.get_BoundingBox(view);
                            if (bb != null) basePoint = (bb.Min + bb.Max) / 2.0;
                        }
                    }
                    else
                    {
                        BoundingBoxXYZ bb = e.get_BoundingBox(view);
                        if (bb != null) basePoint = (bb.Min + bb.Max) / 2.0;
                    }

                    Parameter cmt = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (cmt != null && !cmt.IsReadOnly) cmt.Set(userText);

                    if (AnyTagWithinRadius2D(tagHeadPositions, basePoint, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    XYZ tagPos = basePoint + (rightDir * dRight) + (downDir * dDown);

                    if (AnyTagWithinRadius2D(tagHeadPositions, tagPos, MIN_CLEAR_TAG_TO_TAG_FT))
                        continue;

                    int typeId = e.GetTypeId().IntegerValue;
                    string cellKey = MakeCellKey(basePoint, cellFt, typeId);
                    if (occupiedCells.Contains(cellKey))
                        continue;

                    IndependentTag tag = IndependentTag.Create(
                        doc, tagType.Id, view.Id, new Reference(e),
                        false, TagOrientation.Horizontal, tagPos);

                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagPos;

                    occupiedCells.Add(cellKey);
                    tagHeadPositions.Add(tag.TagHeadPosition);
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Conduits tagueados (modo automático). Verifica que el Tag muestre 'Comentarios'.");
            return Result.Succeeded;
        }

        // -------------------- Conduits (Manual, clic a clic, múltiple) --------------------

        private Result EjecutarTagConduits_Manual(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View view = uidoc.ActiveView;

            if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.DraftingView)
            {
                TaskDialog.Show("Vista no soportada", "Usa una vista de planta/techo/sección/elevación (no 3D/Drafting).");
                return Result.Cancelled;
            }

            string userText;
            if (!PromptForText("Texto del tag", "Escribe el texto que deseas mostrar (ej. 12 3/4\"):", out userText))
                return Result.Cancelled;
            if (string.IsNullOrWhiteSpace(userText)) return Result.Cancelled;

            List<FamilySymbol> conduitTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();
            if (conduitTagTypes.Count == 0)
            {
                TaskDialog.Show("Error", "No hay tipos de tag de Conduit (OST_ConduitTags) en el modelo.");
                return Result.Failed;
            }

            List<string> tagNames = new List<string>();
            foreach (FamilySymbol t in conduitTagTypes) tagNames.Add(t.FamilyName + " : " + t.Name);
            TagSelectionWindow tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedDisplay = tagWindow.SelectedTagType;
            FamilySymbol tagType = conduitTagTypes.FirstOrDefault(t => (t.FamilyName + " : " + t.Name) == selectedDisplay);
            if (tagType == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de tag '" + selectedDisplay + "'.");
                return Result.Failed;
            }

            IList<Reference> picks;
            try
            {
                picks = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new ConduitPickFilter_Alumbrado(),
                    "Selecciona conduits (clics sucesivos). Pulsa ESC para terminar.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            if (picks == null || picks.Count == 0) return Result.Cancelled;

            using (Transaction tx = new Transaction(doc, "Tag Conduits (manual múltiple)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (Reference pickRef in picks)
                {
                    Element elem = doc.GetElement(pickRef.ElementId);
                    if (elem == null) continue;

                    if (HasTagInView(doc, view.Id, elem.Id)) continue;

                    Parameter cmt = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (cmt != null && !cmt.IsReadOnly) cmt.Set(userText);

                    XYZ head = pickRef.GlobalPoint;
                    Reference elemRef = new Reference(elem);

                    IndependentTag tag = IndependentTag.Create(
                        doc, view.Id, elemRef, true,
                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, head);

                    if (tag == null) continue;

                    if (tag.GetTypeId() != tagType.Id)
                        tag.ChangeTypeId(tagType.Id);

                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = head;
                }

                tx.Commit();
            }

            return Result.Succeeded;
        }

        // -------------------- Codos --------------------

        private Result EjecutarTagCodos(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            string requiredFamily = "DC- Tag accesorio conduit_URB";
            HashSet<string> allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Cambio de nivel", "Cambio de nivel Baja", "Cambio de nivel Sube" };

            IEnumerable<FamilySymbol> allTagSymbols = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            List<FamilySymbol> conduitFittingTags = allTagSymbols
                .Where(fs => fs.Category != null &&
                            (fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags ||
                             (fs.Category.Name.IndexOf("conduit", StringComparison.OrdinalIgnoreCase) >= 0 &&
                              fs.Category.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)))
                .Where(fs => (fs.Family != null && fs.Family.Name != null &&
                             fs.Family.Name.Equals(requiredFamily, StringComparison.OrdinalIgnoreCase)) &&
                             allowedNames.Contains(fs.Name))
                .ToList();

            if (conduitFittingTags.Count == 0)
            {
                TaskDialog.Show("Error",
                    "No se encontraron tipos de tag válidos de 'DC- Tag accesorio conduit_URB' (Cambio de nivel / Baja / Sube).");
                return Result.Failed;
            }

            List<string> tagNames = conduitFittingTags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            TagSelectionWindow tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedTag = tagWindow.SelectedTagType;
            FamilySymbol tagType = conduitFittingTags.FirstOrDefault(t => t.Name.Equals(selectedTag, StringComparison.OrdinalIgnoreCase));
            if (tagType == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de tag '" + selectedTag + "'.");
                return Result.Failed;
            }

            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(ObjectType.Element, new CodoSelectionFilter(),
                       "Selecciona los codos a taguear y pulsa 'Finalizar'. (Esc para cancelar)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                TaskDialog.Show("Cancelado", "No se seleccionaron codos.");
                return Result.Cancelled;
            }

            List<FamilyInstance> codos = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
            if (codos.Count == 0)
            {
                TaskDialog.Show("Aviso", "No se seleccionaron codos.");
                return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, "Tag Codos (Conduit Fittings)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (FamilyInstance inst in codos)
                {
                    XYZ tagHeadPos = XYZ.Zero;
                    BoundingBoxXYZ bbox = inst.get_BoundingBox(uidoc.ActiveView);

                    if (bbox != null)
                    {
                        XYZ c = (bbox.Min + bbox.Max) / 2;
                        double z = c.Z;
                        double d = 0.8;

                        XYZ[] candidates = new XYZ[]
                        {
                            new XYZ(bbox.Max.X + d, c.Y, z),
                            new XYZ(bbox.Min.X - d, c.Y, z),
                            new XYZ(c.X, bbox.Max.Y + d, z),
                            new XYZ(c.X, bbox.Min.Y - d, z),
                        };

                        foreach (XYZ p in candidates)
                        {
                            Outline outline = new Outline(p - new XYZ(0.25, 0.25, 0), p + new XYZ(0.25, 0.25, 0));
                            BoundingBoxIntersectsFilter filter = new BoundingBoxIntersectsFilter(outline);

                            bool ocupado = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                                .WherePasses(filter)
                                .Where(e => e.Id != inst.Id && !(e is Autodesk.Revit.DB.View))
                                .Any();

                            if (!ocupado) { tagHeadPos = p; break; }
                        }

                        if (tagHeadPos == XYZ.Zero)
                            tagHeadPos = new XYZ(bbox.Max.X + d, bbox.Min.Y - d, z);
                    }
                    else
                    {
                        LocationPoint loc = inst.Location as LocationPoint;
                        XYZ basePt = (loc != null) ? loc.Point : XYZ.Zero;
                        tagHeadPos = basePt + new XYZ(0.8, -0.4, 0);
                    }

                    IndependentTag tag = IndependentTag.Create(
                        doc, tagType.Id, uidoc.ActiveView.Id, new Reference(inst),
                        false, TagOrientation.Horizontal, tagHeadPos);

                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagHeadPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon los codos correctamente.");
            return Result.Succeeded;
        }

        private class CodoSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e == null || e.Category == null) return false;
                if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_ConduitFitting) return false;

                FamilyInstance fi = e as FamilyInstance;
                if (fi == null) return false;

                string fam = fi.Symbol != null && fi.Symbol.Family != null ? (fi.Symbol.Family.Name ?? "") : "";
                string sym = fi.Symbol != null ? (fi.Symbol.Name ?? "") : "";

                return fam.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       fam.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }

        // -------------------- Helpers comunes --------------------

        // Obtiene el ElementId local del host tagueado (compatible 2020–2025)
        private static ElementId GetTaggedHostElementId(IndependentTag tag)
        {
            // A) GetTaggedReferences()
            try
            {
                System.Reflection.MethodInfo mi = tag.GetType().GetMethod("GetTaggedReferences", Type.EmptyTypes);
                if (mi != null)
                {
                    IList<Reference> refsObj = mi.Invoke(tag, null) as IList<Reference>;
                    if (refsObj != null)
                    {
                        foreach (Reference r in refsObj)
                        {
                            if (r != null && r.ElementId != ElementId.InvalidElementId)
                                return r.ElementId;
                        }
                    }
                }
            }
            catch { }

            // B) 2022+: TaggedLocalElementId
            try
            {
                var propLocal = tag.GetType().GetProperty("TaggedLocalElementId");
                if (propLocal != null)
                {
                    object val = propLocal.GetValue(tag, null);
                    ElementId eid = val as ElementId;
                    if (eid != null && eid != ElementId.InvalidElementId)
                        return eid;
                }
            }
            catch { }

            // C) TaggedElementId (puede ser ElementId o LinkElementId)
            try
            {
                var prop = tag.GetType().GetProperty("TaggedElementId");
                if (prop != null)
                {
                    object val = prop.GetValue(tag, null);

                    ElementId e1 = val as ElementId;
                    if (e1 != null && e1 != ElementId.InvalidElementId)
                        return e1;

                    // LinkElementId.HostElementId
                    if (val != null)
                    {
                        var hostProp = val.GetType().GetProperty("HostElementId");
                        if (hostProp != null)
                        {
                            object hostIdObj = hostProp.GetValue(val, null);
                            ElementId e2 = hostIdObj as ElementId;
                            if (e2 != null && e2 != ElementId.InvalidElementId)
                                return e2;
                        }
                    }
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private static bool HasTagInView(Document doc, ElementId viewId, ElementId elemId)
        {
            foreach (IndependentTag t in new FilteredElementCollector(doc, viewId).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                ElementId tagged = GetTaggedHostElementId(t);
                if (tagged != ElementId.InvalidElementId && tagged == elemId)
                    return true;
            }
            return false;
        }

        private static string MakeCellKey(XYZ p, double cellFt, int typeId)
        {
            int ix = (int)Math.Floor(p.X / cellFt);
            int iy = (int)Math.Floor(p.Y / cellFt);
            return ix.ToString() + ":" + iy.ToString() + ":" + typeId.ToString();
        }

        private static double PedirTamanoCelda()
        {
            TaskDialog td = new TaskDialog("Densidad de etiquetas");
            td.MainInstruction = "Elige densidad de tags (1 por celda y tipo):";
            td.MainContent = "Baja: celdas grandes (menos tags)\nMedia: equilibrio\nAlta: celdas pequeñas (más tags)";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Baja (16 ft)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Media (12 ft)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Alta (8 ft)");

            TaskDialogResult r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return 16.0;
            if (r == TaskDialogResult.CommandLink2) return 12.0;
            if (r == TaskDialogResult.CommandLink3) return 8.0;
            return -1;
        }

        private static bool AnyTagWithinRadius2D(IEnumerable<XYZ> tagHeads, XYZ point, double radius)
        {
            double r2 = radius * radius;
            foreach (XYZ th in tagHeads)
            {
                if (th == null) continue;
                if (SquaredPlanarDistance(th, point) <= r2) return true;
            }
            return false;
        }

        private static double SquaredPlanarDistance(XYZ a, XYZ b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static bool PromptForText(string title, string prompt, out string result)
        {
            result = null;
            using (WinForms.Form f = new WinForms.Form())
            using (WinForms.Label l = new WinForms.Label())
            using (WinForms.TextBox tb = new WinForms.TextBox())
            using (WinForms.Button ok = new WinForms.Button())
            using (WinForms.Button cancel = new WinForms.Button())
            {
                f.Text = title;
                f.StartPosition = WinForms.FormStartPosition.CenterScreen;
                f.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.ClientSize = new System.Drawing.Size(360, 120);

                l.Text = prompt; l.AutoSize = true; l.Left = 12; l.Top = 12;
                tb.Left = 12; tb.Top = 40; tb.Width = 330;
                ok.Text = "Aceptar"; ok.Left = 186; ok.Top = 80; ok.DialogResult = WinForms.DialogResult.OK;
                cancel.Text = "Cancelar"; cancel.Left = 267; cancel.Top = 80; cancel.DialogResult = WinForms.DialogResult.Cancel;

                f.Controls.AddRange(new WinForms.Control[] { l, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog() == WinForms.DialogResult.OK) { result = tb.Text; return true; }
                return false;
            }
        }

        // Filtro local para seleccionar Conduits en modo manual (PointOnElement)
        private class ConduitPickFilter_Alumbrado : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                return e != null &&
                       e.Category != null &&
                       e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Conduit;
            }

            public bool AllowReference(Reference r, XYZ p) { return true; }
        }
    }
}

// Ventana simple de ejemplo (no modificada)
public class CategorySelectionWindow : WinForms.Form
{
    private WinForms.ComboBox combo;
    private WinForms.Button btnOk;
    public string Seleccion { get; private set; }

    public CategorySelectionWindow(string[] opciones)
    {
        this.Text = "Seleccionar Categoría";
        this.Size = new System.Drawing.Size(300, 130);
        this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
        this.StartPosition = WinForms.FormStartPosition.CenterScreen;

        combo = new WinForms.ComboBox { Left = 20, Top = 20, Width = 240 };
        combo.Items.AddRange(opciones);
        combo.SelectedIndex = 0;

        btnOk = new WinForms.Button { Text = "Aceptar", Left = 100, Top = 60, Width = 80 };
        btnOk.Click += (sender, e) =>
        {
            Seleccion = combo.SelectedItem.ToString();
            this.DialogResult = WinForms.DialogResult.OK;
        };

        this.Controls.Add(combo);
        this.Controls.Add(btnOk);
    }
}
