using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using MiNamespace.ValidadorParametros;

namespace MiNamespace.UI
{
    // ─── Converters ────────────────────────────────────────────────────────────

    public class SeveridadToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Severidad sev)
                return sev == Severidad.Critico
                    ? new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C))
                    : new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17));
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class SeveridadToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Severidad sev)
                return sev == Severidad.Critico ? "● CRÍTICO" : "● AVISO";
            return "";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ComplianceToBarColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double pct)
            {
                if (pct >= 90) return new SolidColorBrush(Color.FromRgb(0x2D, 0x6A, 0x4F));
                if (pct >= 70) return new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17));
                return new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ─── Main Window ───────────────────────────────────────────────────────────

    public partial class UiValidadorParametros : Window
    {
        private readonly Document _doc;
        private List<DisciplineParameter> _parametros = new List<DisciplineParameter>();
        private ValidationSummary _summary = null;
        private string _excelPath;
        private ICollectionView _collectionView;
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

        // ─── Excel ──────────────────────────────────────────────────────────────

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
                if (disciplinas.Count > 0) cboDisciplina.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar Excel:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExaminar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls" };
            if (File.Exists(_excelPath)) dlg.InitialDirectory = Path.GetDirectoryName(_excelPath);
            if (dlg.ShowDialog() == true) { _excelPath = dlg.FileName; txtExcelPath.Text = _excelPath; CargarExcel(); }
        }

        private void BtnCrearPlantilla_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
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
                MessageBox.Show($"Plantilla creada:\n{dlg.FileName}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnEditarExcel_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_excelPath))
            { MessageBox.Show("El archivo no existe.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try { Process.Start(new ProcessStartInfo(_excelPath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ─── Validation ─────────────────────────────────────────────────────────

        private void BtnValidar_Click(object sender, RoutedEventArgs e)
        {
            if (cboDisciplina.SelectedItem == null)
            { MessageBox.Show("Seleccione una disciplina.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!_parametros.Any())
            { MessageBox.Show("Cargue el archivo Excel primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            try
            {
                IsEnabled = false;
                string disciplina = cboDisciplina.SelectedItem.ToString();
                _summary = ParameterValidator.Validate(_doc, disciplina, _parametros);

                ActualizarKpiCards(disciplina);
                ActualizarSidebar(disciplina);
                BindDataGrid();
                ActualizarAutoFixFooter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en validación:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsEnabled = true; }
        }

        private void ActualizarKpiCards(string disciplina)
        {
            var issues = _summary.Issues;
            int faltantes  = issues.Count(i => i.TipoDeProblema == TipoProblema.ParametroFaltante);
            int duplicados = issues.Count(i => i.TipoDeProblema == TipoProblema.Duplicado
                                            || i.TipoDeProblema == TipoProblema.AlcanceIncorrecto);

            int totalElementos = _summary.TotalElementos;
            int elementosConError = issues.Select(i => i.ElementId).Distinct().Count();
            double compliance = totalElementos > 0
                ? Math.Round((totalElementos - elementosConError) * 100.0 / totalElementos, 1)
                : 100.0;

            kpiFamilias.Text     = _summary.TotalFamilias.ToString();
            kpiFaltantes.Text    = faltantes.ToString();
            kpiDuplicados.Text   = duplicados.ToString();
            kpiCompliance.Text   = $"{compliance:0.0}%";
            pgCompliance.Value   = compliance;

            var brush = compliance >= 90
                ? new SolidColorBrush(Color.FromRgb(0x2D, 0x6A, 0x4F))
                : compliance >= 70
                    ? new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17))
                    : new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));

            kpiCompliance.Foreground = brush;
            pgCompliance.Foreground  = brush;
        }

        private void ActualizarSidebar(string disciplina)
        {
            var issues = _summary.Issues;

            // Build compliance data per discipline (current = single discipline)
            var catGroups = issues
                .GroupBy(i => i.Categoria, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .Select(g => new CategoryComplianceModel
                {
                    Nombre       = g.Key,
                    TotalErrores = g.Count(),
                    Criticos     = g.Count(i => i.Severidad == Severidad.Critico)
                }).ToList();

            int totalElementos    = _summary.TotalElementos;
            int elementosConError = issues.Select(i => i.ElementId).Distinct().Count();

            var disciplineModel = new DisciplineComplianceModel
            {
                Nombre              = disciplina,
                TotalElementos      = totalElementos,
                ElementosConErrores = elementosConError,
                TotalErrores        = issues.Count,
                ErroresCriticos     = issues.Count(i => i.Severidad == Severidad.Critico),
                Categorias          = catGroups
            };

            lstDisciplinas.ItemsSource = new List<DisciplineComplianceModel> { disciplineModel };
        }

        private void BindDataGrid()
        {
            var view = CollectionViewSource.GetDefaultView(_summary.Issues);
            view.Filter = AplicarFiltro;
            _collectionView = view;
            dgResultados.ItemsSource = view;
            ActualizarContadorFiltro();
        }

        // ─── Filters ─────────────────────────────────────────────────────────────

        private void FiltroChanged(object sender, EventArgs e)
        {
            _collectionView?.Refresh();
            ActualizarContadorFiltro();
        }

        private bool AplicarFiltro(object item)
        {
            if (!(item is ValidationIssue issue)) return false;

            if (chkSoloCriticos.IsChecked  == true && issue.Severidad != Severidad.Critico) return false;
            if (chkSoloFaltantes.IsChecked == true && issue.TipoDeProblema != TipoProblema.ParametroFaltante) return false;
            if (chkSoloTipo.IsChecked      == true && !string.Equals(issue.Alcance, "Tipo",      StringComparison.OrdinalIgnoreCase)) return false;
            if (chkSoloInstancia.IsChecked == true && !string.Equals(issue.Alcance, "Instancia", StringComparison.OrdinalIgnoreCase)) return false;

            string texto = txtFiltroTexto.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(texto))
            {
                bool match = issue.Parametro?.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0
                          || issue.Familia?.IndexOf(texto,   StringComparison.OrdinalIgnoreCase) >= 0
                          || issue.Categoria?.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) return false;
            }

            return true;
        }

        private void ActualizarContadorFiltro()
        {
            if (_summary == null) return;
            int visible = _collectionView?.Cast<object>().Count() ?? 0;
            lblFiltroInfo.Text = $"Mostrando {visible} de {_summary.Issues.Count} problemas";
        }

        // ─── DataGrid selection / quick actions ──────────────────────────────────

        private void DgResultados_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgResultados.SelectedItem is ValidationIssue issue)
                CargarElementoEnPanel(issue.ElementId);
        }

        private void BtnCorregirFila_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ValidationIssue issue)
            {
                dgResultados.SelectedItem = issue;
                CargarElementoEnPanel(issue.ElementId);
            }
        }

        private void CargarElementoEnPanel(string elementIdStr)
        {
            try
            {
#if REVIT_LEGACY_ELEMENTID
                int rawId = int.Parse(elementIdStr);
                _selectedElement = _doc.GetElement(new ElementId(rawId));
#else
                long rawId = long.Parse(elementIdStr);
                _selectedElement = _doc.GetElement(new ElementId(rawId));
#endif
                if (_selectedElement != null)
                    MostrarPanelEdicion(_selectedElement);
                else
                    LimpiarPanelEdicion("Elemento no encontrado.");
            }
            catch { LimpiarPanelEdicion("Error al cargar elemento."); }
        }

        private void MostrarPanelEdicion(Element element)
        {
            var parametros = ParameterEditor.GetEditableParameters(element);
            lstParametros.ItemsSource = new ObservableCollection<ParameterEditModel>(parametros);

            string info = $"ID: {element.Id}";
            if (element is FamilyInstance fi) info += $"  |  {fi.Symbol?.Family?.Name}";
            info += $"\n{element.Category?.Name}  ·  {parametros.Count} parámetros";
            lblElementoInfo.Text = info;

            lblStatusPanel.Text = "Edita los valores y haz clic en Guardar.";
            btnAplicarSimilares.IsEnabled = parametros.Any(p => p.IsInstance) && element is FamilyInstance;
        }

        private void LimpiarPanelEdicion(string mensaje)
        {
            lstParametros.ItemsSource = null;
            lblElementoInfo.Text = mensaje;
            lblStatusPanel.Text = "";
            btnAplicarSimilares.IsEnabled = false;
            _selectedElement = null;
        }

        // ─── Edit panel actions ──────────────────────────────────────────────────

        private void BtnGuardarCambios_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null) { MessageBox.Show("Selecciona un elemento.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var cambios = new Dictionary<string, string>();
            if (lstParametros.ItemsSource is ObservableCollection<ParameterEditModel> items)
                foreach (var item in items)
                    if (item.Value != GetOriginalValue(item.OriginalParameter))
                        cambios[item.Name] = item.Value;

            if (!cambios.Any()) { MessageBox.Show("Sin cambios.", "Información", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            if (ParameterEditor.SaveParameterChanges(_doc, _selectedElement, cambios))
            {
                lblStatusPanel.Text = $"✓ Guardados {cambios.Count} cambio(s).";
                MessageBox.Show($"Se guardaron {cambios.Count} cambio(s).", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else MessageBox.Show("Error al guardar.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void BtnAplicarSimilares_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null || lstParametros.ItemsSource == null) return;

            var items = lstParametros.ItemsSource as ObservableCollection<ParameterEditModel>;
            var paramsInst = items?.Where(p => p.IsInstance).ToList() ?? new List<ParameterEditModel>();
            if (!paramsInst.Any()) { MessageBox.Show("No hay parámetros de instancia.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var dlg = new SelectParameterWindow(paramsInst);
            if (dlg.ShowDialog() != true) return;

            var selected = paramsInst.FirstOrDefault(p => p.Name == dlg.SelectedParameter);
            if (selected == null) return;

            int count = ParameterEditor.ApplyToSimilarElements(_doc, _selectedElement, selected.Name, selected.Value);
            MessageBox.Show($"Valor aplicado a {count} elemento(s) similar(es).", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAgregarParametro_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Para agregar parámetros de forma permanente, edita la familia en Revit.\n\n" +
                "Esta función (Auto Fix) estará disponible en una versión futura.",
                "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ─── Auto Fix ────────────────────────────────────────────────────────────

        private void ActualizarAutoFixFooter()
        {
            if (_summary == null) return;
            var issues = _summary.Issues;

            int criticos    = issues.Count(i => i.Severidad == Severidad.Critico);
            int advertencias = issues.Count(i => i.Severidad == Severidad.Advertencia);
            int autoFixable  = issues.Count(i => i.TipoDeProblema == TipoProblema.ValorVacio
                                              || i.TipoDeProblema == TipoProblema.NombreIncorrecto);

            lblAutoFixInfo.Text   = $"{autoFixable} error(es) pueden corregirse automáticamente según las reglas del Excel.";
            lblAutoFixDetalle.Text = $"Críticos: {criticos}  ·  Advertencias: {advertencias}  ·  Total: {issues.Count}";
        }

        private void BtnAutoFix_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "EJECUTAR AUTO FIX\n\nFunción en desarrollo. Incluirá:\n\n" +
                "  •  Agregar parámetros faltantes según Excel\n" +
                "  •  Corregir nombres (casing / typos)\n" +
                "  •  Aplicar valores por defecto de plantilla\n" +
                "  •  Corregir asignación de Tipo vs Instancia\n" +
                "  •  Sincronizar parámetros compartidos",
                "En desarrollo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetOriginalValue(Parameter param)
        {
            if (param == null) return "";
            switch (param.StorageType)
            {
                case StorageType.String:  return param.AsString() ?? "";
                case StorageType.Double:  return param.AsValueString() ?? "";
                case StorageType.Integer: return param.AsInteger().ToString();
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

    // ─── Select Parameter Window ─────────────────────────────────────────────

    public class SelectParameterWindow : Window
    {
        public string SelectedParameter { get; private set; }

        public SelectParameterWindow(List<ParameterEditModel> parameters)
        {
            Title  = "Seleccionar parámetro";
            Width  = 320;
            Height = 210;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var sp = new StackPanel { Margin = new Thickness(16) };

            sp.Children.Add(new TextBlock
            {
                Text       = "Parámetro a aplicar a elementos similares:",
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 0, 0, 10)
            });

            var cbo = new ComboBox
            {
                ItemsSource    = parameters.Select(p => p.Name).ToList(),
                SelectedIndex  = 0,
                Padding        = new Thickness(6, 4),
                Margin         = new Thickness(0, 0, 0, 16)
            };
            sp.Children.Add(cbo);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnOk = new Button { Content = "Aplicar", Padding = new Thickness(20, 7), Margin = new Thickness(0, 0, 8, 0) };
            btnOk.Click += (s, e) => { SelectedParameter = (string)cbo.SelectedItem; DialogResult = true; };

            var btnCancelar = new Button { Content = "Cancelar", Padding = new Thickness(20, 7) };
            btnCancelar.Click += (s, e) => DialogResult = false;

            row.Children.Add(btnOk);
            row.Children.Add(btnCancelar);
            sp.Children.Add(row);

            Content = sp;
        }
    }
}
