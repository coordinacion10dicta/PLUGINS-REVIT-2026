//V3. se incluyo condicional para espesor de muros, comentarios.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;

// Definición de alias para evitar ambigüedad entre espacios de nombres de Revit y WinForms
using RevitView = Autodesk.Revit.DB.View;
using WinForms = System.Windows.Forms;

namespace MiNamespace
{
    /// <summary>
    /// Comando externo para la automatización del etiquetado de elementos arquitectónicos.
    /// Soporta filtrado por categorías, espesores de muro y selección dinámica de tipos de etiqueta.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_ARQ : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Inicialización de objetos de contexto de la interfaz de usuario y el documento
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                RevitView activeView = uidoc.ActiveView;

                // 1. Configuración de Categorías base: Diccionario que mapea el nombre comercial con la categoría de elemento y su respectiva etiqueta
                var opciones = new Dictionary<string, (BuiltInCategory elem, BuiltInCategory tag)>
                {
                    { "Accesorios", (BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_MultiCategoryTags) },
                    { "Iluminación", (BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingFixtureTags) },
                    { "Puertas", (BuiltInCategory.OST_Doors, BuiltInCategory.OST_DoorTags) },
                    { "Acabados de Muro", (BuiltInCategory.OST_Walls, BuiltInCategory.OST_WallTags) }
                };

                // 2. Selección de categoría mediante interfaz nativa de Revit (TaskDialog)
                TaskDialog mainDialog = new TaskDialog("DICTA - Etiquetado");
                mainDialog.MainInstruction = "¿Qué deseas taguear?";
                mainDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Accesorios");
                mainDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Iluminación");
                mainDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Puertas");
                mainDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Acabados de Muro");

                TaskDialogResult res = mainDialog.Show();
                if (res == TaskDialogResult.Cancel) return Result.Cancelled;

                // Mapeo del resultado del diálogo a la clave del diccionario de opciones
                string seleccion = (res == TaskDialogResult.CommandLink1) ? "Accesorios" :
                                   (res == TaskDialogResult.CommandLink2) ? "Iluminación" :
                                   (res == TaskDialogResult.CommandLink3) ? "Puertas" : "Acabados de Muro";

                var config = opciones[seleccion];

                // --- LÓGICA DE FILTRADO POR ESPESOR (Específico para Acabados de Muro) ---
                double espesorMaximoPies = double.MaxValue;
                if (seleccion == "Acabados de Muro")
                {
                    ThicknessInputWindow inputWin = new ThicknessInputWindow();
                    if (inputWin.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

                    // Conversión de unidades: El usuario ingresa CM, Revit API procesa en Pies (Internal Units)
                    espesorMaximoPies = inputWin.EspesorCm / 30.48;
                }
                // -------------------------------------------------------------------------

                // 3. Obtención de familias de etiquetas disponibles en el proyecto
                var availableTags = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .Where(t => t.Category != null && (t.Category.Id.IntegerValue == (int)config.tag ||
                                                       t.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags))
                    .ToList();

                if (!availableTags.Any())
                {
                    message = "No se encontraron etiquetas cargadas para la categoría seleccionada.";
                    return Result.Failed;
                }

                // Despliegue de ventana personalizada para selección de tipo de etiqueta específico
                List<string> nombres = availableTags.Select(t => t.FamilyName + ": " + t.Name).OrderBy(x => x).ToList();
                TagSelectionWindow win = new TagSelectionWindow(nombres);
                if (win.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;

                // Identificación del FamilySymbol seleccionado y verificación si es Multi-Categoría
                FamilySymbol tagType = availableTags.First(t => (t.FamilyName + ": " + t.Name) == win.SelectedTagType);
                bool esMulti = tagType.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MultiCategoryTags;

                // 4. Lógica de recolección de elementos visibles en la vista activa
                List<Element> elementosVisibles;
                if (seleccion == "Accesorios" && esMulti)
                {
                    // Lista de categorías permitidas para etiquetas multi-categoría en este contexto
                    var categoriasValidas = new List<BuiltInCategory> {
                        BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Furniture,
                        BuiltInCategory.OST_Casework, BuiltInCategory.OST_SpecialityEquipment,
                        BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_ElectricalFixtures,
                        BuiltInCategory.OST_MechanicalEquipment
                    };

                    elementosVisibles = new FilteredElementCollector(doc, activeView.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && categoriasValidas.Contains((BuiltInCategory)e.Category.Id.IntegerValue))
                        .ToList();
                }
                else if (seleccion == "Acabados de Muro")
                {
                    // Filtrado de muros por el parámetro de espesor (Width) definido previamente
                    elementosVisibles = new FilteredElementCollector(doc, activeView.Id)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .Cast<Wall>()
                        .Where(w => w.WallType.Width < (espesorMaximoPies + 0.001)) // Margen de tolerancia para valores double
                        .Cast<Element>()
                        .ToList();
                }
                else
                {
                    // Recolección estándar por categoría base
                    elementosVisibles = new FilteredElementCollector(doc, activeView.Id)
                        .OfCategory(config.elem)
                        .WhereElementIsNotElementType()
                        .ToList();
                }

                // Inicio de la transacción para modificar el modelo de Revit
                using (Transaction tx = new Transaction(doc, "Etiquetado Profesional"))
                {
                    tx.Start();

                    // Asegurar que el símbolo de la etiqueta esté activo antes de su uso
                    if (!tagType.IsActive) { tagType.Activate(); doc.Regenerate(); }

                    int contador = 0;

                    // Agrupamiento lógico para evitar duplicidad de etiquetas en elementos contiguos o idénticos (Especialmente para muros)
                    var gruposParaTaguear = elementosVisibles.GroupBy(el =>
                    {
                        if (seleccion == "Acabados de Muro" && el is Wall wall)
                        {
                            LocationCurve lc = wall.Location as LocationCurve;
                            XYZ mid = lc.Curve.Evaluate(0.5, true);
                            // Agrupación por zona (cuadrícula de 15 unidades) para manejar muros segmentados
                            string zona = $"{(int)(mid.X / 15)}_{(int)(mid.Y / 15)}";
                            return wall.WallType.Id.ToString() + "_" + wall.LevelId.ToString() + "_" + zona;
                        }
                        return el.Id.ToString();
                    });

                    foreach (var grupo in gruposParaTaguear)
                    {
                        Element el = grupo.First();

                        // Omitir muros que ya han sido divididos en piezas (Parts)
                        if (el is Wall && PartUtils.HasAssociatedParts(doc, el.Id)) continue;

                        XYZ puntoBaseRaw = null;
                        XYZ vecOffset = new XYZ(1.0, 1.0, 0);

                        // Cálculo del punto de inserción y vector de orientación según el tipo de geometría (Punto o Curva)
                        if (el.Location is LocationPoint lp)
                        {
                            puntoBaseRaw = lp.Point;
                            if (el is FamilyInstance fi) vecOffset = fi.FacingOrientation.Normalize();
                        }
                        else if (el.Location is LocationCurve lc)
                        {
                            puntoBaseRaw = lc.Curve.Evaluate(0.5, true);
                            XYZ dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
                            // Cálculo del vector perpendicular a la curva para el offset de la etiqueta
                            vecOffset = new XYZ(-dir.Y, dir.X, 0);
                        }

                        if (puntoBaseRaw != null)
                        {
                            // Ajuste de elevación Z según el nivel de la vista o la posición del elemento
                            double zVista = activeView.GenLevel?.ProjectElevation ?? puntoBaseRaw.Z;
                            XYZ puntoInsercion = new XYZ(puntoBaseRaw.X, puntoBaseRaw.Y, zVista);

                            try
                            {
                                // Creación de la etiqueta independiente
                                IndependentTag newTag = IndependentTag.Create(doc, tagType.Id, activeView.Id, new Reference(el), false, TagOrientation.Horizontal, puntoInsercion);

                                // Ajuste fino de la posición de la cabeza de la etiqueta y configuración de la directriz (Leader)
                                double distancia = (seleccion == "Puertas") ? 1.0 : 1.2;
                                newTag.TagHeadPosition = puntoInsercion + (vecOffset * distancia);
                                newTag.HasLeader = (seleccion != "Puertas");

                                contador++;
                            }
                            catch { /* Manejo de errores silencioso para elementos no etiquetables en el bucle */ }
                        }
                    }

                    tx.Commit();
                    TaskDialog.Show("Éxito", $"Se colocaron {contador} etiquetas.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Ventana de diálogo para la entrada de datos numéricos (Espesor en cm).
    /// </summary>
    public class ThicknessInputWindow : WinForms.Form
    {
        private WinForms.TextBox txtEspesor;
        public double EspesorCm { get; private set; }

        public ThicknessInputWindow()
        {
            this.Text = "Configurar Acabados";
            this.Size = new System.Drawing.Size(300, 150);
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;

            WinForms.Label lbl = new WinForms.Label() { Text = "Espesor máximo de acabado (cm):", Location = new System.Drawing.Point(20, 20), AutoSize = true };
            txtEspesor = new WinForms.TextBox() { Location = new System.Drawing.Point(20, 45), Width = 240 };
            txtEspesor.Text = "10";

            WinForms.Button btnOK = new WinForms.Button() { Text = "Aceptar", Location = new System.Drawing.Point(185, 80), DialogResult = WinForms.DialogResult.OK };
            btnOK.Click += (s, e) =>
            {
                if (double.TryParse(txtEspesor.Text, out double val)) EspesorCm = val;
                else EspesorCm = 0;
            };

            this.Controls.Add(lbl);
            this.Controls.Add(txtEspesor);
            this.Controls.Add(btnOK);
            this.AcceptButton = btnOK;
        }
    }

    /// <summary>
    /// Ventana de interfaz para la selección y filtrado de tipos de etiquetas mediante búsqueda dinámica.
    /// </summary>
    public class TagSelectionWindow : WinForms.Form
    {
        private WinForms.TextBox txtBuscar;
        private WinForms.ListBox listBoxTags;
        public string SelectedTagType { get; private set; }

        public TagSelectionWindow(List<string> tags)
        {
            this.Text = "Seleccionar Etiqueta - DICTA";
            this.Width = 450; this.Height = 470;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;

            // Implementación de buscador reactivo
            txtBuscar = new WinForms.TextBox { Location = new System.Drawing.Point(15, 15), Width = 405 };
            txtBuscar.TextChanged += (s, e) =>
            {
                listBoxTags.Items.Clear();
                listBoxTags.Items.AddRange(tags.Where(t => t.ToLower().Contains(txtBuscar.Text.ToLower())).ToArray());
            };

            listBoxTags = new WinForms.ListBox { Location = new System.Drawing.Point(15, 45), Size = new System.Drawing.Size(405, 320) };
            listBoxTags.Items.AddRange(tags.ToArray());

            WinForms.Button btnOK = new WinForms.Button { Text = "Aceptar", Location = new System.Drawing.Point(320, 380), Width = 100, Height = 30, DialogResult = WinForms.DialogResult.OK };
            btnOK.Click += (s, e) => { if (listBoxTags.SelectedItem != null) SelectedTagType = listBoxTags.SelectedItem.ToString(); };

            this.Controls.Add(txtBuscar); this.Controls.Add(listBoxTags); this.Controls.Add(btnOK);
            this.AcceptButton = btnOK;
        }
    }
}
