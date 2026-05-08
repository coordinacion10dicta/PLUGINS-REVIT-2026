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
    /// <summary>
    /// Comando externo para tagueo:
    /// - Tomas (Electrical Fixtures)
    /// - Codos (Conduit Fittings)
    /// - Conduits con texto de usuario (automático / manual por clic)
    ///
    /// Notas:
    /// - Usa TaskDialog como menú de navegación (Menú principal -> Confirmación -> Acción).
    /// - Anti-duplicados y control de densidad para conduits automáticos.
    /// - Guarda el texto del usuario en "Comentarios" de los conduits/codos cuando aplica.
    /// - Requiere una ventana WinForms llamada TagSelectionWindow(string[] items) con SelectedTagType.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_Tomas : IExternalCommand
    {
        #region Configuración / Constantes

        // Separaciones en pies para filtros de proximidad (anti-duplicados)
        private const double MIN_CLEAR_TAG_TO_ELEMENT_FT = 1.5; // radio mínimo entre elemento y head del tag
        private const double MIN_CLEAR_TAG_TO_TAG_FT = 2.0; // radio mínimo entre heads de tags

        // Opciones de menú
        private enum MenuOption { Tomas, Codos, ConduitsTexto, ConduitsTextoManual, Cancelar }
        private enum SubChoice { Aceptar, Regresar, Cancelar }

        #endregion

        #region Entrypoint / Menús

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            while (true)
            {
                var opt = MostrarMenuPrincipal();
                if (opt == MenuOption.Cancelar) return Result.Cancelled;

                var sub = MostrarSubmenu(opt);
                if (sub == SubChoice.Cancelar) return Result.Cancelled;
                if (sub == SubChoice.Regresar) continue;

                switch (opt)
                {
                    case MenuOption.Tomas: return EjecutarTagTomas(commandData);
                    case MenuOption.Codos: return EjecutarTagCodos(commandData);
                    case MenuOption.ConduitsTexto: return EjecutarTagConduitsTexto(commandData);
                    case MenuOption.ConduitsTextoManual: return EjecutarTagConduitsTextoManual(commandData);
                }
            }
        }

        /// <summary>Menú principal de selección.</summary>
        private MenuOption MostrarMenuPrincipal()
        {
            var td = new TaskDialog("TAGS Tomas - Menú principal")
            {
                MainInstruction = "¿Qué deseas taguear?",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Taguear Tomas");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Taguear Codos");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Taguear Fase,Neutro y Tierra (texto de usuario) – Automático");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Taguear Fase,Neutro y Tierra (texto de usuario) – Manual (clic en el elemento)");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return MenuOption.Tomas;
            if (r == TaskDialogResult.CommandLink2) return MenuOption.Codos;
            if (r == TaskDialogResult.CommandLink3) return MenuOption.ConduitsTexto;
            if (r == TaskDialogResult.CommandLink4) return MenuOption.ConduitsTextoManual;
            return MenuOption.Cancelar;
        }

        /// <summary>Submenú de confirmación previo a ejecutar la acción.</summary>
        private SubChoice MostrarSubmenu(MenuOption opt)
        {
            string titulo =
                opt == MenuOption.Tomas ? "Tomas" :
                opt == MenuOption.Codos ? "Codos" :
                opt == MenuOption.ConduitsTexto ? "Conduits (texto de usuario – Automático)" :
                "Conduits (texto de usuario – Manual)";

            var td = new TaskDialog($"TAGS ELE - {titulo}")
            {
                MainInstruction = $"¿Deseas continuar con el tagueo de {titulo.ToLower()}?",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Aceptar");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Regresar al menú principal");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return SubChoice.Aceptar;
            if (r == TaskDialogResult.CommandLink2) return SubChoice.Regresar;
            return SubChoice.Cancelar;
        }

        #endregion

        #region Tomas (Electrical Fixtures)

        /// <summary>
        /// Coloca tags para elementos de la categoría Electrical Fixtures (tomas) en la vista activa.
        /// Permite elegir cualquier tipo de tag cargado (sin validar compatibilidad específica).
        /// </summary>
        private Result EjecutarTagTomas(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1) Recolectar todos los tipos de tag disponibles (familias cargadas que contengan "tag" en la categoría)
            var availableTags = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (!availableTags.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag disponibles en el modelo.");
                return Result.Failed;
            }

            // 2) Selección del tipo de tag (por nombre de símbolo)
            var tagNames = availableTags.Select(t => t.Name).ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }

            string selectedTag = tagWindow.SelectedTagType;
            FamilySymbol tagType = availableTags.FirstOrDefault(t => t.Name.Equals(selectedTag, StringComparison.Ordinal));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"El tipo de tag '{selectedTag}' no fue encontrado.");
                return Result.Failed;
            }

            // 3) Recolectar tomas visibles en la vista activa
            var elementos = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                .Cast<FamilyInstance>()
                .ToList();

            if (!elementos.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron tomas en la vista activa.");
                return Result.Cancelled;
            }

            // 4) Crear tags
            using (var tx = new Transaction(doc, "Taguear Tomas"))
            {
                tx.Start();

                if (!tagType.IsActive)
                {
                    tagType.Activate();
                    doc.Regenerate();
                }

                XYZ rightDir = uidoc.ActiveView.RightDirection;
                XYZ downDir = -uidoc.ActiveView.UpDirection;

                foreach (var elem in elementos)
                {
                    if (elem.Location is LocationPoint location)
                    {
                        XYZ basePoint = location.Point;
                        XYZ tagPos = basePoint + (rightDir * 0.8) + (downDir * 0.4);

                        var newTag = IndependentTag.Create(
                            doc, tagType.Id, uidoc.ActiveView.Id, new Reference(elem),
                            false, TagOrientation.Horizontal, tagPos);

                        newTag.HasLeader = true;
                        newTag.LeaderEndCondition = LeaderEndCondition.Free;
                        newTag.TagHeadPosition = tagPos;
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon las tomas correctamente.");
            return Result.Succeeded;
        }

        #endregion

        #region Conduits (automático con texto de usuario)

        /// <summary>
        /// Taguea conduits visibles en la vista activa de forma automática, con:
        /// - Texto ingresado por el usuario (se guarda en "Comentarios" del conduit).
        /// - Selección de tipo de tag (OST_ConduitTags).
        /// - Control de densidad por celdas + anti-duplicados por proximidad.
        /// </summary>
        private Result EjecutarTagConduitsTexto(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;

            // 0) Validación de vista
            if (view.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Vista no soportada", "No se pueden crear tags en vistas 3D. Cambia a una vista 2D.");
                return Result.Cancelled;
            }

            // 1) Pedir texto al usuario
            if (!PromptForText("Texto del tag", "Escribe el texto que deseas mostrar (ej. 12 3/4\"):", out string userText))
                return Result.Cancelled;
            if (string.IsNullOrWhiteSpace(userText)) return Result.Cancelled;

            // 2) Densidad
            double cellFt = PedirTamanoCelda();
            if (cellFt <= 0) return Result.Cancelled;

            // 3) Tipos de tag para conduits
            var conduitTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();

            if (!conduitTagTypes.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag de Conduit (OST_ConduitTags) en el modelo.");
                return Result.Failed;
            }

            // 4) Elegir tipo de tag
            var names = conduitTagTypes.Select(t => $"{t.FamilyName} : {t.Name}").ToList();
            var pick = new TagSelectionWindow(names);
            if (pick.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedDisplay = pick.SelectedTagType;
            var tagType = conduitTagTypes.FirstOrDefault(t =>
                $"{t.FamilyName} : {t.Name}".Equals(selectedDisplay, StringComparison.Ordinal));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedDisplay}'.");
                return Result.Failed;
            }

            // 5) Conduits visibles
            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (!conduits.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron Conduits en la vista activa.");
                return Result.Cancelled;
            }

            // 6) Tags existentes relevantes para anti-duplicados
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category != null &&
                           (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags
                         || t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                .ToList();

            var tagHeadPositions = existingTags.Select(t => t.TagHeadPosition).ToList();
            var occupiedCells = new HashSet<string>();

            // 7) Crear tags
            using (var tx = new Transaction(doc, "Taguear Conduits (texto usuario)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                XYZ rightDir = view.RightDirection;
                XYZ downDir = -view.UpDirection;
                double dRight = 0.8, dDown = 0.4;

                foreach (var e in conduits)
                {
                    // Punto base (mitad de la curva si es posible; si no, centro del bbox)
                    XYZ basePoint = XYZ.Zero;
                    if (e.Location is LocationCurve lc && lc.Curve != null)
                    {
                        try { basePoint = lc.Curve.Evaluate(0.5, true); }
                        catch
                        {
                            var bb = e.get_BoundingBox(view);
                            basePoint = (bb != null) ? (bb.Min + bb.Max) / 2.0 : XYZ.Zero;
                        }
                    }
                    else
                    {
                        var bb = e.get_BoundingBox(view);
                        basePoint = (bb != null) ? (bb.Min + bb.Max) / 2.0 : XYZ.Zero;
                    }

                    // Guardar el texto en "Comentarios" del conduit
                    var cmt = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (cmt != null && !cmt.IsReadOnly) cmt.Set(userText);

                    // Filtro 1: proximidad al elemento
                    if (AnyTagWithinRadius2D(tagHeadPositions, basePoint, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    // Posición sugerida del head del tag
                    XYZ tagPos = basePoint + (rightDir * dRight) + (downDir * dDown);

                    // Filtro 2: proximidad entre heads
                    if (AnyTagWithinRadius2D(tagHeadPositions, tagPos, MIN_CLEAR_TAG_TO_TAG_FT))
                        continue;

                    // Filtro 3: densidad por celda (1 por celda y tipo)
                    int typeId = e.GetTypeId().IntegerValue;
                    string cellKey = MakeCellKey(basePoint, cellFt, typeId);
                    if (occupiedCells.Contains(cellKey))
                        continue;

                    // Crear tag
                    var tag = IndependentTag.Create(
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

            TaskDialog.Show("Completado", "Conduits tagueados. Verifica que la familia muestre el parámetro 'Comentarios'.");
            return Result.Succeeded;
        }

        #endregion

        #region Conduits (manual por clic múltiple con texto de usuario)

        /// <summary>
        /// Tagueo manual de conduits a partir de clics del usuario (PointOnElement).
        /// - Pide texto y lo guarda en "Comentarios" de cada conduit.
        /// - Evita duplicar tags del mismo elemento en la vista.
        /// </summary>
        private Result EjecutarTagConduitsTextoManual(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;

            // Vistas soportadas (no 3D ni Drafting)
            if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.DraftingView)
            {
                TaskDialog.Show("Vista no soportada",
                    "Usa una vista de planta, techo, sección o elevación (no 3D ni Drafting).");
                return Result.Cancelled;
            }

            // 1) Texto
            if (!PromptForText("Texto del tag", "Escribe el texto que deseas mostrar (ej. 12 3/4\"):", out string userText))
                return Result.Cancelled;
            if (string.IsNullOrWhiteSpace(userText)) return Result.Cancelled;

            // 2) Tipo de tag (solo ConduitTags)
            var conduitTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();

            if (!conduitTagTypes.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag de Conduit (OST_ConduitTags) en el modelo.");
                return Result.Failed;
            }

            var tagNames = conduitTagTypes.Select(t => $"{t.FamilyName} : {t.Name}").ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedDisplay = tagWindow.SelectedTagType;
            var tagType = conduitTagTypes.FirstOrDefault(t =>
                $"{t.FamilyName} : {t.Name}".Equals(selectedDisplay, StringComparison.Ordinal));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedDisplay}'.");
                return Result.Failed;
            }

            // 3) Selección manual (clic sobre conduits)
            IList<Reference> picks;
            try
            {
                picks = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new ConduitPickFilter_Tomas(),
                    "Selecciona conduits (clics sucesivos). Pulsa ESC para terminar.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (picks == null || picks.Count == 0) return Result.Cancelled;

            // 4) Crear tags en una sola transacción
            using (var tx = new Transaction(doc, "Tag Conduits (manual múltiple)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (var pickRef in picks)
                {
                    var elem = doc.GetElement(pickRef.ElementId);
                    if (elem == null) continue;

                    // Evitar duplicar tag del mismo elemento en la vista
                    if (HasTagInView(doc, view.Id, elem.Id)) continue;

                    // Guardar "Comentarios"
                    var cmt = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (cmt != null && !cmt.IsReadOnly) cmt.Set(userText);

                    // Head del tag = punto clickado
                    XYZ head = pickRef.GlobalPoint;

                    // Referencia al ELEMENTO (no a cara/borde)
                    var elemRef = new Reference(elem);

                    // Crear tag por categoría y luego cambiar type si fuese necesario
                    var tag = IndependentTag.Create(
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

        #endregion

        #region Codos (Conduit Fittings)

        /// <summary>
        /// Coloca tags para codos (accesorios de conduit) seleccionados por el usuario.
        /// Filtra por familia/nombres permitidos (ej. "DC- Tag accesorio conduit_URB").
        /// </summary>
        private Result EjecutarTagCodos(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1) Filtrar tipos de tag permitidos (familia/nombre)
            string requiredFamily = "DC- Tag accesorio conduit_URB";
            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Cambio de nivel", "Cambio de nivel Baja", "Cambio de nivel Sube" };

            var allTagSymbols = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            var conduitFittingTags = allTagSymbols
                .Where(fs => fs.Category != null &&
                            (fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitFittingTags ||
                             (fs.Category.Name.IndexOf("conduit", StringComparison.OrdinalIgnoreCase) >= 0 &&
                              fs.Category.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)))
                .Where(fs => (fs.Family?.Name?.Equals(requiredFamily, StringComparison.OrdinalIgnoreCase) ?? false) &&
                             allowedNames.Contains(fs.Name))
                .ToList();

            if (!conduitFittingTags.Any())
            {
                TaskDialog.Show("Error",
                    "No se encontraron tipos de tag válidos de 'DC- Tag accesorio conduit_URB' (Cambio de nivel / Baja / Sube).");
                return Result.Failed;
            }

            // 2) Elegir tipo de tag por nombre de símbolo
            var tagNames = conduitFittingTags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            string selectedTag = tagWindow.SelectedTagType;
            var tagType = conduitFittingTags.FirstOrDefault(t => t.Name.Equals(selectedTag, StringComparison.OrdinalIgnoreCase));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedTag}'.");
                return Result.Failed;
            }

            // 3) Selección de codos
            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(
                    ObjectType.Element, new CodoSelectionFilter(),
                    "Selecciona los codos a taguear y pulsa 'Finalizar'. (Esc para cancelar)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                TaskDialog.Show("Cancelado", "No se seleccionaron codos.");
                return Result.Cancelled;
            }

            var codos = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
            if (!codos.Any())
            {
                TaskDialog.Show("Aviso", "No se seleccionaron codos.");
                return Result.Cancelled;
            }

            // 4) Crear tags
            using (var tx = new Transaction(doc, "Tag Codos (Conduit Fittings)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (var inst in codos)
                {
                    XYZ tagHeadPos = XYZ.Zero;
                    var bbox = inst.get_BoundingBox(uidoc.ActiveView);

                    if (bbox != null)
                    {
                        // Centro y candidatos alrededor para evitar colisiones
                        XYZ c = (bbox.Min + bbox.Max) / 2;
                        double z = c.Z;
                        double d = 0.8;

                        var candidates = new[]
                        {
                            new XYZ(bbox.Max.X + d, c.Y, z),
                            new XYZ(bbox.Min.X - d, c.Y, z),
                            new XYZ(c.X, bbox.Max.Y + d, z),
                            new XYZ(c.X, bbox.Min.Y - d, z),
                        };

                        foreach (var p in candidates)
                        {
                            var outline = new Outline(p - new XYZ(0.25, 0.25, 0), p + new XYZ(0.25, 0.25, 0));
                            var filter = new BoundingBoxIntersectsFilter(outline);

                            bool ocupado = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                                .WherePasses(filter)
                                .Where(e => e.Id != inst.Id && !(e is View))
                                .Any();

                            if (!ocupado) { tagHeadPos = p; break; }
                        }

                        // Fallback si todo estaba ocupado
                        if (tagHeadPos == XYZ.Zero)
                            tagHeadPos = new XYZ(bbox.Max.X + d, bbox.Min.Y - d, z);
                    }
                    else
                    {
                        var loc = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        tagHeadPos = loc + new XYZ(0.8, -0.4, 0);
                    }

                    var tag = IndependentTag.Create(
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

        #endregion

        #region Helpers comunes

        /// <summary>
        /// Devuelve el ElementId del elemento host etiquetado por un IndependentTag,
        /// compatible con Revit 2020–2025 (intenta varias APIs: GetTaggedReferences, TaggedLocalElementId, TaggedElementId/LinkElementId).
        /// </summary>
        private static ElementId GetTaggedHostElementId(IndependentTag tag)
        {
            // A) Intento con GetTaggedReferences() (algunas versiones lo exponen)
            try
            {
                var mi = tag.GetType().GetMethod("GetTaggedReferences", Type.EmptyTypes);
                if (mi != null)
                {
                    var refsObj = mi.Invoke(tag, null) as IList<Reference>;
                    if (refsObj != null)
                    {
                        foreach (var r in refsObj)
                        {
                            if (r != null && r.ElementId != null && r.ElementId != ElementId.InvalidElementId)
                                return r.ElementId;
                        }
                    }
                }
            }
            catch { /* ignorar y seguir con otros métodos */ }

            // B) Revit 2022+: TaggedLocalElementId (ElementId)
            try
            {
                var propLocal = tag.GetType().GetProperty("TaggedLocalElementId");
                if (propLocal != null)
                {
                    var val = propLocal.GetValue(tag, null);
                    if (val is ElementId eid && eid != ElementId.InvalidElementId)
                        return eid;
                }
            }
            catch { }

            // C) Propiedad TaggedElementId (puede ser ElementId o LinkElementId)
            try
            {
                var prop = tag.GetType().GetProperty("TaggedElementId");
                if (prop != null)
                {
                    var val = prop.GetValue(tag, null);

                    if (val is ElementId e1 && e1 != ElementId.InvalidElementId)
                        return e1;

                    // LinkElementId.HostElementId
                    var hostProp = val?.GetType().GetProperty("HostElementId");
                    if (hostProp != null)
                    {
                        var hostId = hostProp.GetValue(val, null);
                        if (hostId is ElementId e2 && e2 != ElementId.InvalidElementId)
                            return e2;
                    }
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// ¿Ya existe un tag en esta vista que apunte al mismo elemento?
        /// </summary>
        private static bool HasTagInView(Document doc, ElementId viewId, ElementId elemId)
        {
            var tags = new FilteredElementCollector(doc, viewId)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var t in tags)
            {
                ElementId tagged = GetTaggedHostElementId(t);
                if (tagged != ElementId.InvalidElementId && tagged == elemId)
                    return true;
            }
            return false;
        }

        /// <summary>Clave de celda para control de densidad (2D, ignora Z) incluyendo el tipo de elemento.</summary>
        private static string MakeCellKey(XYZ p, double cellFt, int typeId)
        {
            int ix = (int)Math.Floor(p.X / cellFt);
            int iy = (int)Math.Floor(p.Y / cellFt);
            return $"{ix}:{iy}:{typeId}";
        }

        /// <summary>
        /// Selector rápido de tamaño de celda (densidad). Devuelve pies. -1 si cancelado.
        /// </summary>
        private static double PedirTamanoCelda()
        {
            var td = new TaskDialog("Densidad de etiquetas")
            {
                MainInstruction = "Elige densidad de tags (1 por celda y tipo):",
                MainContent = "Baja: celdas grandes (menos tags)\n" +
                                  "Media: equilibrio\n" +
                                  "Alta: celdas pequeñas (más tags)",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Baja (16 ft)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Media (12 ft)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Alta (8 ft)");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return 16.0;
            if (r == TaskDialogResult.CommandLink2) return 12.0;
            if (r == TaskDialogResult.CommandLink3) return 8.0;
            return -1; // Cancelado
        }

        /// <summary>¿Existe alguna cabeza de tag a una distancia (2D) <= radio del punto dado?</summary>
        private static bool AnyTagWithinRadius2D(IEnumerable<XYZ> tagHeads, XYZ point, double radius)
        {
            double r2 = radius * radius;
            foreach (var th in tagHeads)
            {
                if (th == null) continue;
                if (SquaredPlanarDistance(th, point) <= r2) return true;
            }
            return false;
        }

        /// <summary>Distancia al cuadrado en planta (sin Z).</summary>
        private static double SquaredPlanarDistance(XYZ a, XYZ b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Diálogo simple de entrada de texto (WinForms); retorna true si el usuario aceptó.
        /// </summary>
        private static bool PromptForText(string title, string prompt, out string result)
        {
            result = null;
            using (var f = new WinForms.Form())
            using (var l = new WinForms.Label())
            using (var tb = new WinForms.TextBox())
            using (var ok = new WinForms.Button())
            using (var cancel = new WinForms.Button())
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

                if (f.ShowDialog() == WinForms.DialogResult.OK)
                {
                    result = tb.Text;
                    return true;
                }
                return false;
            }
        }

        #endregion

        #region Filtros de selección

        /// <summary>
        /// Filtro para seleccionar codos (Conduit Fittings).
        /// </summary>
        private class CodoSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_ConduitFitting) return false;

                // (Opcional) heurística por nombre para asegurar que sea un "codo"
                var fi = e as FamilyInstance;
                if (fi == null) return false;

                string fam = fi.Symbol?.Family?.Name ?? "";
                string sym = fi.Symbol?.Name ?? "";
                return fam.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       fam.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>
        /// Filtro de selección de conduits para el flujo manual (PointOnElement).
        /// </summary>
        private class ConduitPickFilter_Tomas : ISelectionFilter
        {
            public bool AllowElement(Element e)
                => e?.Category != null &&
                   e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Conduit;

            public bool AllowReference(Reference r, XYZ p) => true; // se requiere para PointOnElement
        }

        #endregion
    }

    /// <summary>
    /// Ventana simple para elegir una opción de una lista (extra útil para prototipos).
    /// *No se usa arriba, pero se deja como utilitario si te hace falta en otros flujos.*
    /// </summary>
    public class CategorySelectionWindow : WinForms.Form
    {
        private readonly WinForms.ComboBox _combo;
        private readonly WinForms.Button _btnOk;

        public string Seleccion { get; private set; }

        public CategorySelectionWindow(string[] opciones)
        {
            Text = "Seleccionar Categoría";
            Size = new System.Drawing.Size(300, 130);
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;

            _combo = new WinForms.ComboBox { Left = 20, Top = 20, Width = 240 };
            _combo.Items.AddRange(opciones);
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;

            _btnOk = new WinForms.Button { Text = "Aceptar", Left = 100, Top = 60, Width = 80 };
            _btnOk.Click += (sender, e) =>
            {
                Seleccion = _combo.SelectedItem?.ToString();
                DialogResult = WinForms.DialogResult.OK;
            };

            Controls.Add(_combo);
            Controls.Add(_btnOk);
        }
    }
}
