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

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MiNamespace
{
    //Comando principal: muestra menú y ejecuta los flujos de tageo (Alimentadores, Cajas, Codos).
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_ELE : IExternalCommand
    {
        private enum MenuOption { Alimentadores, Cajas, Codos, Cancelar }
        private enum SubChoice { Aceptar, Regresar, Cancelar }

        #region Entrada principal y menús (TaskDialog)

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Bucle de navegación: Menú -> Submenú -> Acción
            while (true)
            {
                var opt = MostrarMenuPrincipal();
                if (opt == MenuOption.Cancelar) return Result.Cancelled;

                var sub = MostrarSubmenu(opt);
                if (sub == SubChoice.Cancelar) return Result.Cancelled;
                if (sub == SubChoice.Regresar) continue;

                switch (opt)
                {
                    case MenuOption.Alimentadores: return EjecutarTagAlimentadores(commandData);
                    case MenuOption.Cajas: return EjecutarTagCajas(commandData);
                    case MenuOption.Codos: return EjecutarTagCodos(commandData);
                }
            }
        }
        //Menú principal de selección de flujo.
        private MenuOption MostrarMenuPrincipal()
        {
            var td = new TaskDialog("TAGS ELE - Menú principal")
            {
                MainInstruction = "¿Qué deseas taguear?",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Taguear Alimentadores");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Taguear Cajas de derivación");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Taguear Codos");

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1) return MenuOption.Alimentadores;
            if (r == TaskDialogResult.CommandLink2) return MenuOption.Cajas;
            if (r == TaskDialogResult.CommandLink3) return MenuOption.Codos;
            return MenuOption.Cancelar;
        }

        //Submenú de confirmación por flujo
        private SubChoice MostrarSubmenu(MenuOption opt)
        {
            string titulo = opt == MenuOption.Alimentadores ? "Alimentadores"
                         : opt == MenuOption.Cajas ? "Cajas de derivación"
                         : "Codos";

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

        #region Alimentadores
        //Taguea equipos eléctricos (tableros, transformadores, etc.) en la vista activa.
        //Busca una zona libre alrededor del equipo y deja el líder limpio (Attached).
        private Result EjecutarTagAlimentadores(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1) Recolectar tipos de tag disponibles
            var availableTags = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Name.ToLower().Contains("tag"))
                .ToList();

            if (!availableTags.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag disponibles en el modelo.");
                return Result.Failed;
            }

            // 2) Selección de tipo de tag
            List<string> tagNames = availableTags.Select(t => t.Name).ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != DialogResult.OK)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }
            string selectedTag = tagWindow.SelectedTagType;
            FamilySymbol tagType = availableTags.FirstOrDefault(t => t.Name == selectedTag);
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedTag}'.");
                return Result.Failed;
            }

            // 3) Elementos objetivo: equipos eléctricos en la vista
            var alimentadores = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            if (!alimentadores.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron alimentadores en la vista activa.");
                return Result.Cancelled;
            }

            // 4) Colocación de tags
            using (var tx = new Transaction(doc, "Tag Alimentadores"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (var inst in alimentadores)
                {
                    XYZ tagHeadPos = XYZ.Zero;
                    var bbox = inst.get_BoundingBox(uidoc.ActiveView);

                    if (bbox != null)
                    {
                        // Candidatos: dcha/izq/arriba/abajo (offset ~1 pie)
                        XYZ c = (bbox.Min + bbox.Max) / 2; double z = c.Z; double d = 1.0;
                        var candidates = new[]
                        {
                            new XYZ(bbox.Max.X + d, c.Y, z),  // derecha
                            new XYZ(bbox.Min.X - d, c.Y, z),  // izquierda
                            new XYZ(c.X, bbox.Max.Y + d, z),  // arriba
                            new XYZ(c.X, bbox.Min.Y - d, z)   // abajo
                        };

                        foreach (var p in candidates)
                        {
                            var outline = new Outline(p - new XYZ(0.3, 0.3, 0), p + new XYZ(0.3, 0.3, 0));
                            var filter = new BoundingBoxIntersectsFilter(outline);
                            bool ocupado = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                                .WherePasses(filter)
                                .Where(e => e.Id != inst.Id && !(e is Autodesk.Revit.DB.View))
                                .Any();
                            if (!ocupado) { tagHeadPos = p; break; }
                        }

                        // Fallback abajo-derecha
                        if (tagHeadPos == XYZ.Zero)
                            tagHeadPos = new XYZ(bbox.Max.X + d, bbox.Min.Y - d, z);
                    }
                    else
                    {
                        // Fallback sin bbox
                        var loc = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        tagHeadPos = loc + new XYZ(1.0, -0.5, 0);
                    }

                    var tag = IndependentTag.Create(doc, tagType.Id, uidoc.ActiveView.Id,
                                                    new Reference(inst), false,
                                                    TagOrientation.Horizontal, tagHeadPos);
                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagHeadPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon los alimentadores correctamente.");
            return Result.Succeeded;
        }

        #endregion

        #region Cajas de derivación

        //Taguea cajas de derivación. El tag se intenta colocar ARRIBA; si hay obstrucción,
        //busca DERECHA → IZQUIERDA → ABAJO con distancias crecientes y evita solapes.
        private Result EjecutarTagCajas(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1) Recolectar tipos de tag (filtrando por nombres que sugieran "caja/deriv/junction")
            var allTagSymbols = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            var cajaTags = allTagSymbols
                .Where(fs => fs.Category != null && fs.Category.Name.ToLower().Contains("tag"))
                .Where(fs =>
                {
                    string fam = fs.Family?.Name ?? "";
                    string nam = fs.Name ?? "";
                    return fam.IndexOf("caja", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fam.IndexOf("deriv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fam.IndexOf("junction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           nam.IndexOf("caja", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           nam.IndexOf("deriv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           nam.IndexOf("junction", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            // Fallback: ofrecer todos los tags si no encontró por nombre
            if (!cajaTags.Any())
                cajaTags = allTagSymbols.Where(fs => fs.Category != null && fs.Category.Name.ToLower().Contains("tag")).ToList();

            if (!cajaTags.Any())
            {
                TaskDialog.Show("Error", "No hay tipos de tag disponibles en el modelo.");
                return Result.Failed;
            }

            // 2) Selección de tipo de tag
            var tagNames = cajaTags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != DialogResult.OK)
            {
                TaskDialog.Show("Cancelado", "No se seleccionó ningún tipo de tag.");
                return Result.Cancelled;
            }
            string selectedTag = tagWindow.SelectedTagType;
            var tagType = cajaTags.FirstOrDefault(t => t.Name.Equals(selectedTag, StringComparison.OrdinalIgnoreCase));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedTag}'.");
                return Result.Failed;
            }

            // 3) Elementos objetivo: cajas (pueden estar en varias categorías)
            var cajas = new FilteredElementCollector(doc, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    if (e.Category == null) return false;
                    int cat = e.Category.Id.IntegerValue;
                    // FIX: usar ConduitFittings (plural)
                    return cat == (int)BuiltInCategory.OST_ConduitFitting
                        || cat == (int)BuiltInCategory.OST_ElectricalFixtures
                        || cat == (int)BuiltInCategory.OST_ElectricalEquipment;
                })
                .OfType<FamilyInstance>()
                .Where(IsCajaDerivacion) // heurística por nombre: "caja/deriv/junction/JB/box"
                .ToList();

            if (!cajas.Any())
            {
                TaskDialog.Show("Aviso", "No se encontraron cajas de derivación en la vista activa.");
                return Result.Cancelled;
            }

            // 4) Colocación de tags con búsqueda de zona libre
            using (var tx = new Transaction(doc, "Tag Cajas de Derivación"))
            {
                tx.Start();

                if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                foreach (var inst in cajas)
                {
                    XYZ tagHeadPos;
                    var bbox = inst.get_BoundingBox(uidoc.ActiveView);

                    if (bbox != null)
                    {
                        tagHeadPos = FindFreeTagPos(doc, uidoc.ActiveView, inst);
                    }
                    else
                    {
                        // Fallback: 600 mm arriba del punto de inserción
                        var loc = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        tagHeadPos = loc + new XYZ(0, Ft(600), 0);
                    }

                    var tag = IndependentTag.Create(doc, tagType.Id, uidoc.ActiveView.Id,
                                                    new Reference(inst), false,
                                                    TagOrientation.Horizontal, tagHeadPos);
                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagHeadPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon las cajas de derivación correctamente.");
            return Result.Succeeded;
        }

        #endregion

        #region Codos

        //Permite al usuario seleccionar manualmente los codos a taguear,
        //restringe el tipo de tag a la familia/nombres indicados y posiciona en zona libre.
        private Result EjecutarTagCodos(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1) Tipos de tag permitidos (familia/nombres)
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

            // 2) Elegir tipo de tag
            var tagNames = conduitFittingTags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var tagWindow = new TagSelectionWindow(tagNames);
            if (tagWindow.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            string selectedTag = tagWindow.SelectedTagType;
            var tagType = conduitFittingTags.FirstOrDefault(t => t.Name.Equals(selectedTag, StringComparison.OrdinalIgnoreCase));
            if (tagType == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de tag '{selectedTag}'.");
                return Result.Failed;
            }

            // 3) Usuario selecciona codos a taguear
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

            var codos = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
            if (!codos.Any())
            {
                TaskDialog.Show("Aviso", "No se seleccionaron codos.");
                return Result.Cancelled;
            }

            // 4) Colocar tags
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
                        XYZ c = (bbox.Min + bbox.Max) / 2; double z = c.Z; double d = 0.8;
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
                                .Where(e => e.Id != inst.Id && !(e is Autodesk.Revit.DB.View))
                                .Any();
                            if (!ocupado) { tagHeadPos = p; break; }
                        }

                        if (tagHeadPos == XYZ.Zero)
                            tagHeadPos = new XYZ(bbox.Max.X + d, bbox.Min.Y - d, z); // fallback
                    }
                    else
                    {
                        var loc = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        tagHeadPos = loc + new XYZ(0.8, -0.4, 0);
                    }

                    var tag = IndependentTag.Create(doc, tagType.Id, uidoc.ActiveView.Id,
                                                    new Reference(inst), false,
                                                    TagOrientation.Horizontal, tagHeadPos);
                    tag.HasLeader = true;
                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                    tag.TagHeadPosition = tagHeadPos;
                }

                tx.Commit();
            }

            TaskDialog.Show("Completado", "Se taguearon los codos correctamente.");
            return Result.Succeeded;
        }

        //Filtro de selección para permitir solo codos (accesorios de conduit) en PickObjects
        class CodoSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                if (e?.Category == null) return false;

                // FIX: usar ConduitFittings (plural)
                if (e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_ConduitFitting)
                    return false;

                var fi = e as FamilyInstance;
                if (fi == null) return false;

                // Palabras clave típicas
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

        #region Helpers compartidos

        //Heurística: identifica cajas de derivación por nombre de familia/símbolo
        private static bool IsCajaDerivacion(FamilyInstance fi)
        {
            if (fi?.Symbol == null) return false;
            string fam = fi.Symbol.Family?.Name ?? "";
            string sym = fi.Symbol.Name ?? "";
            return fam.IndexOf("caja", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("deriv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("junction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sym.IndexOf("caja", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sym.IndexOf("deriv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sym.IndexOf("junction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sym.IndexOf("jb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sym.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        //Convierte milímetros a pies (unidades internas de Revit)
        private static double Ft(double mm) => mm / 304.8;

        //Comprueba si el rectángulo alrededor del punto propuesto está libre de colisiones.
        //Se ignoran la propia vista, tags y notas de texto para no "repeler" otras anotaciones.

        private static bool IsFreeAround(Document doc, Autodesk.Revit.DB.View view, Element ignore, XYZ p, double halfX, double halfY)
        {
            var outline = new Outline(p - new XYZ(halfX, halfY, 0), p + new XYZ(halfX, halfY, 0));
            var filter = new BoundingBoxIntersectsFilter(outline);

            return !new FilteredElementCollector(doc, view.Id)
                .WherePasses(filter)
                .Where(e => e.Id != ignore.Id
                         && !(e is Autodesk.Revit.DB.View)
                         && !(e is Autodesk.Revit.DB.IndependentTag)
                         && !(e is Autodesk.Revit.DB.TextNote))
                .Any();
        }

        //Busca la mejor posición para un tag alrededor de un FamilyInstance:
        // prioridad ARRIBA → DERECHA → IZQUIERDA → ABAJO, con distancias crecientes.

        private static XYZ FindFreeTagPos(Document doc, Autodesk.Revit.DB.View view, FamilyInstance inst)
        {
            var bb = inst.get_BoundingBox(view);
            if (bb == null) return (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;

            var center = (bb.Min + bb.Max) / 2.0;
            double z = center.Z;

            // Tamaño “virtual” del tag para test de colisión
            double halfX = Ft(2000);
            double halfY = Ft(1000);

            // Distancias de prueba: 300, 450, 600, 900 mm
            double[] steps = { Ft(300), Ft(450), Ft(600), Ft(900) };

            foreach (double d in steps)
            {
                XYZ up = new XYZ(center.X, bb.Max.Y + d, z);
                if (IsFreeAround(doc, view, inst, up, halfX, halfY)) return up;

                XYZ right = new XYZ(bb.Max.X + d, center.Y, z);
                if (IsFreeAround(doc, view, inst, right, halfX, halfY)) return right;

                XYZ left = new XYZ(bb.Min.X - d, center.Y, z);
                if (IsFreeAround(doc, view, inst, left, halfX, halfY)) return left;

                XYZ down = new XYZ(center.X, bb.Min.Y - d, z);
                if (IsFreeAround(doc, view, inst, down, halfX, halfY)) return down;
            }

            // Fallback: arriba con la última distancia
            return new XYZ(center.X, bb.Max.Y + steps.Last(), z);
        }

        #endregion
    }
}
