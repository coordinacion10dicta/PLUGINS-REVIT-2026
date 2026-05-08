// Definiciones de compilación condicional para las diferentes versiones de Revit
#if REVIT2018 || REVIT2019 || REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025 
#define REVIT
#endif

// En Revit 2020+ existen propiedades extra para el crop de anotaciones y el codo del líder
#if REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
#define SUPPORTS_ANNOTATION_CROP
#define SUPPORTS_LEADER_ELBOW
#endif

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    // Comando externo de Revit, se ejecuta manualmente (TransactionMode.Manual)
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_HVAC : IExternalCommand
    {
        // ===================== CONSTANTES / PARÁMETROS DE DISEÑO =====================

        // Distancias para ubicación de tags (en pies, porque Revit internamente usa pies)
        private const double HEAD_GAP_FT = 0.16; // Distancia de separación de la cabeza del tag (~1.9")
        private const double BOX_EDGE_GAP_FT = 0.12; // Separación desde el borde de la caja (~1.4")
        private const double PANEL_EDGE_GAP_FT = 0.12; // Separación desde el borde del panel (~1.4")
        private const double MIN_CLEAR_TAG_TO_ELEMENT_FT = 0.20; // Mínima distancia tag–elemento
        private const double MIN_CLEAR_TAG_TO_TAG_FT = 0.30;     // Mínima distancia tag–tag

        // Parámetros para buscar un “hueco libre” alrededor (para paneles/tableros)
        private const double FREE_HALFBOX_FT = 1.00;   // Semitamaño de la caja de búsqueda (~12")
        private const double SEARCH_STEP_FT = 0.50;    // Paso entre anillos de búsqueda (~6")
        private const int SEARCH_MAX_STEPS = 16;       // Máx. pasos (anillos) de búsqueda (~8 ft)

        // ===================== ENTRADA DEL COMANDO (método principal) =====================
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Documentos de Revit: interfaz de usuario y documento activo
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;

                // Bucle para que el usuario pueda ejecutar varias operaciones sin salir del comando
                while (true)
                {
                    // 1) Obtener una vista válida (planta, techo, sección, elevación, etc.)
                    View view = GetUsableView(uidoc);
                    if (view == null)
                    {
                        TaskDialog.Show("Vista no soportada",
                            "Abre una planta/techo/sección/elevación y vuelve a ejecutar el comando.");
                        return Result.Succeeded; // No hay vista válida, se sale sin error
                    }

                    // 2) Preguntar al usuario qué quiere taguear (rejillas, equipos, ductos, etc.)
                    var choice = AskWhatToTag();
                    if (choice == HvacChoice.Cancel) return Result.Succeeded;

                    // 3) Pre-chequeo: validar que existan elementos de esa categoría en la vista
                    BuiltInCategory? precheck = null;
                    if (choice == HvacChoice.AirTerminals) precheck = BuiltInCategory.OST_DuctTerminal;
                    else if (choice == HvacChoice.MechanicalEquipment) precheck = BuiltInCategory.OST_MechanicalEquipment;
                    else if (choice == HvacChoice.ElectricalEquipment) precheck = BuiltInCategory.OST_ElectricalEquipment;
                    else if (choice == HvacChoice.CommunicationDevices) precheck = BuiltInCategory.OST_CommunicationDevices;
                    else if (choice == HvacChoice.DuctAccessories) precheck = BuiltInCategory.OST_DuctAccessory;
                    else if (choice == HvacChoice.Ducts) precheck = BuiltInCategory.OST_DuctCurves;
                    else if (choice == HvacChoice.Pipes) precheck = BuiltInCategory.OST_PipeCurves;

                    // Si se definió una categoría a revisar…
                    if (precheck.HasValue)
                    {
                        // Se buscan elementos de esa categoría en la vista actual
                        bool none = !new FilteredElementCollector(doc, view.Id)
                                        .OfCategory(precheck.Value)
                                        .WhereElementIsNotElementType()
                                        .Any();
                        // Si no hay ninguno, se muestra advertencia y se vuelve al menú
                        if (none)
                        {
                            WarnNotFoundInView(FriendlyName(precheck.Value));
                            continue; // Vuelve al menú principal
                        }
                    }

                    // 4) Permitir al usuario seleccionar el tipo de Tag (FamilySymbol),
                    // filtrando por categorías compatibles según lo que eligió
                    FamilySymbol tagType = SelectTagTypeWithValidation(doc, choice);
                    if (tagType == null) continue; // Si cancela o es incompatible, regresar al menú

                    int placed = 0; // Contador de tags colocados

                    // 5) Según la elección, se llama a los métodos apropiados de tagueo
                    if (choice == HvacChoice.AirTerminals)
                    {
                        // Tagueo automático de rejillas (Air Terminals) con líder hacia fuera
                        placed = TagElementsCount(doc, view,
                            BuiltInCategory.OST_DuctTerminal,
                            IsAirTerminalCompatible, tagType,
                            PlacementMode.OutwardWithLeader);
                    }
                    else if (choice == HvacChoice.MechanicalEquipment)
                    {
                        // Tagueo de equipos mecánicos (Mechanical Equipment)
                        placed = TagElementsCount(doc, view,
                            BuiltInCategory.OST_MechanicalEquipment,
                            IsMechanicalEquipmentCompatible, tagType,
                            PlacementMode.BBoxWithLeader);
                    }
                    else if (choice == HvacChoice.ElectricalEquipment)
                    {
                        // Tagueo de tableros/paneles eléctricos
                        placed = TagElementsCount(doc, view,
                            BuiltInCategory.OST_ElectricalEquipment,
                            IsElectricalEquipmentCompatible, tagType,
                            PlacementMode.PanelTopWithLeader);
                    }
                    else if (choice == HvacChoice.CommunicationDevices)
                    {
                        // Tagueo de sensores (Communication Devices)
                        placed = TagElementsCount(doc, view,
                            BuiltInCategory.OST_CommunicationDevices,
                            IsCommunicationDeviceCompatible, tagType,
                            PlacementMode.BBoxWithLeader);
                    }
                    else if (choice == HvacChoice.DuctAccessories)
                    {
                        // Tagueo de accesorios de ductos
                        placed = TagElementsCount(doc, view,
                            BuiltInCategory.OST_DuctAccessory,
                            IsDuctAccessoryCompatible, tagType,
                            PlacementMode.BBoxWithLeader);
                    }
                    else if (choice == HvacChoice.Ducts)
                    {
                        // Tagueo por clic sobre ductos (el usuario va haciendo clic)
                        placed = TagDuctsByClickCount(uidoc, doc, view, tagType);
                    }
                    else // Pipes
                    {
                        // Tagueo por clic sobre tuberías (pipes)
                        placed = TagPipesByClickCount(uidoc, doc, view, tagType);
                    }

                    // 6) Mensaje final indicando cuántos tags se crearon y preguntando
                    // si quiere volver al menú o salir del comando
                    var td = new TaskDialog("Completado")
                    {
                        MainInstruction = $"Se crearon {placed} tag(s).",
                        MainContent = "Pulsa Aceptar para volver al menú o Cancelar para salir.",
                        CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
                    };
                    var res = td.Show();
                    if (res == TaskDialogResult.Cancel) return Result.Succeeded;
                    // Si el usuario presiona OK, el while(true) continúa y se repite el proceso
                }
            }
            catch (Exception ex)
            {
                // Si ocurre un error, se muestra el mensaje y se marca el comando como Fallido
                message = ex.ToString();
                TaskDialog.Show("MyTAGS_HVAC - Error", ex.Message);
                return Result.Failed;
            }
        }

        // ===================== MENÚS (elecciones del usuario) =====================

        // Enum que representa las opciones de categorías a taguear
        private enum HvacChoice
        {
            Cancel,
            AirTerminals,
            MechanicalEquipment,
            ElectricalEquipment,
            Ducts,
            Pipes,
            CommunicationDevices,
            DuctAccessories
        }

        // Primer menú principal: selecciona tipo general de elementos a taguear
        private HvacChoice AskWhatToTag()
        {
            var td = new TaskDialog("HVAC - ¿Qué deseas taguear?")
            {
                MainInstruction = "Selecciona la categoría",
                MainContent = "Luego filtras el tipo de tag por nombre.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Rejillas (Air Terminals)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Equipos / Sensores / Accesorios");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Tableros / Paneles (Electrical Equipment)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Redes (Ductos / Pipes)");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return HvacChoice.AirTerminals;
            if (r == TaskDialogResult.CommandLink2) return AskWhatEquipOrSensor(); // Submenú
            if (r == TaskDialogResult.CommandLink3) return HvacChoice.ElectricalEquipment;
            if (r == TaskDialogResult.CommandLink4) return AskWhatNetworkToTag();   // Submenú
            return HvacChoice.Cancel;
        }

        // Submenú para equipos, sensores y accesorios
        private HvacChoice AskWhatEquipOrSensor()
        {
            var td = new TaskDialog("HVAC - Equipos / Sensores / Accesorios")
            {
                MainInstruction = "Elige qué deseas taguear",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Equipos (Mechanical Equipment)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Sensores (Communication Devices)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Accesorios de ductos (Duct Accessories)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Regresar al menú");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return HvacChoice.MechanicalEquipment;
            if (r == TaskDialogResult.CommandLink2) return HvacChoice.CommunicationDevices;
            if (r == TaskDialogResult.CommandLink3) return HvacChoice.DuctAccessories;
            if (r == TaskDialogResult.CommandLink4) return AskWhatToTag(); // Vuelve al menú principal
            return HvacChoice.Cancel;
        }

        // Submenú para redes (ductos / pipes)
        private HvacChoice AskWhatNetworkToTag()
        {
            var td = new TaskDialog("HVAC - Redes")
            {
                MainInstruction = "Elige qué red deseas taguear",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ductos");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Pipes");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Regresar al menú");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return HvacChoice.Ducts;
            if (r == TaskDialogResult.CommandLink2) return HvacChoice.Pipes;
            if (r == TaskDialogResult.CommandLink3) return AskWhatToTag();
            return HvacChoice.Cancel;
        }

        // ===================== SELECTOR DE TAG (con validación por categoría) =====================

        // Muestra una ventana para seleccionar un tipo de tag (FamilySymbol) y valida que sea compatible
        private FamilySymbol SelectTagTypeWithValidation(Document doc, HvacChoice choice)
        {
            // 1) Se obtienen todos los FamilySymbol que son de categoría de Tags (terminan en "Tags")
            var allTagTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(IsAnyTagCategory)
                .OrderBy(fs => Enum.ToObject(typeof(BuiltInCategory), (long)fs.Category.Id.IntegerValue).ToString())
                .ThenBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .ToList();

            IEnumerable<FamilySymbol> source = allTagTypes;

            // 2) Filtros específicos para sensores y accesorios de ductos
            if (choice == HvacChoice.CommunicationDevices)
            {
                source = allTagTypes.Where(IsCommunicationDeviceCompatible);
                if (!source.Any())
                {
                    TaskDialog.Show("Sin tags compatibles",
                        "No hay tipos de tag para Communication Devices cargados en el proyecto.");
                    return null;
                }
            }
            else if (choice == HvacChoice.DuctAccessories)
            {
                source = allTagTypes.Where(IsDuctAccessoryCompatible);
                if (!source.Any())
                {
                    TaskDialog.Show("Sin tags compatibles",
                        "No hay tipos de tag para Duct Accessories cargados en el proyecto.");
                    return null;
                }
            }

            // 3) Texto a mostrar en el picker: "Familia : Tipo"
            var display = source.Select(fs => $"{fs.FamilyName} : {fs.Name}").ToList();

            // 4) Ventana personalizada (TagSelectionWindow) para escoger el tipo
            var picker = new TagSelectionWindow(display);
            var dlg = picker.ShowDialog();
            if (dlg != System.Windows.Forms.DialogResult.OK) return null;

            string selectedDisplay = picker.SelectedTagType;
            if (string.IsNullOrWhiteSpace(selectedDisplay)) return null;

            // 5) Volver del display al FamilySymbol original
            var tagType = source.FirstOrDefault(t =>
                $"{t.FamilyName} : {t.Name}".Equals(selectedDisplay, StringComparison.Ordinal));

            if (tagType == null)
            {
                TaskDialog.Show("No encontrado", "No se encontró el tipo de tag seleccionado.");
                return null;
            }

            // 6) Validar compatibilidad exacta según la categoría elegida
            bool ok =
                (choice == HvacChoice.AirTerminals) ? IsAirTerminalCompatible(tagType) :
                (choice == HvacChoice.MechanicalEquipment) ? IsMechanicalEquipmentCompatible(tagType) :
                (choice == HvacChoice.ElectricalEquipment) ? IsElectricalEquipmentCompatible(tagType) :
                (choice == HvacChoice.CommunicationDevices) ? IsCommunicationDeviceCompatible(tagType) :
                (choice == HvacChoice.DuctAccessories) ? IsDuctAccessoryCompatible(tagType) :
                (choice == HvacChoice.Ducts) ? IsDuctCompatible(tagType) :
                                               IsPipeCompatible(tagType);

            if (!ok)
            {
                // 7) Mensaje específico según tipo
                TaskDialog.Show("Tipo incompatible",
                    (choice == HvacChoice.AirTerminals) ? "Ese tipo de tag no etiqueta Rejillas (Air Terminals)." :
                    (choice == HvacChoice.MechanicalEquipment) ? "Ese tipo de tag no etiqueta Equipos (Mechanical Equipment)." :
                    (choice == HvacChoice.ElectricalEquipment) ? "Ese tipo de tag no etiqueta Tableros/Paneles (Electrical Equipment)." :
                    (choice == HvacChoice.CommunicationDevices) ? "Ese tipo de tag no etiqueta Communication Devices. Elige un 'Communication Device Tag' o un 'Multi-Category Tag'." :
                    (choice == HvacChoice.DuctAccessories) ? "Ese tipo de tag no etiqueta Duct Accessories. Elige un 'Duct Accessory Tag' o un 'Multi-Category Tag'." :
                    (choice == HvacChoice.Ducts) ? "Ese tipo de tag no etiqueta Ductos. Elige un 'Duct Tag' o un 'Multi-Category Tag'." :
                                                                  "Ese tipo de tag no etiqueta Pipes. Elige un 'Pipe Tag' o un 'Multi-Category Tag'.");
                return null;
            }

            return tagType;
        }

        // ===================== MODO DE COLOCACIÓN (estrategias de ubicación) =====================

        private enum PlacementMode
        {
            OutwardWithLeader,          // Air Terminals: tag hacia afuera respecto al centroide del grupo
            BBoxWithLeader,             // Equipos / Sensores: tag al borde de la caja, con líder corto
            BBoxNoLeader_PreferLeft,    // Reservado, tags al borde sin líder, preferencia a la izquierda
            PanelTopWithLeader          // Tableros: tag arriba del panel, con líder y buscando hueco
        }

        // Tagueo automático de muchos elementos en bloque
        private int TagElementsCount(Document doc, View view,
                                     BuiltInCategory bicCategory,
                                     Func<FamilySymbol, bool> compatibilityCheck,
                                     FamilySymbol tagType,
                                     PlacementMode placement)
        {
            // 1) Obtener todos los elementos de la categoría en la vista
            var elems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bicCategory)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            // Si no hay elementos, mostrar advertencia y salir
            if (!elems.Any())
            {
                WarnNotFoundInView(FriendlyName(bicCategory));
                return 0;
            }

            // 2) Calcular puntos base (anchor) de cada elemento (centro de bounding box o centro de curva)
            var bases = elems.Select(e => GetAnchorPoint(e, view)).ToList();
            // Centroide general de todos los elementos (para saber hacia dónde “salir” con el tag)
            var centroid = ComputeCentroid(bases);

            // 3) Obtener posiciones de tags ya existentes en la vista para evitar solapes
            var existingHeads = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => t.Category != null && compatibilityCheckByCategoryId(t.Category.Id.IntegerValue, bicCategory))
                .Select(t => t.TagHeadPosition)
                .Where(p => p != null)
                .ToList();

            // Direcciones locales de la vista (derecha y abajo)
            XYZ right = view.RightDirection;
            XYZ down = -view.UpDirection;

            int placed = 0;

            using (var tx = new Transaction(doc, $"HVAC | Tag {bicCategory}"))
            {
                tx.Start();

                // Activar el tipo de tag si no lo está
                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                // 4) Recorrer cada elemento y calcular la mejor posición del tag
                for (int i = 0; i < elems.Count; i++)
                {
                    var el = elems[i];
                    var basePt = bases[i];

                    // Evitar colocar un tag si otro tag ya está demasiado cerca del elemento
                    if (AnyTagWithinRadius2D(existingHeads, basePt, MIN_CLEAR_TAG_TO_ELEMENT_FT))
                        continue;

                    // Generar candidatos de posición (cabeza y codo de líder)
                    var candPairs = new List<HeadElbow>();
                    if (placement == PlacementMode.PanelTopWithLeader)
                        candPairs = GetTopPreferredCandidatesWithElbow(el, view, PANEL_EDGE_GAP_FT);
                    else if (placement == PlacementMode.BBoxWithLeader)
                        candPairs = GetBBoxSideCandidatesWithElbow(el, view, BOX_EDGE_GAP_FT, preferLeft: false);
                    else if (placement == PlacementMode.BBoxNoLeader_PreferLeft)
                        candPairs = GetBBoxSideCandidatesWithElbow(el, view, PANEL_EDGE_GAP_FT, preferLeft: true);
                    else
                        candPairs = GetOutwardCandidatesWithElbow(basePt, centroid, HEAD_GAP_FT, right, down);

                    // Si no hay candidatos, se usa el punto base
                    HeadElbow chosenHE = candPairs.Count > 0 ? candPairs[0] : new HeadElbow(basePt, basePt);

                    // Elegir el primer candidato que no colisione con otros tags
                    foreach (var he in candPairs)
                    {
                        if (!AnyTagWithinRadius2D(existingHeads, he.Head, MIN_CLEAR_TAG_TO_TAG_FT))
                        { chosenHE = he; break; }
                    }

                    // Clamp para que el punto esté dentro del crop de la vista
                    XYZ head = ClampOnlyIfOutside(chosenHE.Head, view);
                    XYZ elbow = ClampOnlyIfOutside(chosenHE.Elbow, view);

                    try
                    {
                        // 5) Crear el IndependentTag en Revit
                        var tag = IndependentTag.Create(
                            doc, tagType.Id, view.Id, new Reference(el),
                            false, TagOrientation.Horizontal, head);

                        tag.HasLeader = true;
#if !(REVIT2018 || REVIT2019)
                        tag.LeaderEndCondition = LeaderEndCondition.Attached;
#endif
                        tag.TagHeadPosition = head;
#if SUPPORTS_LEADER_ELBOW
                        // Si la versión de Revit lo permite, se define el codo del líder
                        try { tag.LeaderElbow = elbow; } catch { }
#endif
                        existingHeads.Add(head);
                        placed++;
                    }
                    catch
                    {
                        // Si falla con este elemento, se continúa con el siguiente
                    }
                }

                tx.Commit();
            }

            return placed;
        }

        // ===================== DUCTOS POR CLIC (uno a uno, interactivo) =====================

        private int TagDuctsByClickCount(UIDocument uidoc, Document doc, View view, FamilySymbol tagType)
        {
            // No permite taguear ductos en vistas 3D
            if (view.ViewType == ViewType.ThreeD) return 0;

            // Verificar si hay ductos visibles en la vista
            bool hayDuctos = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType()
                .Any();
            if (!hayDuctos)
            {
                WarnNotFoundInView(FriendlyName(BuiltInCategory.OST_DuctCurves));
                return 0;
            }

            // Activar tipo de tag en una transacción corta
            using (var tx = new Transaction(doc, "HVAC | Preparar tag de ductos"))
            {
                tx.Start();
                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }
                tx.Commit();
            }

            var filter = new DuctPointSelectionFilter(); // Filtro para seleccionar solo ductos
            int colocados = 0;

            // Bucle de selección hasta que el usuario presione ESC
            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.PointOnElement, filter,
                        "Haz clic SOBRE el ducto donde quieras el tag (ESC para terminar)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Usuario presionó ESC → salir del bucle
                    break;
                }

                if (pickRef == null) break;

                var el = doc.GetElement(pickRef.ElementId);
                if (el == null) continue;

                // Cabeza del tag donde el usuario hizo clic (ajustada al crop si hace falta)
                XYZ head = ClampOnlyIfOutside(pickRef.GlobalPoint, view);

                using (var tx = new Transaction(doc, "HVAC | Tag ducto (clic)"))
                {
                    tx.Start();
                    bool ok = false;
                    try
                    {
                        // Intento 1: crear tag sin líder (lo habitual para ductos)
                        var tag = IndependentTag.Create(
                            doc, tagType.Id, view.Id, new Reference(el),
                            false, TagOrientation.Horizontal, head);

                        tag.HasLeader = false;
                        tag.TagHeadPosition = head;
                        ok = true; colocados++;
                    }
                    catch
                    {
                        try
                        {
                            // Intento 2: si falla sin líder, se intenta con líder
                            var tag = IndependentTag.Create(
                                doc, tagType.Id, view.Id, new Reference(el),
                                false, TagOrientation.Horizontal, head);

                            tag.HasLeader = true;
#if !(REVIT2018 || REVIT2019)
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
#endif
                            tag.TagHeadPosition = head;
                            ok = true; colocados++;
                        }
                        catch
                        {
                            // Si aún así falla, no se hace nada (se avisará luego)
                        }
                    }
                    tx.Commit();

                    if (!ok) TaskDialog.Show("Ductos", "No se pudo crear el tag en ese punto.");
                }
            }

            return colocados;
        }

        // Filtro para que el usuario solo pueda seleccionar ductos al dar clic
        private class DuctPointSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                return e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DuctCurves;
            }

            // Se permite cualquier referencia sobre el ducto
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        // ===================== PIPES POR CLIC (similar a ductos) =====================

        private int TagPipesByClickCount(UIDocument uidoc, Document doc, View view, FamilySymbol tagType)
        {
            if (view.ViewType == ViewType.ThreeD) return 0;

            bool hayPipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Any();
            if (!hayPipes)
            {
                WarnNotFoundInView(FriendlyName(BuiltInCategory.OST_PipeCurves));
                return 0;
            }

            using (var tx = new Transaction(doc, "HVAC | Preparar tag de pipes"))
            {
                tx.Start();
                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }
                tx.Commit();
            }

            var filter = new PipePointSelectionFilter();
            int colocados = 0;

            while (true)
            {
                Reference pickRef = null;
                try
                {
                    pickRef = uidoc.Selection.PickObject(
                        ObjectType.PointOnElement, filter,
                        "Haz clic SOBRE la tubería (Pipe) donde quieras el tag (ESC para terminar)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                if (pickRef == null) break;

                var el = doc.GetElement(pickRef.ElementId);
                if (el == null) continue;

                XYZ head = ClampOnlyIfOutside(pickRef.GlobalPoint, view);

                using (var tx = new Transaction(doc, "HVAC | Tag pipe (clic)"))
                {
                    tx.Start();
                    bool ok = false;
                    try
                    {
                        var tag = IndependentTag.Create(
                            doc, tagType.Id, view.Id, new Reference(el),
                            false, TagOrientation.Horizontal, head);

                        tag.HasLeader = false; // Pipe tags normalmente sin líder
                        tag.TagHeadPosition = head;
                        ok = true; colocados++;
                    }
                    catch
                    {
                        try
                        {
                            var tag = IndependentTag.Create(
                                doc, tagType.Id, view.Id, new Reference(el),
                                false, TagOrientation.Horizontal, head);

                            tag.HasLeader = true;
#if !(REVIT2018 || REVIT2019)
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
#endif
                            tag.TagHeadPosition = head;
                            ok = true; colocados++;
                        }
                        catch { }
                    }
                    tx.Commit();

                    if (!ok) TaskDialog.Show("Pipes", "No se pudo crear el tag en ese punto.");
                }
            }

            return colocados;
        }

        // Filtro para que el usuario solo pueda seleccionar tuberías
        private class PipePointSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;
                return e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves;
            }
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        // ===================== HELPERS GENERALES =====================

        // Nombre “amigable” para mostrar en diálogos según categoría
        private static string FriendlyName(BuiltInCategory bic)
        {
            switch (bic)
            {
                case BuiltInCategory.OST_DuctTerminal: return "Rejillas (Air Terminals)";
                case BuiltInCategory.OST_MechanicalEquipment: return "Equipos (Mechanical Equipment)";
                case BuiltInCategory.OST_ElectricalEquipment: return "Tableros / Paneles (Electrical Equipment)";
                case BuiltInCategory.OST_CommunicationDevices: return "Sensores (Communication Devices)";
                case BuiltInCategory.OST_DuctAccessory: return "Accesorios de ductos (Duct Accessories)";
                case BuiltInCategory.OST_DuctCurves: return "Ductos";
                case BuiltInCategory.OST_PipeCurves: return "Pipes";
                default: return "Categoría";
            }
        }

        // Diálogo genérico cuando no se encuentran elementos de la categoría en la vista
        private static void WarnNotFoundInView(string nombre)
        {
            TaskDialog.Show("Sin elementos en vista",
                $"En la vista activa no se encontró el elemento que se desea tagear ({nombre}).");
        }

        // Determina si una vista es soportada por el comando
        private static bool IsSupported(View v)
        {
            return v != null && !v.IsTemplate &&
                   (v.ViewType == ViewType.FloorPlan
                 || v.ViewType == ViewType.CeilingPlan
                 || v.ViewType == ViewType.EngineeringPlan
                 || v.ViewType == ViewType.Section
                 || v.ViewType == ViewType.Elevation
                 || v.ViewType == ViewType.Detail);
        }

        // Intenta usar la vista activa; si no es válida, busca otra vista abierta o cualquier vista soportada
        private static View GetUsableView(UIDocument uidoc)
        {
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
            var gv = uidoc.ActiveGraphicalView;
            if (IsSupported(gv)) return gv;
#endif
            var v = uidoc.ActiveView;
            if (IsSupported(v)) return v;

            var doc = uidoc.Document;
            // Buscar entre las UIViews abiertas
            foreach (var uiv in uidoc.GetOpenUIViews())
            {
                var vv = doc.GetElement(uiv.ViewId) as View;
                if (IsSupported(vv)) { uidoc.ActiveView = vv; return vv; }
            }
            // Si no, buscar cualquier vista soportada en el documento
            var any = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(IsSupported);
            if (any != null) { uidoc.ActiveView = any; return any; }
            return null;
        }

        // Devuelve true si el FamilySymbol pertenece a una categoría de Tags (termina en "Tags")
        private static bool IsAnyTagCategory(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name.EndsWith("Tags", StringComparison.Ordinal);
        }

        // ===================== FUNCIONES DE COMPATIBILIDAD DE TAGS =====================

        private static bool IsAirTerminalCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_DuctTerminalTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsMechanicalEquipmentCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_MechanicalEquipmentTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsElectricalEquipmentCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_ElectricalEquipmentTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsDuctCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_DuctTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsPipeCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_PipeTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsCommunicationDeviceCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_CommunicationDeviceTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        private static bool IsDuctAccessoryCompatible(FamilySymbol fs)
        {
            if (fs?.Category == null) return false;
            long c = fs.Category.Id.IntegerValue;
            string name = Enum.ToObject(typeof(BuiltInCategory), c).ToString();
            return name == nameof(BuiltInCategory.OST_DuctAccessoryTags)
                || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
        }

        // Chequeo de compatibilidad usando el CategoryId del tag y la categoría del elemento
        private static bool compatibilityCheckByCategoryId(long catId, BuiltInCategory target)
        {
            string name = Enum.ToObject(typeof(BuiltInCategory), catId).ToString();
            if (target == BuiltInCategory.OST_DuctTerminal)
                return name == nameof(BuiltInCategory.OST_DuctTerminalTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_MechanicalEquipment)
                return name == nameof(BuiltInCategory.OST_MechanicalEquipmentTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_ElectricalEquipment)
                return name == nameof(BuiltInCategory.OST_ElectricalEquipmentTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_CommunicationDevices)
                return name == nameof(BuiltInCategory.OST_CommunicationDeviceTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_DuctCurves)
                return name == nameof(BuiltInCategory.OST_DuctTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_PipeCurves)
                return name == nameof(BuiltInCategory.OST_PipeTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            if (target == BuiltInCategory.OST_DuctAccessory)
                return name == nameof(BuiltInCategory.OST_DuctAccessoryTags) || name == nameof(BuiltInCategory.OST_MultiCategoryTags);
            return false;
        }

        // ===================== GEOMETRÍA Y POSICIONAMIENTO =====================

        // Obtiene un “punto ancla” para el elemento (centro de curva o centro de bounding box)
        private static XYZ GetAnchorPoint(Element e, View v)
        {
            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                try { return lc.Curve.Evaluate(0.5, true); } catch { }
            }
            if (e.Location is LocationPoint lp) return lp.Point;
            var bb = e.get_BoundingBox(v);
            return (bb != null) ? (bb.Min + bb.Max) / 2.0 : XYZ.Zero;
        }

        // Calcula el centroide de una lista de puntos
        private static XYZ ComputeCentroid(IList<XYZ> pts)
        {
            if (pts == null || pts.Count == 0) return XYZ.Zero;
            double sx = 0, sy = 0, sz = 0;
            foreach (var p in pts) { sx += p.X; sy += p.Y; sz += p.Z; }
            return new XYZ(sx / pts.Count, sy / pts.Count, sz / pts.Count);
        }

        // Devuelve true si algún tag está dentro de un radio 2D alrededor del punto dado
        private static bool AnyTagWithinRadius2D(IEnumerable<XYZ> heads, XYZ p, double r)
        {
            double r2 = r * r;
            foreach (var h in heads)
            {
                if (h == null) continue;
                double dx = h.X - p.X, dy = h.Y - p.Y;
                if ((dx * dx + dy * dy) <= r2) return true;
            }
            return false;
        }

        // Estructura para guardar la posición de cabeza de tag y codo de líder
        private struct HeadElbow
        {
            public XYZ Head;
            public XYZ Elbow;
            public HeadElbow(XYZ h, XYZ e) { Head = h; Elbow = e; }
        }

        // Genera candidatos de posición de tag alejándose del centroide (para Air Terminals)
        private static List<HeadElbow> GetOutwardCandidatesWithElbow(XYZ basePt, XYZ centroid, double gapFt, XYZ right, XYZ down)
        {
            var list = new List<HeadElbow>();
            XYZ elbow = basePt;

            var vdir = basePt - centroid;
            XYZ primaryDir; int sign;
            // Elige si es mejor “salir” en X (derecha/izquierda) o en Y (arriba/abajo)
            if (Math.Abs(vdir.X) >= Math.Abs(vdir.Y)) { primaryDir = right; sign = vdir.X >= 0 ? 1 : -1; }
            else { primaryDir = down; sign = vdir.Y >= 0 ? 1 : -1; }

            // Genera varios puntos de cabeza en esa dirección y algunas alternativas
            XYZ main = basePt + (primaryDir * (sign * gapFt));
            list.Add(new HeadElbow(main, elbow));
            list.Add(new HeadElbow(basePt + (primaryDir * (sign * gapFt * 1.5)), elbow));
            list.Add(new HeadElbow(basePt + (primaryDir * (sign * gapFt * 2.0)), elbow));
            list.Add(new HeadElbow(basePt + (primaryDir.Negate() * (gapFt * 0.8)), elbow));
            list.Add(new HeadElbow(basePt + ((primaryDir == right ? down : right) * gapFt), elbow));
            return list;
        }

        // Genera candidatos alrededor de la bounding box del elemento (para equipos/sensores)
        private static List<HeadElbow> GetBBoxSideCandidatesWithElbow(Element el, View v, double gapFt, bool preferLeft)
        {
            var list = new List<HeadElbow>();
            var bb = el.get_BoundingBox(v);
            if (bb == null) return list;

            double z = (bb.Min.Z + bb.Max.Z) / 2.0;
            XYZ c = (bb.Min + bb.Max) / 2.0;

            XYZ rightEdge = new XYZ(bb.Max.X, c.Y, z);
            XYZ leftEdge = new XYZ(bb.Min.X, c.Y, z);
            XYZ topEdge = new XYZ(c.X, bb.Max.Y, z);
            XYZ bottomEdge = new XYZ(c.X, bb.Min.Y, z);

            // Preferencia izquierda o derecha según el parámetro
            if (preferLeft)
            {
                list.Add(new HeadElbow(new XYZ(bb.Min.X - gapFt, c.Y, z), leftEdge));
                list.Add(new HeadElbow(new XYZ(bb.Max.X + gapFt, c.Y, z), rightEdge));
            }
            else
            {
                list.Add(new HeadElbow(new XYZ(bb.Max.X + gapFt, c.Y, z), rightEdge));
                list.Add(new HeadElbow(new XYZ(bb.Min.X - gapFt, c.Y, z), leftEdge));
            }
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X, bb.Min.Y - gapFt, z), bottomEdge));

            // Candidatos un poco más alejados
            if (preferLeft)
            {
                list.Add(new HeadElbow(new XYZ(bb.Min.X - gapFt * 1.5, c.Y, z), leftEdge));
                list.Add(new HeadElbow(new XYZ(bb.Max.X + gapFt * 1.5, c.Y, z), rightEdge));
            }
            else
            {
                list.Add(new HeadElbow(new XYZ(bb.Max.X + gapFt * 1.5, c.Y, z), rightEdge));
                list.Add(new HeadElbow(new XYZ(bb.Min.X - gapFt * 1.5, c.Y, z), leftEdge));
            }
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt * 1.5, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X, bb.Min.Y - gapFt * 1.5, z), bottomEdge));

            return list;
        }

        // Genera candidatos por encima del elemento (para paneles/tableros)
        private static List<HeadElbow> GetTopPreferredCandidatesWithElbow(Element el, View v, double gapFt)
        {
            var list = new List<HeadElbow>();
            var bb = el.get_BoundingBox(v);
            if (bb == null) return list;

            double z = (bb.Min.Z + bb.Max.Z) / 2.0;
            XYZ c = (bb.Min + bb.Max) / 2.0;
            XYZ topEdge = new XYZ(c.X, bb.Max.Y, z);

            // Varios puntos por encima del elemento y ligeramente desplazados en X
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt * 1.4, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X + gapFt * 0.6, bb.Max.Y + gapFt, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X - gapFt * 0.6, bb.Max.Y + gapFt, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X + gapFt, bb.Max.Y + gapFt * 1.4, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X - gapFt, bb.Max.Y + gapFt * 1.4, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt * 1.8, z), topEdge));
            list.Add(new HeadElbow(new XYZ(c.X, bb.Max.Y + gapFt * 2.2, z), topEdge));
            return list;
        }

        // Comprueba si una “caja” alrededor del punto está libre de otros elementos (para buscar huecos)
        private static bool IsFreeSpot(Document doc, View view, Element avoid, XYZ head)
        {
            XYZ min = new XYZ(head.X - FREE_HALFBOX_FT, head.Y - FREE_HALFBOX_FT, head.Z - 10.0);
            XYZ max = new XYZ(head.X + FREE_HALFBOX_FT, head.Y + FREE_HALFBOX_FT, head.Z + 10.0);
            var outline = new Outline(min, max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            return !new FilteredElementCollector(doc, view.Id)
                .WherePasses(bbFilter)
                .Where(e =>
                    e != null &&
                    e.Id != avoid?.Id &&
                    !(e is View) &&
                    e.Category != null)
                .Any();
        }

        // Busca alrededor de un punto la posición libre más cercana según una lista de direcciones
        private static XYZ FindNearestFreeSpot(Document doc, View view, Element avoid, XYZ seed, IList<XYZ> directions)
        {
            if (IsFreeSpot(doc, view, avoid, seed)) return seed;

            double step = SEARCH_STEP_FT;
            for (int ring = 1; ring <= SEARCH_MAX_STEPS; ring++)
            {
                foreach (var dir in directions)
                {
                    XYZ p = seed + dir.Multiply(step * ring);
                    if (IsFreeSpot(doc, view, avoid, p)) return p;
                }
            }
            // Si no encuentra nada, devuelve el punto original
            return seed;
        }

        // Verifica si un punto está dentro de algún crop (modelo o anotación)
        private static bool IsInsideAnyCrop(XYZ p, View v)
        {
#if SUPPORTS_ANNOTATION_CROP
            if (TryGetAnnotationOutline(v, out var aol) && IsInsideOutline(p, aol)) return true;
#endif
            if (TryGetViewOutline(v, out var mol) && IsInsideOutline(p, mol)) return true;

#if SUPPORTS_ANNOTATION_CROP
            return !(v.CropBoxActive || v.AnnotationCropActive);
#else
            return !v.CropBoxActive;
#endif
        }

        // Si el punto está fuera del crop, lo “clampa” a los límites; si está dentro, lo deja igual
        private static XYZ ClampOnlyIfOutside(XYZ p, View v)
        {
            if (IsInsideAnyCrop(p, v)) return p;

#if SUPPORTS_ANNOTATION_CROP
            if (TryGetAnnotationOutline(v, out var aol))
            {
                var c = ClampToOutline(p, aol);
                if (IsInsideOutline(c, aol)) return c;
            }
#endif
            if (TryGetViewOutline(v, out var mol))
            {
                var c = ClampToOutline(p, mol);
                if (IsInsideOutline(c, mol)) return c;
            }
            return p;
        }

        // Obtiene el Outline del crop de modelo
        private static bool TryGetViewOutline(View v, out Outline modelOutline)
        {
            modelOutline = null;
            try
            {
                if (!v.CropBoxActive) return false;
                var bb = v.CropBox;
                if (bb == null) return false;
                modelOutline = new Outline(bb.Min, bb.Max);
                return true;
            }
            catch { return false; }
        }

        // Obtiene el Outline del annotation crop (si está disponible en la versión de Revit)
        private static bool TryGetAnnotationOutline(View v, out Outline annoOutline)
        {
            annoOutline = null;
#if SUPPORTS_ANNOTATION_CROP
            try
            {
                if (!v.AnnotationCropActive) return false;
                var bb = v.GetAnnotationCropBox();
                if (bb == null) return false;
                annoOutline = new Outline(bb.Min, bb.Max);
                return true;
            }
            catch { return false; }
#else
            return false;
#endif
        }

        // “Recorta” el punto a los límites del Outline (en X e Y)
        private static XYZ ClampToOutline(XYZ p, Outline o)
        {
            double x = Math.Min(Math.Max(p.X, o.MinimumPoint.X), o.MaximumPoint.X);
            double y = Math.Min(Math.Max(p.Y, o.MinimumPoint.Y), o.MaximumPoint.Y);
            return new XYZ(x, y, p.Z);
        }

        // Verifica si un punto está dentro de un Outline (en X e Y)
        private static bool IsInsideOutline(XYZ p, Outline o)
        {
            return p.X >= o.MinimumPoint.X && p.X <= o.MaximumPoint.X
                && p.Y >= o.MinimumPoint.Y && p.Y <= o.MaximumPoint.Y;
        }
    }
}
