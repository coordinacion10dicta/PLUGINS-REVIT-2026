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
using WinForms = System.Windows.Forms;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    /// <summary>
    /// Comando externo para tagueo eléctrico:
    /// - Varillas (Conduits) en vista activa (anti-duplicados + densidad por celdas).
    /// - Cajas de inspección (Electrical Equipment).
    /// - Codos (Conduit Fittings) con texto ingresado por el usuario (se guarda en "Comentarios").
    /// - Bajantes verticales (Conduits con dirección casi paralela a Z).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_SIPRA : IExternalCommand
    {
        #region Constantes / Configuración

        // Radios de separación (en pies) para evitar tags muy cercanos entre sí o al elemento.
        private const double MIN_CLEAR_TAG_TO_ELEMENT_FT = 1.5;
        private const double MIN_CLEAR_TAG_TO_TAG_FT = 2.0;

        // Opciones del menú principal
        private enum MenuOption
        {
            Varillas,
            Cajas,
            Codos,
            BajantesVerticales,
            Cancelar
        }

        // Opciones del submenú de confirmación
        private enum SubChoice
        {
            Aceptar,
            Regresar,
            Cancelar
        }

        #endregion

        #region Entrypoint / Menús

        /// <summary>
        /// Punto de entrada del comando.
        /// Muestra Menú -> Submenú -> Ejecuta flujo seleccionado.
        /// </summary>
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
                    case MenuOption.Varillas: return EjecutarTagConduits(commandData);
                    case MenuOption.Cajas: return EjecutarTagCajas(commandData);
                    case MenuOption.Codos: return EjecutarTagCodos(commandData);
                    case MenuOption.BajantesVerticales: return EjecutarTagBajantesVerticales(commandData);
                }
            }
        }

        /// <summary>Menú principal con las opciones de tagueo.</summary>
        private MenuOption MostrarMenuPrincipal()
        {
            var td = new TaskDialog("TAGS ELE - Menú principal")
            {
                MainInstruction = "¿Qué deseas taguear?",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Taguear Varillas");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Taguear Cajas de inspección y Puntas franklin");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Taguear Codos");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Taguear verticales");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return MenuOption.Varillas;
            if (r == TaskDialogResult.CommandLink2) return MenuOption.Cajas;
            if (r == TaskDialogResult.CommandLink3) return MenuOption.Codos;
            if (r == TaskDialogResult.CommandLink4) return MenuOption.BajantesVerticales;
            return MenuOption.Cancelar;
        }

        /// <summary>Submenú de confirmación antes de ejecutar cada flujo.</summary>
        private SubChoice MostrarSubmenu(MenuOption opt)
        {
            string titulo = opt == MenuOption.Varillas ? "Varillas" :
                            opt == MenuOption.Cajas ? "Cajas de inspección y Puntas franklin" :
                            opt == MenuOption.Codos ? "Codos" :
                            "verticales";

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

        #region Varillas (Conduits)

        /// <summary>
        /// Taguea CONDUITS visibles en la vista activa.
        /// - Evita duplicados con dos filtros de cercanía (al elemento y entre tags).
        /// - Limita densidad con una rejilla de celdas (1 tag por celda y tipo).
        /// - El usuario puede escoger cualquier familia de tag cargada (validación de compatibilidad).
        /// </summary>
        private Result EjecutarTagConduits(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;

            // 3D no soportado para creación de tags por API
            if (view.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Vista no soportada", "No se pueden crear tags en vistas 3D. Cambia a una vista 2D.");
                return Result.Cancelled;
            }

            // Densidad (tamaño de celda)
            double cellFt = PedirTamanoCelda();
            if (cellFt <= 0) return Result.Cancelled;

            // Selección de tipo de tag desde TODOS los cargados (valida que sirvan para Conduits)
            bool tagOk;
            FamilySymbol tagType = PickAnyTagTypeForConduits(doc, out tagOk);
            if (tagType == null)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }
            if (!tagOk)
            {
                TaskDialog.Show("Tipo de tag incompatible",
                    "El tipo de tag seleccionado no puede etiquetar Conduits.\n" +
                    "Usa etiquetas de categoría 'Conduit Tags' o 'Multi-Category Tags'.");
                return Result.Cancelled;
            }

            // Conduits visibles en la vista
            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (!conduits.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron varillas (Conduits) en la vista activa.");
                return Result.Cancelled;
            }

            // Tags existentes relevantes en la vista (para anti-duplicados)
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category != null &&
                           (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags
                         || t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                .ToList();

            var tagHeadPositions = existingTags.Select(t => t.TagHeadPosition).ToList();
            var occupiedCells = new HashSet<string>(); // (ix:iy:typeId)

            using (Transaction tx = new Transaction(doc, "Taguear Varillas (Conduits)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                // Direcciones de la vista para desplazar el head del tag
                XYZ rightDir = view.RightDirection;
                XYZ downDir = -view.UpDirection;
                double dRight = 0.8; // offset horizontal (ft)
                double dDown = 0.4; // offset vertical (ft)

                foreach (var e in conduits)
                {
                    // Punto base: mitad de la curva si existe; si no, centro del bounding box
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

                    // Filtro 1: no tag cercano al elemento
                    if (AnyTagWithinRadius2D(tagHeadPositions, basePoint, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    // Posición candidata del tag
                    XYZ tagPos = basePoint + (rightDir * dRight) + (downDir * dDown);

                    // Filtro 2: no tag cercano a la posición candidata
                    if (AnyTagWithinRadius2D(tagHeadPositions, tagPos, MIN_CLEAR_TAG_TO_TAG_FT))
                        continue;

                    // Filtro 3: densidad (1 tag por celda y tipo)
                    int typeId = e.GetTypeId().IntegerValue;
                    string cellKey = MakeCellKey(basePoint, cellFt, typeId);
                    if (occupiedCells.Contains(cellKey))
                        continue;

                    // Crear tag
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

            TaskDialog.Show("Completado", "Tagueo de varillas finalizado con control de densidad.");
            return Result.Succeeded;
        }

        #endregion

        #region Cajas de inspección (Electrical Equipment)

        /// <summary>
        /// Taguea CAJAS DE INSPECCIÓN visibles en la vista activa.
        /// - El usuario elige el tipo de tag (OST_ElectricalEquipmentTags).
        /// - Anti-duplicados + densidad por celdas.
        /// </summary>
        private Result EjecutarTagCajas(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;

            if (view.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Vista no soportada", "No se pueden crear tags en vistas 3D. Cambia a una vista 2D.");
                return Result.Cancelled;
            }

            double cellFt = PedirTamanoCelda();
            if (cellFt <= 0) return Result.Cancelled;

            // Tipos de tag de "Electrical Equipment"
            var equipTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ElectricalEquipmentTags)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();

            if (!equipTagTypes.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag de Electrical Equipment disponibles (OST_ElectricalEquipmentTags).");
                return Result.Failed;
            }

            // Selección de tipo de tag
            List<string> tagNames = equipTagTypes.Select(t => $"{t.FamilyName} : {t.Name}").ToList();
            TagSelectionWindow tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }
            string selectedDisplay = tagWindow.SelectedTagType;
            var tagType = equipTagTypes.FirstOrDefault(t =>
                $"{t.FamilyName} : {t.Name}".Equals(selectedDisplay, StringComparison.Ordinal));

            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedDisplay}'.");
                return Result.Failed;
            }

            // Cajas visibles
            var cajas = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            if (!cajas.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron Cajas de inspección en la vista activa.");
                return Result.Cancelled;
            }

            // Tags existentes relevantes
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category != null &&
                           (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ElectricalEquipmentTags
                         || t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                .ToList();

            var tagHeadPositions = existingTags.Select(t => t.TagHeadPosition).ToList();
            var occupiedCells = new HashSet<string>();

            using (Transaction tx = new Transaction(doc, "Taguear Cajas de inspección"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                XYZ rightDir = view.RightDirection;
                XYZ downDir = -view.UpDirection;
                double dRight = 0.8;
                double dDown = 0.4;

                foreach (var e in cajas)
                {
                    // Punto base: LocationPoint o centro del bbox
                    XYZ basePoint = XYZ.Zero;
                    if (e.Location is LocationPoint lp) basePoint = lp.Point;
                    else
                    {
                        var bb = e.get_BoundingBox(view);
                        basePoint = (bb != null) ? (bb.Min + bb.Max) / 2.0 : XYZ.Zero;
                    }

                    // Filtros de cercanía
                    if (AnyTagWithinRadius2D(tagHeadPositions, basePoint, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    XYZ tagPos = basePoint + (rightDir * dRight) + (downDir * dDown);

                    if (AnyTagWithinRadius2D(tagHeadPositions, tagPos, MIN_CLEAR_TAG_TO_TAG_FT))
                        continue;

                    // Densidad por celda
                    int typeId = e.GetTypeId().IntegerValue;
                    string cellKey = MakeCellKey(basePoint, cellFt, typeId);
                    if (occupiedCells.Contains(cellKey))
                        continue;

                    // Crear tag
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

            TaskDialog.Show("Completado", "Se taguearon las Cajas de inspección con control de densidad.");
            return Result.Succeeded;
        }

        #endregion

        #region Bajantes verticales (Conduits)

        /// <summary>
        /// Taguea únicamente CONDUITS casi verticales (|dir.Z| >= 0.95) en la vista activa.
        /// - Usa misma lógica de densidad/anti-duplicados.
        /// - Permite elegir cualquier tag compatible con Conduits.
        /// </summary>
        private Result EjecutarTagBajantesVerticales(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;

            if (view.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Vista no soportada", "No se pueden crear tags en vistas 3D. Cambia a una vista 2D.");
                return Result.Cancelled;
            }

            double cellFt = PedirTamanoCelda();
            if (cellFt <= 0) return Result.Cancelled;

            // Elegir tipo de tag (de todos los cargados) validando compatibilidad con Conduits
            bool tagOk;
            FamilySymbol tagType = PickAnyTagTypeForConduits(doc, out tagOk);
            if (tagType == null)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }
            if (!tagOk)
            {
                TaskDialog.Show("Tipo de tag incompatible",
                    "El tipo de tag seleccionado no puede etiquetar Conduits.\n" +
                    "Usa etiquetas de categoría 'Conduit Tags' o 'Multi-Category Tags'.");
                return Result.Cancelled;
            }

            // Helper local: determina si el conduit es vertical y obtiene su punto medio
            bool IsVertical(Element e, out XYZ mid)
            {
                mid = XYZ.Zero;
                if (!(e.Location is LocationCurve lc) || lc.Curve == null) return false;

                Curve c = lc.Curve;
                XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1);
                mid = (p0 + p1) / 2.0;

                XYZ dir;
                if (c is Line ln) dir = ln.Direction;
                else dir = (p1 - p0).Normalize();

                return Math.Abs(dir.Z) >= 0.95;
            }

            // Conduits verticales visibles
            var conduits = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => IsVertical(e, out _))
                .ToList();

            if (!conduits.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron bajantes verticales en la vista activa.");
                return Result.Cancelled;
            }

            // Tags existentes relevantes
            var existingTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category != null &&
                            (t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_ConduitTags
                          || t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                .ToList();

            var tagHeadPositions = existingTags.Select(t => t.TagHeadPosition).ToList();
            var occupiedCells = new HashSet<string>();

            using (Transaction tx = new Transaction(doc, "Taguear bajantes verticales (Conduits)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                XYZ rightDir = view.RightDirection;
                XYZ downDir = -view.UpDirection;
                double dRight = 0.8, dDown = 0.4;

                foreach (var e in conduits)
                {
                    // Punto medio del tramo vertical
                    XYZ basePoint; IsVertical(e, out basePoint);

                    // Filtros de cercanía
                    if (AnyTagWithinRadius2D(tagHeadPositions, basePoint, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    XYZ tagPos = basePoint + (rightDir * dRight) + (downDir * dDown);

                    if (AnyTagWithinRadius2D(tagHeadPositions, tagPos, MIN_CLEAR_TAG_TO_TAG_FT))
                        continue;

                    // Densidad por celda + tipo
                    int typeId = e.GetTypeId().IntegerValue;
                    string cellKey = MakeCellKey(basePoint, cellFt, typeId);
                    if (occupiedCells.Contains(cellKey))
                        continue;

                    // Crear tag
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

            TaskDialog.Show("Completado", "Se taguearon los bajantes verticales.");
            return Result.Succeeded;
        }

        #endregion

        #region Codos (Conduit Fittings) con texto digitado por el usuario

        /// <summary>
        /// Taguea codos (Conduit Fittings) seleccionados por el usuario.
        /// - Pide texto al usuario y lo guarda en "Comentarios" de cada codo.
        /// - Permite elegir cualquier tipo de tag cargado, validando compatibilidad con accesorios de conduit.
        /// - Busca una posición libre alrededor del bbox para el head del tag.
        /// </summary>
        private Result EjecutarTagCodos(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Texto para mostrar en el tag (se guarda en "Comentarios")
            if (!PromptForText("Texto del tag", "Escribe el texto que deseas mostrar (se guardará en 'Comentarios'):", out string userText))
                return Result.Cancelled;
            if (string.IsNullOrWhiteSpace(userText)) return Result.Cancelled;

            // Trae TODOS los types de tag (cualquier categoría con "tag")
            var allTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(fs => fs.Category.Name)
                .ThenBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (!allTagTypes.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag cargados en el proyecto.");
                return Result.Failed;
            }

            // Mostrar compatibilidad
            var display = new List<string>(allTagTypes.Count);
            foreach (var fs in allTagTypes)
            {
                bool ok = IsConduitFittingCompatible(fs);
                string badge = ok ? "✓ compatible" : "× no compatible";
                display.Add($"{fs.Category.Name} | {fs.FamilyName} : {fs.Name}  [{badge}]");
            }

            // Elegir tipo de tag
            var tagWindow = new TagSelectionWindow(display);
            if (tagWindow.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

            int idx = display.IndexOf(tagWindow.SelectedTagType);
            if (idx < 0) return Result.Cancelled;
            FamilySymbol tagType = allTagTypes[idx];

            // Validación de compatibilidad para evitar excepciones al crear el tag
            if (!IsConduitFittingCompatible(tagType))
            {
                TaskDialog.Show("Tipo de tag incompatible",
                    "El tipo seleccionado no puede etiquetar Conduit Fittings.\n" +
                    "Usa etiquetas de categoría 'Conduit Fitting Tags' o 'Multi-Category Tags'.");
                return Result.Cancelled;
            }

            // Seleccionar codos
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

            // Crear tags
            using (var tx = new Transaction(doc, "Tag Codos (texto usuario)"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (var inst in codos)
                {
                    // Escribir "Comentarios"
                    var cmt = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (cmt != null && !cmt.IsReadOnly) cmt.Set(userText);

                    // Calcular una posición "libre" alrededor del bbox
                    XYZ tagHeadPos = XYZ.Zero;
                    var bbox = inst.get_BoundingBox(uidoc.ActiveView);

                    if (bbox != null)
                    {
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

                        // Fallback si todas las posiciones estaban ocupadas
                        if (tagHeadPos == XYZ.Zero)
                            tagHeadPos = new XYZ(bbox.Max.X + d, bbox.Min.Y - d, z);
                    }
                    else
                    {
                        // Si no hay bbox visible (raro), usar LocationPoint + offset
                        var loc = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        tagHeadPos = loc + new XYZ(0.8, -0.4, 0);
                    }

                    // Crear el tag
                    var tag = IndependentTag.Create(
                        doc, tagType.Id, uidoc.ActiveView.Id,
                        new Reference(inst), false,
                        TagOrientation.Horizontal, tagHeadPos);

                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagHeadPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado",
                "Se taguearon los codos. Asegúrate de que la familia del tag muestre el parámetro 'Comentarios'.");
            return Result.Succeeded;
        }

        #endregion

        #region Helpers (comunes)

        /// <summary>
        /// Ventana simple para pedir un texto al usuario.
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
                f.ClientSize = new System.Drawing.Size(380, 130);

                l.Text = prompt; l.AutoSize = true; l.Left = 12; l.Top = 12;
                tb.Left = 12; tb.Top = 40; tb.Width = 350;

                ok.Text = "Aceptar"; ok.Left = 206; ok.Top = 80; ok.DialogResult = WinForms.DialogResult.OK;
                cancel.Text = "Cancelar"; cancel.Left = 287; cancel.Top = 80; cancel.DialogResult = WinForms.DialogResult.Cancel;

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

        /// <summary>
        /// ¿Es un tipo de tag compatible para etiquetar Conduits?
        /// (Conduit Tags o Multi-Category Tags).
        /// </summary>
        private static bool IsConduitCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            int cat = fs.Category.Id.IntegerValue;
            return cat == (int)BuiltInCategory.OST_ConduitTags
                || cat == (int)BuiltInCategory.OST_MultiCategoryTags;
        }

        /// <summary>
        /// Selector de tipo de tag (muestra TODOS los tags del proyecto).
        /// Devuelve la compatibilidad con Conduits para avisar al usuario.
        /// </summary>
        private FamilySymbol PickAnyTagTypeForConduits(Document doc, out bool isCompatible)
        {
            isCompatible = false;

            var allTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Name.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(fs => fs.Category.Name)
                .ThenBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (!allTagTypes.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag cargados en el proyecto.");
                return null;
            }

            // Lista de display con badge de compatibilidad
            var display = new List<string>(allTagTypes.Count);
            foreach (var fs in allTagTypes)
            {
                bool ok = IsConduitCompatible(fs);
                string badge = ok ? "✓ compatible" : "× no compatible";
                display.Add($"{fs.Category.Name} | {fs.FamilyName} : {fs.Name}  [{badge}]");
            }

            var win = new TagSelectionWindow(display);
            if (win.ShowDialog() != WinForms.DialogResult.OK) return null;

            int idx = display.IndexOf(win.SelectedTagType);
            if (idx < 0) return null;

            var picked = allTagTypes[idx];
            isCompatible = IsConduitCompatible(picked);
            return picked;
        }

        /// <summary>
        /// ¿Es un tipo de tag compatible para etiquetar Conduit Fittings (codos)?
        /// (Conduit Fitting Tags o Multi-Category Tags).
        /// </summary>
        private static bool IsConduitFittingCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            int cat = fs.Category.Id.IntegerValue;
            return cat == (int)BuiltInCategory.OST_ConduitFittingTags
                || cat == (int)BuiltInCategory.OST_MultiCategoryTags;
        }

        /// <summary>
        /// Genera la clave de celda (ix:iy:typeId) para la rejilla de densidad en planta (ignora Z).
        /// </summary>
        private static string MakeCellKey(XYZ p, double cellFt, int typeId)
        {
            int ix = (int)Math.Floor(p.X / cellFt);
            int iy = (int)Math.Floor(p.Y / cellFt);
            return $"{ix}:{iy}:{typeId}";
        }

        /// <summary>
        /// Diálogo para elegir densidad (tamaño de celda). Devuelve pies.
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

        /// <summary>
        /// ¿Existe alguna cabeza de tag a una distancia (2D) <= radio del punto dado?
        /// </summary>
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

        /// <summary>
        /// Distancia al cuadrado en planta (ignora Z).
        /// </summary>
        private static double SquaredPlanarDistance(XYZ a, XYZ b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Filtro de selección para codos (accesorios de conduit).
        /// </summary>
        private class CodoSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;

                // Categoría correcta en la API
                if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_ConduitFitting)
                    return false;

                var fi = e as FamilyInstance;
                if (fi == null) return false;

                // Palabras clave típicas en familia/símbolo
                string fam = fi.Symbol?.Family?.Name ?? "";
                string sym = fi.Symbol?.Name ?? "";

                return fam.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("codo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       fam.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       sym.IndexOf("elbow", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public bool AllowReference(Reference r, XYZ p) => false;
        }

        #endregion
    }
}
