using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using MiNamespace.ValidadorParametros;

namespace MiNamespace.UI
{
    public partial class UiValidadorParametros : Window
    {
        private readonly Document _doc;
        private List<DisciplineParameter> _parametros = new List<DisciplineParameter>();
        private List<ValidationIssue> _resultados = new List<ValidationIssue>();
        private string _excelPath;
        private ValidationIssue _selectedIssue = null;
        private Element _selectedElement = null;

        public UiValidadorParametros(Document doc, string defaultExcelPath)
        {
            InitializeComponent();
            _doc = doc;
            _excelPath = defaultExcelPath;
            txtExcelPath.Text = _excelPath;

            if (File.Exists(_excelPath))
                CargarExcel();
        }

        private void CargarExcel()
        {
            try
            {
                _parametros = ExcelParameterLoader.Load(_excelPath);
                var disciplinas = _parametros
                    .Select(p => p.Disciplina)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(d => d)
                    .ToList();

                cboDisciplina.ItemsSource = disciplinas;
                if (disciplinas.Count > 0)
                    cboDisciplina.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar Excel:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExaminar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls",
                Title = "Seleccionar archivo de parámetros"
            };

            if (File.Exists(_excelPath))
                dlg.InitialDirectory = Path.GetDirectoryName(_excelPath);

            if (dlg.ShowDialog() == true)
            {
                _excelPath = dlg.FileName;
                txtExcelPath.Text = _excelPath;
                CargarExcel();
            }
        }

        private void BtnCrearPlantilla_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                Title = "Guardar plantilla",
                FileName = "ParametrosRequeridos.xlsx",
                InitialDirectory = File.Exists(_excelPath)
                    ? Path.GetDirectoryName(_excelPath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelParameterLoader.CrearPlantilla(dlg.FileName);
                _excelPath = dlg.FileName;
                txtExcelPath.Text = _excelPath;
                CargarExcel();
                MessageBox.Show($"Plantilla creada en:\n{dlg.FileName}",
                    "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear plantilla:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_excelPath))
            {
                MessageBox.Show("El archivo Excel no existe. Crea uno primero.",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_excelPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir Excel:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnValidar_Click(object sender, RoutedEventArgs e)
        {
            if (cboDisciplina.SelectedItem == null)
            {
                MessageBox.Show("Seleccione una disciplina.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_parametros.Any())
            {
                MessageBox.Show("Cargue el archivo Excel de parámetros primero.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsEnabled = false;
                string disciplina = cboDisciplina.SelectedItem.ToString();
                _resultados = ParameterValidator.Validate(_doc, disciplina, _parametros);

                dgResultados.ItemsSource = _resultados;

                int faltantes  = _resultados.Count(i => i.TipoDeProblema == TipoProblema.ParametroFaltante);
                int vacios     = _resultados.Count(i => i.TipoDeProblema == TipoProblema.ValorVacio);
                int incorrectos = _resultados.Count(i => i.TipoDeProblema == TipoProblema.NombreIncorrecto);
                int duplicados = _resultados.Count(i => i.TipoDeProblema == TipoProblema.Duplicado);

                lblResumen.Text = _resultados.Any()
                    ? $"Total: {_resultados.Count} | Faltantes: {faltantes} | Vacíos: {vacios} | Nombre incorrecto: {incorrectos} | Duplicados: {duplicados}"
                    : "Validación sin problemas.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en validación:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void DgResultados_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (dgResultados.SelectedItem is ValidationIssue issue)
            {
                _selectedIssue = issue;

                try
                {
#if REVIT_LEGACY_ELEMENTID
                    int elemId = int.Parse(issue.ElementId);
                    _selectedElement = _doc.GetElement(new ElementId(elemId));
#else
                    long elemId = long.Parse(issue.ElementId);
                    _selectedElement = _doc.GetElement(new ElementId(elemId));
#endif
                    if (_selectedElement != null)
                    {
                        MostrarPanelEdicion(_selectedElement);
                    }
                    else
                    {
                        LimpiarPanelEdicion("Elemento no encontrado en el modelo.");
                    }
                }
                catch
                {
                    LimpiarPanelEdicion("Error al cargar el elemento.");
                }
            }
            else
            {
                LimpiarPanelEdicion("Selecciona un elemento para editar.");
            }
        }

        private void MostrarPanelEdicion(Element element)
        {
            if (element == null)
            {
                LimpiarPanelEdicion("Elemento no válido.");
                return;
            }

            try
            {
                var parametros = ParameterEditor.GetEditableParameters(element);
                lstParametros.ItemsSource = new ObservableCollection<ParameterEditModel>(parametros);

                string info = $"ID: {element.Id}\n";
                if (element is FamilyInstance fi)
                    info += $"Familia: {fi.Symbol.Family.Name}\n";
                info += $"Categoría: {element.Category?.Name}\n";
                info += $"Parámetros: {parametros.Count}";

                lblElementoInfo.Text = info;
                lblStatusPanel.Text = "Edita los valores y guarda los cambios.";

                bool tieneParamsInstancia = parametros.Any(p => p.IsInstance);
                btnAplicarSimilares.IsEnabled = tieneParamsInstancia && element is FamilyInstance;
            }
            catch (Exception ex)
            {
                LimpiarPanelEdicion($"Error al mostrar parámetros:\n{ex.Message}");
            }
        }

        private void LimpiarPanelEdicion(string mensaje)
        {
            lstParametros.ItemsSource = null;
            lblElementoInfo.Text = "";
            lblStatusPanel.Text = mensaje;
            btnAplicarSimilares.IsEnabled = false;
            _selectedElement = null;
        }

        private void BtnGuardarCambios_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null)
            {
                MessageBox.Show("Selecciona un elemento primero.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var cambios = new Dictionary<string, string>();

                if (lstParametros.ItemsSource is ObservableCollection<ParameterEditModel> items)
                {
                    foreach (var item in items)
                    {
                        if (item.Value != GetOriginalValue(item.OriginalParameter))
                            cambios[item.Name] = item.Value;
                    }
                }

                if (cambios.Count == 0)
                {
                    MessageBox.Show("No hay cambios para guardar.", "Información",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (ParameterEditor.SaveParameterChanges(_doc, _selectedElement, cambios))
                {
                    MessageBox.Show($"Se guardaron {cambios.Count} cambio(s).", "Éxito",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    lblStatusPanel.Text = $"Guardados {cambios.Count} cambios.";
                }
                else
                {
                    MessageBox.Show("Error al guardar cambios.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAplicarSimilares_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null || lstParametros.ItemsSource == null)
            {
                MessageBox.Show("Selecciona un elemento primero.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var items = lstParametros.ItemsSource as ObservableCollection<ParameterEditModel>;
            var paramsInstancia = items?.Where(p => p.IsInstance).ToList();

            if (!paramsInstancia?.Any() == true)
            {
                MessageBox.Show("No hay parámetros de instancia para aplicar.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SelectParameterDialog(paramsInstancia);
            if (dlg.ShowDialog() != true) return;

            string paramName = dlg.SelectedParameter;
            var param = paramsInstancia.FirstOrDefault(p => p.Name == paramName);
            if (param == null) return;

            try
            {
                int applied = ParameterEditor.ApplyToSimilarElements(_doc, _selectedElement, paramName, param.Value);
                MessageBox.Show($"Se aplicó el valor a {applied} elemento(s) similar(es).", "Éxito",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAgregarParametro_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "La creación de parámetros requiere acceso a la familia.\n\n" +
                "Para agregar parámetros de forma permanente, edita la familia en Revit.",
                "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAutoFix_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "EJECUTAR AUTO FIX\n\n" +
                "Función en desarrollo para correcciones automáticas:\n\n" +
                "  •  Agregar parámetros faltantes\n" +
                "  •  Corregir casing de nombres\n" +
                "  •  Aplicar valores por defecto\n" +
                "  •  Sincronizar entre elementos",
                "En desarrollo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetOriginalValue(Parameter param)
        {
            if (param == null) return "";
            switch (param.StorageType)
            {
                case StorageType.String:    return param.AsString() ?? "";
                case StorageType.Double:    return param.AsValueString() ?? "";
                case StorageType.Integer:   return param.AsInteger().ToString();
                case StorageType.ElementId:
#if REVIT_LEGACY_ELEMENTID
                    return param.AsElementId()?.IntegerValue.ToString() ?? "";
#else
                    return param.AsElementId()?.Value.ToString() ?? "";
#endif
                default: return "";
            }
        }
    }

    public partial class SelectParameterDialog : Window
    {
        public string SelectedParameter { get; private set; }

        public SelectParameterDialog(List<ParameterEditModel> parameters)
        {
            InitializeComponent();
            Title = "Seleccionar parámetro";
            Width = 300;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            grid.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Selecciona el parámetro a aplicar:",
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var cbo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = parameters.Select(p => p.Name).ToList(),
                Margin = new Thickness(0, 0, 0, 15),
                Padding = new Thickness(6, 4)
            };
            cbo.SelectedIndex = 0;
            grid.Children.Add(cbo);

            var sp = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Gap = 8
            };

            var btnOk = new System.Windows.Controls.Button
            {
                Content = "OK",
                Padding = new Thickness(20, 8),
                Width = 80
            };
            btnOk.Click += (s, e) => { SelectedParameter = (string)cbo.SelectedItem; DialogResult = true; };

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Cancelar",
                Padding = new Thickness(20, 8),
                Width = 80
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            sp.Children.Add(btnOk);
            sp.Children.Add(btnCancel);
            grid.Children.Add(sp);

            Content = grid;
        }
    }
}
