using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using MiNamespace.Json;

namespace MiNamespace.UI
{
    public partial class UiCategorias : Window
    {
        public List<CategoriaItem> Categorias { get; set; }
        private List<CategoriaItem> TodasLasCategorias;
        private Document _doc;
        private string _disciplina;

        public UiCategorias(Document doc, string disciplina)
        {
            InitializeComponent();

            _doc = doc;
            _disciplina = disciplina;

            txtTitulo.Text = "Disciplina: " + disciplina;

            CargarCategoriasRevit();
            InicializarCategoriasPorDisciplina(disciplina);

            lstCategorias.ItemsSource = Categorias;
            cmbCategorias.ItemsSource = TodasLasCategorias;
        }

        private void CargarCategoriasRevit()
        {
            TodasLasCategorias = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c.CategoryType == CategoryType.Model)
                .Where(c => c.AllowsBoundParameters)
                .Where(c => c.Id.IntegerValue < 0)
                .Select(c => new CategoriaItem
                {
                    Nombre = c.Name,
                    BuiltInCategory = (BuiltInCategory)c.Id.IntegerValue
                })
                .OrderBy(c => c.Nombre)
                .ToList();
        }

        private void InicializarCategoriasPorDisciplina(string disciplina)
        {
            // 1. Intentar cargar desde persistencia
            List<BuiltInCategory> savedCats = CategorySettingsManager.LoadCategories(disciplina);
            
            if (savedCats != null)
            {
                Categorias = savedCats.Select(c => new CategoriaItem
                {
                    Nombre = TodasLasCategorias.FirstOrDefault(t => t.BuiltInCategory == c)?.Nombre ?? LabelUtils.GetLabelFor(c),
                    BuiltInCategory = c,
                    IsSelected = true
                }).ToList();
                return;
            }

            // 2. Si no hay guardados, usar valores por defecto
            List<BuiltInCategory> baseCats = new List<BuiltInCategory>();

            if (disciplina == "HVAC")
            {
                baseCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_DuctFitting,
                    BuiltInCategory.OST_DuctAccessory,
                    BuiltInCategory.OST_DuctTerminal,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_PipeFitting
                };
            }
            else if (disciplina == "HIDRAULICO")
            {
                baseCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_PipeAccessory,
                    BuiltInCategory.OST_PlumbingFixtures
                };
            }
            else if (disciplina == "RCI")
            {
                baseCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_PipeAccessory,
                    BuiltInCategory.OST_Sprinklers
                };
            }
            else if (disciplina == "ELECTRICO")
            {
                baseCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_LightingFixtures
                };
            }

            Categorias = baseCats.Select(c => new CategoriaItem
            {
                Nombre = TodasLasCategorias.FirstOrDefault(t => t.BuiltInCategory == c)?.Nombre ?? LabelUtils.GetLabelFor(c),
                BuiltInCategory = c,
                IsSelected = true
            }).ToList();
        }

        private void cmbCategorias_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            string texto = cmbCategorias.Text.ToLower();

            List<CategoriaItem> filtradas = TodasLasCategorias
                .Where(c => c.Nombre.ToLower().Contains(texto))
                .ToList();

            cmbCategorias.ItemsSource = filtradas;
            cmbCategorias.IsDropDownOpen = true;
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            CategoriaItem selected = cmbCategorias.SelectedItem as CategoriaItem;

            if (selected == null)
                return;

            bool existe = Categorias.Any(c => c.BuiltInCategory == selected.BuiltInCategory);

            if (!existe)
            {
                Categorias.Add(new CategoriaItem
                {
                    Nombre = selected.Nombre,
                    BuiltInCategory = selected.BuiltInCategory,
                    IsSelected = true
                });

                RefrescarLista();
            }

            cmbCategorias.Text = "";
            cmbCategorias.ItemsSource = TodasLasCategorias;
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            List<CategoriaItem> eliminar = Categorias
                .Where(c => c.IsSelected)
                .ToList();

            foreach (CategoriaItem item in eliminar)
                Categorias.Remove(item);

            RefrescarLista();
        }

        private void RefrescarLista()
        {
            lstCategorias.ItemsSource = null;
            lstCategorias.ItemsSource = Categorias;
        }

        public List<BuiltInCategory> GetSeleccionadas()
        {
            return Categorias
                .Where(c => c.IsSelected)
                .Select(c => c.BuiltInCategory)
                .ToList();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionadas = GetSeleccionadas();
            CategorySettingsManager.SaveCategories(_disciplina, seleccionadas);
            MessageBox.Show("Categorías guardadas correctamente para la disciplina: " + _disciplina, "Guardar", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e)
        {
            // Guardar categorías para la próxima vez
            var seleccionadas = GetSeleccionadas();
            CategorySettingsManager.SaveCategories(_disciplina, seleccionadas);

            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class CategoriaItem
    {
        public string Nombre { get; set; }
        public BuiltInCategory BuiltInCategory { get; set; }
        public bool IsSelected { get; set; }
    }
}
