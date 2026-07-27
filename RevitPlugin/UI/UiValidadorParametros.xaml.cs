using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using WpfColor = System.Windows.Media.Color;
using MiNamespace.ValidadorParametros;
#if REVIT2022_OR_LATER
using BuiltInCategory = Autodesk.Revit.DB.BuiltInCategory;
#endif

namespace MiNamespace.UI
{
    // ─── Converters ────────────────────────────────────────────────────────────

    public class SeveridadToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Severidad sev)
                return sev == Severidad.Critico
                    ? new SolidColorBrush(WpfColor.FromRgb(0xB7, 0x1C, 0x1C))
                    : new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x7F, 0x17));
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

    public class TipoBloqueToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "keyword":  return new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32)); // verde
                default:         return new SolidColorBrush(WpfColor.FromRgb(0x54, 0x6E, 0x7A)); // gris (texto)
            }
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
                if (pct >= 90) return new SolidColorBrush(WpfColor.FromRgb(0x2D, 0x6A, 0x4F));
                if (pct >= 70) return new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x7F, 0x17));
                return new SolidColorBrush(WpfColor.FromRgb(0xB7, 0x1C, 0x1C));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ─── Modelo para lista de categorías con agrupación ─────────────────────────

    public class CategoriaListItem
    {
        public string Nombre   { get; set; }
        public bool   Agregada { get; set; }
        public string Grupo    => Agregada ? "✓ Agregadas a la disciplina" : "Categorías del modelo";
    }

    // ─── Main Window ───────────────────────────────────────────────────────────

    public partial class UiValidadorParametros : Window
    {
        private readonly ValidadorParametros.ValidacionEventHandler    _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent               _extEvent;
        private readonly ValidadorParametros.AgregarParametroEventHandler _agregarHandler;
        private readonly Autodesk.Revit.UI.ExternalEvent               _agregarEvent;
        private Document _doc;

        private ValidationIssue _selectedIssue = null;

        private List<DisciplineParameter> _parametros = new List<DisciplineParameter>();
        private ValidationSummary _summary = null;
        private ICollectionView _collectionView;
        private Element _selectedElement = null;

        // Estado CRUD
        private DisciplinaConfig _crudDisciplinaActual;
        private CategoriaConfig  _crudCategoriaActual;
        private ObservableCollection<ParametroConfig>   _crudParametros;
        private ObservableCollection<PlantillaBloque>   _plantilla;
        private ObservableCollection<KeywordViewModel>  _keywords;
        private List<string> _categoriasCatalogo;

        public UiValidadorParametros(
            Document doc,
            ValidadorParametros.ValidacionEventHandler handler,
            Autodesk.Revit.UI.ExternalEvent extEvent,
            ValidadorParametros.AgregarParametroEventHandler agregarHandler,
            Autodesk.Revit.UI.ExternalEvent agregarEvent)
        {
            try
            {
                InitializeComponent();
                _doc           = doc;
                _handler       = handler;
                _extEvent      = extEvent;
                _agregarHandler = agregarHandler;
                _agregarEvent   = agregarEvent;

                _agregarHandler.OnCompleted = msg =>
                {
                    bool exito = msg.StartsWith("✓");
                    MessageBox.Show(msg,
                        exito ? "Parámetro agregado al proyecto" : "Atención",
                        MessageBoxButton.OK,
                        exito ? MessageBoxImage.Information : MessageBoxImage.Warning);
                };
                _agregarHandler.OnError = msg =>
                    MessageBox.Show(msg, "Error al agregar parámetro", MessageBoxButton.OK, MessageBoxImage.Error);

                _handler.OnStarted   = () => SetValidandoState(true);
                _handler.OnCompleted = (updatedDoc, summary) => { _doc = updatedDoc; MostrarResultados(summary); };
                _handler.OnError     = msg =>
                {
                    SetValidandoState(false);
                    MessageBox.Show(msg, "Error en validación", MessageBoxButton.OK, MessageBoxImage.Error);
                };

                RefrescarComboValidacion();
                CargarCrudDisciplinas();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar Validador:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Error crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetValidandoState(bool validando)
        {
            BtnValidar.IsEnabled      = !validando;
            lblValidandoStatus.Text   = validando ? "⏳ Validando..." : "";
        }

        // ─── Validación: ComboBox disciplinas ────────────────────────────────────

        private void RefrescarComboValidacion()
        {
            var disciplinas = DisciplinasRepository.GetNombresDisciplinas();
            cboDisciplina.ItemsSource = disciplinas;
            if (disciplinas.Count > 0) cboDisciplina.SelectedIndex = 0;
            else RefrescarCategoriasValidacion(null);
        }

        private void CboDisciplina_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefrescarCategoriasValidacion(cboDisciplina.SelectedItem?.ToString());
        }

        private void RefrescarCategoriasValidacion(string disciplina)
        {
            var items = new List<string> { "(Todas las categorías)" };
            if (!string.IsNullOrEmpty(disciplina))
            {
                var disc = DisciplinasRepository.GetDisciplinas()
                    .FirstOrDefault(d => d.Nombre.Equals(disciplina, StringComparison.OrdinalIgnoreCase));
                if (disc != null)
                    items.AddRange(disc.Categorias.Select(c => c.Nombre).OrderBy(n => n));
            }
            cboCategoriaFiltro.ItemsSource = items;
            cboCategoriaFiltro.SelectedIndex = 0;
        }

        // ─── Tab switching ───────────────────────────────────────────────────────

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tc && tc.SelectedIndex == 0)
                RefrescarComboValidacion();
        }

        // ─── Validation ─────────────────────────────────────────────────────────

        private void BtnValidar_Click(object sender, RoutedEventArgs e)
        {
            if (cboDisciplina.SelectedItem == null)
            { MessageBox.Show("Seleccione una disciplina.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            _parametros = DisciplinasRepository.ToDisciplineParameters();
            if (!_parametros.Any())
            { MessageBox.Show("No hay parámetros configurados. Ve a la pestaña Configuración.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string disciplina = cboDisciplina.SelectedItem.ToString();
            string catFiltro  = cboCategoriaFiltro.SelectedItem?.ToString();
            bool   todas      = string.IsNullOrEmpty(catFiltro) || catFiltro == "(Todas las categorías)";

            var reglasFiltradas = todas
                ? _parametros
                : _parametros.Where(p => p.Categoria.Equals(catFiltro, StringComparison.OrdinalIgnoreCase)).ToList();

            // Llenar request y delegar a Revit — NUNCA llamar API de Revit desde aquí
            _handler.Disciplina = disciplina;
            _handler.Reglas     = reglasFiltradas;

            SetValidandoState(true);
            var req = _extEvent.Raise();
            if (req != Autodesk.Revit.UI.ExternalEventRequest.Accepted)
            {
                SetValidandoState(false);
                MessageBox.Show($"Revit no pudo aceptar la validación ahora (estado: {req}).\nIntente de nuevo.",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MostrarResultados(ValidationSummary summary)
        {
            _summary = summary;
            string disciplina = cboDisciplina.SelectedItem?.ToString() ?? "";
            try
            {
                ActualizarKpiCards(disciplina);
                ActualizarSidebar(disciplina);
                BindDataGrid();
                ActualizarAutoFixFooter();
                tabValidacion.IsSelected = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al mostrar resultados:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { SetValidandoState(false); }
        }

        private void ActualizarKpiCards(string disciplina)
        {
            var issues = _summary.Issues;
            int faltantes  = issues.Count(i => i.TipoDeProblema == TipoProblema.ParametroFaltante);
            int duplicados = issues.Count(i => i.TipoDeProblema == TipoProblema.Duplicado
                                            || i.TipoDeProblema == TipoProblema.AlcanceIncorrecto);

            int totalElementos     = _summary.TotalElementos;
            int elementosConError  = issues.Select(i => i.ElementId).Distinct().Count();
            double compliance      = totalElementos > 0
                ? Math.Round((totalElementos - elementosConError) * 100.0 / totalElementos, 1)
                : 100.0;

            kpiFamilias.Text   = _summary.TotalFamilias.ToString();
            kpiFaltantes.Text  = faltantes.ToString();
            kpiDuplicados.Text = duplicados.ToString();
            kpiCompliance.Text = $"{compliance:0.0}%";
            pgCompliance.Value = compliance;

            var brush = compliance >= 90
                ? new SolidColorBrush(WpfColor.FromRgb(0x2D, 0x6A, 0x4F))
                : compliance >= 70
                    ? new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x7F, 0x17))
                    : new SolidColorBrush(WpfColor.FromRgb(0xB7, 0x1C, 0x1C));

            kpiCompliance.Foreground = brush;
            pgCompliance.Foreground  = brush;
        }

        private void ActualizarSidebar(string disciplina)
        {
            var issues    = _summary.Issues;
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
            {
                _selectedIssue = issue;
                CargarElementoEnPanel(issue.ElementId);
            }
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
            lblStatusPanel.Text  = "";
            btnAplicarSimilares.IsEnabled = false;
            _selectedElement = null;
        }

        // ─── Edit panel actions ──────────────────────────────────────────────────

        private void BtnGuardarCambios_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedElement == null) { MessageBox.Show("Selecciona un elemento.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var cambios = new Dictionary<string, string>();
            if (lstParametros.ItemsSource is ObservableCollection<ParameterEditModel> items)
            {
                foreach (var item in items)
                {
                    try
                    {
                        Parameter currentParam = _selectedElement.LookupParameter(item.Name);
                        string currentValue = currentParam != null ? GetParameterValueString(currentParam) : "";
                        if (item.Value != currentValue)
                            cambios[item.Name] = item.Value;
                    }
                    catch { }
                }
            }

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

            var items      = lstParametros.ItemsSource as ObservableCollection<ParameterEditModel>;
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
            if (_selectedIssue == null)
            {
                MessageBox.Show("Selecciona un error en el grid primero.", "Atención",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_selectedIssue.TipoDeProblema != TipoProblema.ParametroFaltante
             && _selectedIssue.TipoDeProblema != TipoProblema.AlcanceIncorrecto)
            {
                MessageBox.Show(
                    $"Solo se pueden agregar parámetros de tipo 'Faltante' o 'Alcance incorrecto'.\n" +
                    $"El error seleccionado es: {_selectedIssue.TipoDeProblema}",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Agregar el parámetro '{_selectedIssue.Parametro}' como '{_selectedIssue.Alcance}' " +
                $"a la categoría '{_selectedIssue.Categoria}'?\n\n" +
                "Se creará como parámetro de texto en el proyecto actual.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _agregarHandler.Nombre    = _selectedIssue.Parametro;
            _agregarHandler.Categoria = _selectedIssue.Categoria;
            _agregarHandler.Alcance   = _selectedIssue.Alcance;

            var req = _agregarEvent.Raise();
            if (req != Autodesk.Revit.UI.ExternalEventRequest.Accepted)
                MessageBox.Show($"Revit no pudo procesar la solicitud (estado: {req}). Intente de nuevo.",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ─── Auto Fix ────────────────────────────────────────────────────────────

        private void ActualizarAutoFixFooter()
        {
            if (_summary == null) return;
            var issues = _summary.Issues;

            int criticos     = issues.Count(i => i.Severidad == Severidad.Critico);
            int advertencias = issues.Count(i => i.Severidad == Severidad.Advertencia);
            int autoFixable  = issues.Count(i => i.TipoDeProblema == TipoProblema.ValorVacio
                                              || i.TipoDeProblema == TipoProblema.NombreIncorrecto);

            lblAutoFixInfo.Text    = $"{autoFixable} error(es) pueden corregirse automáticamente.";
            lblAutoFixDetalle.Text = $"Críticos: {criticos}  ·  Advertencias: {advertencias}  ·  Total: {issues.Count}";
        }

        private void BtnAutoFix_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "EJECUTAR AUTO FIX\n\nFunción en desarrollo. Incluirá:\n\n" +
                "  •  Agregar parámetros faltantes según configuración\n" +
                "  •  Corregir nombres (casing / typos)\n" +
                "  •  Aplicar valores por defecto de plantilla\n" +
                "  •  Corregir asignación de Tipo vs Instancia",
                "En desarrollo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetParameterValueString(Parameter param)
        {
            if (param == null) return "";
            try
            {
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
            catch { return ""; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CRUD — Tab Configuración
        // ═══════════════════════════════════════════════════════════════════════

        private void CargarCrudDisciplinas()
        {
            try
            {
                lstCrudDisciplinas.ItemsSource = null;
                lstCrudDisciplinas.ItemsSource = DisciplinasRepository.GetDisciplinas()
                    .Select(d => d.Nombre).ToList();

                LimpiarCrudCategorias();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar disciplinas:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimpiarCrudCategorias()
        {
            _crudDisciplinaActual = null;
            lstCrudCategorias.ItemsSource = null;
            txtNuevaCategoria.Text = "";
            LimpiarCrudParametros();
            lblCategoriasTitulo.Text = "Categorías";
            lblParametrosTitulo.Text = "Parámetros";
        }

        private void LimpiarCrudParametros()
        {
            _crudCategoriaActual         = null;
            _crudParametros              = null;
            _plantilla                   = null;
            _keywords                    = null;
            dgCrudParametros.ItemsSource = null;
            icPlantilla.ItemsSource      = null;
            dgKeywords.ItemsSource       = null;
            if (lblPlantillaPreview != null) lblPlantillaPreview.Text = "";
            if (txtBloqueTexto != null) txtBloqueTexto.Text = "";
            if (txtNuevaKeyword != null) txtNuevaKeyword.Text = "";
        }

        // ─── Disciplinas ─────────────────────────────────────────────────────────

        private void LstCrudDisciplinas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstCrudDisciplinas.SelectedItem is string nombre)
            {
                _crudDisciplinaActual = DisciplinasRepository.GetDisciplinas()
                    .FirstOrDefault(d => d.Nombre == nombre);

                lblCategoriasTitulo.Text = $"Categorías — {nombre}";
                txtNuevaCategoria.Text = "";
                _categoriasCatalogo = DisciplinaCatalogo.GetCategorias(nombre);
                RebuildCategoriasView();

                LimpiarCrudParametros();
                lblParametrosTitulo.Text = "Parámetros";
            }
        }

        private void RebuildCategoriasView()
        {
            txtNuevaCategoria.Text = "";
            AplicarFiltroCategoria();
        }

        private void BtnAgregarDisciplina_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNuevaDisciplina.Text.Trim();
            if (string.IsNullOrEmpty(nombre)) { MessageBox.Show("Ingresa un nombre.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (DisciplinasRepository.GetDisciplinas().Any(d => d.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            { MessageBox.Show("Ya existe una disciplina con ese nombre.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DisciplinasRepository.AddDisciplina(nombre);
            txtNuevaDisciplina.Text = "";
            CargarCrudDisciplinas();
        }

        private void BtnEliminarDisciplina_Click(object sender, RoutedEventArgs e)
        {
            if (_crudDisciplinaActual == null) { MessageBox.Show("Selecciona una disciplina.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var res = MessageBox.Show(
                $"¿Eliminar la disciplina '{_crudDisciplinaActual.Nombre}' y todas sus categorías y parámetros?",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            DisciplinasRepository.RemoveDisciplina(_crudDisciplinaActual.Nombre);
            CargarCrudDisciplinas();
        }

        // ─── Categorías ──────────────────────────────────────────────────────────

        // Filtra el catálogo predefinido por el texto ingresado y muestra en la lista.
        // Las ya agregadas a la disciplina se muestran al inicio con color verde.
        private void AplicarFiltroCategoria()
        {
            if (_categoriasCatalogo == null) return;

            string filtro = txtNuevaCategoria.Text.Trim().ToLower();
            var agregadas = new HashSet<string>(
                _crudDisciplinaActual?.Categorias.Select(c => c.Nombre) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var items = (string.IsNullOrEmpty(filtro)
                    ? _categoriasCatalogo
                    : _categoriasCatalogo.Where(c => c.ToLower().StartsWith(filtro)))
                .Select(c => new CategoriaListItem { Nombre = c, Agregada = agregadas.Contains(c) })
                .OrderBy(c => c.Grupo)
                .ThenBy(c => c.Nombre)
                .ToList();

            var view = CollectionViewSource.GetDefaultView(items);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription("Grupo"));
            lstCrudCategorias.ItemsSource = view;
        }

        private void TxtNuevaCategoria_TextChanged(object sender, TextChangedEventArgs e)
            => AplicarFiltroCategoria();

        private void LstCrudCategorias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_crudDisciplinaActual == null) return;
            if (!(lstCrudCategorias.SelectedItem is CategoriaListItem item)) return;

            _crudCategoriaActual = _crudDisciplinaActual.Categorias
                .FirstOrDefault(c => c.Nombre.Equals(item.Nombre, StringComparison.OrdinalIgnoreCase));

            if (_crudCategoriaActual != null)
            {
                lblParametrosTitulo.Text = $"Parámetros — {item.Nombre}";
                _crudParametros = new ObservableCollection<ParametroConfig>(_crudCategoriaActual.Parametros);
                dgCrudParametros.ItemsSource = _crudParametros;
                CargarPlantilla();
            }
            else
            {
                LimpiarCrudParametros();
                lblParametrosTitulo.Text = "Parámetros — (selecciona una agregada)";
            }
        }

        private void BtnAgregarCategoria_Click(object sender, RoutedEventArgs e)
        {
            if (_crudDisciplinaActual == null) { MessageBox.Show("Selecciona una disciplina primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!(lstCrudCategorias.SelectedItem is CategoriaListItem item)) { MessageBox.Show("Selecciona una categoría de la lista.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (item.Agregada) { MessageBox.Show($"'{item.Nombre}' ya está en esta disciplina.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DisciplinasRepository.AddCategoria(_crudDisciplinaActual.Nombre, item.Nombre);
            AplicarFiltroCategoria();
        }

        private void BtnEliminarCategoria_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null) { MessageBox.Show("Selecciona una categoría ya agregada.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var res = MessageBox.Show(
                $"¿Eliminar '{_crudCategoriaActual.Nombre}' y todos sus parámetros?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            DisciplinasRepository.RemoveCategoria(_crudDisciplinaActual.Nombre, _crudCategoriaActual.Nombre);
            LimpiarCrudParametros();
            lblParametrosTitulo.Text = "Parámetros";
            AplicarFiltroCategoria();
        }

        // ─── Parámetros ──────────────────────────────────────────────────────────

        private void BtnAgregarParamCrud_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null) { MessageBox.Show("Selecciona una categoría primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var nuevo = new ParametroConfig { Nombre = "NuevoParametro", Alcance = "Instancia", Obligatorio = false, Descripcion = "" };
            _crudCategoriaActual.Parametros.Add(nuevo);
            _crudParametros.Add(nuevo);
            DisciplinasRepository.Save();

            dgCrudParametros.SelectedItem = nuevo;
            dgCrudParametros.ScrollIntoView(nuevo);
            dgCrudParametros.BeginEdit();
        }

        private void BtnBuscarParamFamilia_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null) { MessageBox.Show("Selecciona una categoría primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            try
            {
                if (_doc == null) { MessageBox.Show("Abra un proyecto en Revit primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                var paramsEnFamilia = EscanearParametros(_crudCategoriaActual.Nombre);

                if (paramsEnFamilia.Count == 0)
                {
                    MessageBox.Show("No hay elementos o parámetros en esta categoría.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var paramsDisponibles = paramsEnFamilia
                    .Where(p => !_crudCategoriaActual.Parametros.Any(x => x.Nombre.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(p => p).ToList();

                if (paramsDisponibles.Count == 0)
                {
                    MessageBox.Show("Todos los parámetros de esta categoría ya están agregados.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SeleccionarParamDialog(paramsDisponibles);
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Seleccionado))
                {
                    var param = new ParametroConfig { Nombre = dlg.Seleccionado, Alcance = "Instancia", Obligatorio = false };
                    _crudCategoriaActual.Parametros.Add(param);
                    _crudParametros.Add(param);
                    DisciplinasRepository.Save();
                    ActualizarCboParamBloque();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al buscar parámetros:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BuiltInCategory GetBuiltInCategory(string categoryName)
        {
            switch (categoryName)
            {
                case "Pipes":                 return BuiltInCategory.OST_PipeCurves;
                case "Pipe Fittings":         return BuiltInCategory.OST_PipeFitting;
                case "Pipe Accessories":      return BuiltInCategory.OST_PipeAccessory;
                case "Pipe Insulations":      return BuiltInCategory.OST_PipeInsulations;
                case "Pipe Systems":          return BuiltInCategory.OST_PipingSystem;
                case "Plumbing Fixtures":     return BuiltInCategory.OST_PlumbingFixtures;
                case "Flex Pipes":            return BuiltInCategory.OST_FlexPipeCurves;
                case "Sprinklers":            return BuiltInCategory.OST_Sprinklers;
                case "Ducts":                 return BuiltInCategory.OST_DuctCurves;
                case "Duct Fittings":         return BuiltInCategory.OST_DuctFitting;
                case "Duct Accessories":      return BuiltInCategory.OST_DuctAccessory;
                case "Duct Insulations":      return BuiltInCategory.OST_DuctInsulations;
                case "Duct Systems":          return BuiltInCategory.OST_DuctSystem;
                case "Flex Ducts":            return BuiltInCategory.OST_FlexDuctCurves;
                case "Air Terminals":         return BuiltInCategory.OST_DuctTerminal;
                case "Mechanical Equipment":  return BuiltInCategory.OST_MechanicalEquipment;
                case "Electrical Equipment":  return BuiltInCategory.OST_ElectricalEquipment;
                case "Electrical Fixtures":   return BuiltInCategory.OST_ElectricalFixtures;
                case "Conduits":              return BuiltInCategory.OST_Conduit;
                case "Conduit Fittings":      return BuiltInCategory.OST_ConduitFitting;
                case "Cable Trays":           return BuiltInCategory.OST_CableTray;
                case "Cable Tray Fittings":   return BuiltInCategory.OST_CableTrayFitting;
                case "Electrical Circuits":   return BuiltInCategory.OST_ElectricalCircuit;
                case "Lighting Fixtures":     return BuiltInCategory.OST_LightingFixtures;
                case "Lighting Devices":      return BuiltInCategory.OST_LightingDevices;
                case "Communication Devices": return BuiltInCategory.OST_CommunicationDevices;
                case "Data Devices":          return BuiltInCategory.OST_DataDevices;
                case "Nurse Call Devices":    return BuiltInCategory.OST_NurseCallDevices;
                case "Telephone Devices":     return BuiltInCategory.OST_TelephoneDevices;
                case "Security Devices":      return BuiltInCategory.OST_SecurityDevices;
                case "Fire Alarm Devices":    return BuiltInCategory.OST_FireAlarmDevices;
                default:                      return BuiltInCategory.OST_GenericModel;
            }
        }

        // Escanea parámetros de los primeros N elementos de una categoría
        private HashSet<string> EscanearParametros(string categoriaNombre, int maxElementos = 10)
        {
            var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_doc == null) return resultado;

            var bic = GetBuiltInCategory(categoriaNombre);
            if (bic == BuiltInCategory.OST_GenericModel) return resultado;

            ICollection<ElementId> ids;
            try
            {
                ids = new FilteredElementCollector(_doc)
                    .WhereElementIsNotElementType()
                    .OfCategory(bic)
                    .ToElementIds();
            }
            catch { return resultado; }

            int procesados = 0;
            foreach (var eid in ids)
            {
                if (procesados >= maxElementos) break;
                Element elem;
                try { elem = _doc.GetElement(eid); } catch { continue; }
                if (elem == null || !elem.IsValidObject) continue;

                try
                {
                    foreach (Parameter p in elem.Parameters)
                    {
                        try
                        {
                            if (p?.Definition?.Name != null && !string.IsNullOrEmpty(p.Definition.Name))
                                resultado.Add(p.Definition.Name);
                        }
                        catch { }
                    }
                    procesados++;
                }
                catch { }
            }
            return resultado;
        }

        private void BtnEliminarParamCrud_Click(object sender, RoutedEventArgs e)
        {
            if (dgCrudParametros.SelectedItem is ParametroConfig param && _crudCategoriaActual != null)
            {
                _crudCategoriaActual.Parametros.Remove(param);
                _crudParametros.Remove(param);
                DisciplinasRepository.Save();
            }
            else MessageBox.Show("Selecciona un parámetro.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void DgCrudParametros_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DisciplinasRepository.Save();
                    ActualizarCboParamBloque(); // refresca el combo si cambió un nombre
                }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ─── Plantilla de descripción ────────────────────────────────────────────

        private void CargarPlantilla()
        {
            try
            {
                if (_crudCategoriaActual == null) { LimpiarCrudParametros(); return; }

                if (_crudCategoriaActual.Plantilla == null)
                    _crudCategoriaActual.Plantilla = new List<PlantillaBloque>();
                if (_crudCategoriaActual.Keywords == null)
                    _crudCategoriaActual.Keywords = new List<KeywordConfig>();

                var plantillaValida = _crudCategoriaActual.Plantilla
                    .Where(b => b != null && !string.IsNullOrEmpty(b.Valor)).ToList();

                _plantilla = new ObservableCollection<PlantillaBloque>(plantillaValida);
                if (icPlantilla != null) icPlantilla.ItemsSource = _plantilla;

                _keywords = new ObservableCollection<KeywordViewModel>(
                    _crudCategoriaActual.Keywords.Select(k => new KeywordViewModel(k)));
                if (dgKeywords != null) dgKeywords.ItemsSource = _keywords;

                ActualizarPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _plantilla = new ObservableCollection<PlantillaBloque>();
                _keywords  = new ObservableCollection<KeywordViewModel>();
            }
        }

        private void ActualizarCboParamBloque() { } // reservado

        private void ActualizarPreview()
        {
            if (_plantilla == null) { lblPlantillaPreview.Text = ""; return; }
            lblPlantillaPreview.Text = string.Concat(
                _plantilla.Select(b => b.Tipo == "parametro" ? $"{{{b.Valor}}}" : b.Valor));
        }

        private void BtnAgregarTextoBloque_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null) return;
            string texto = txtBloqueTexto.Text;
            if (string.IsNullOrEmpty(texto)) return;

            var bloque = new PlantillaBloque { Tipo = "texto", Valor = texto };
            _crudCategoriaActual.Plantilla.Add(bloque);
            _plantilla.Add(bloque);
            txtBloqueTexto.Text = "";
            DisciplinasRepository.Save();
            ActualizarPreview();
        }

        private void BtnAgregarParamBloque_Click(object sender, RoutedEventArgs e) { }

        // ─── Keywords ────────────────────────────────────────────────────────────

        private void DgKeywords_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void BtnAgregarKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null) { MessageBox.Show("Selecciona una categoría.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string nombre = txtNuevaKeyword.Text.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(nombre)) return;
            if (_crudCategoriaActual.Keywords.Any(k => k.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            { MessageBox.Show("Ya existe esa palabra clave.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var kw = new KeywordConfig { Nombre = nombre };
            _crudCategoriaActual.Keywords.Add(kw);
            _keywords.Add(new KeywordViewModel(kw));
            txtNuevaKeyword.Text = "";
            DisciplinasRepository.Save();
        }

        private void BtnEliminarKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (!(dgKeywords.SelectedItem is KeywordViewModel vm)) { MessageBox.Show("Selecciona una palabra clave.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _crudCategoriaActual.Keywords.Remove(vm.Config);
            _keywords.Remove(vm);
            DisciplinasRepository.Save();
        }

        private void BtnAsociarParamKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (!(dgKeywords.SelectedItem is KeywordViewModel vm)) { MessageBox.Show("Selecciona una palabra clave.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_crudCategoriaActual == null) return;

            try
            {
                if (_doc == null) { MessageBox.Show("Abra un proyecto en Revit primero.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                var paramsEnFamilia = EscanearParametros(_crudCategoriaActual.Nombre);

                var disponibles = paramsEnFamilia
                    .Where(p => !vm.Config.Parametros.Contains(p, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(p => p).ToList();

                if (!disponibles.Any()) { MessageBox.Show("No hay parámetros disponibles.", "Información", MessageBoxButton.OK, MessageBoxImage.Information); return; }

                var dlg = new SeleccionarParamDialog(disponibles);
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Seleccionado))
                {
                    vm.Config.Parametros.Add(dlg.Seleccionado);
                    vm.RefrescarTexto();
                    dgKeywords.Items.Refresh();
                    DisciplinasRepository.Save();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void KeywordAPlantilla_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var vm = (sender as FrameworkElement)?.DataContext as KeywordViewModel;
                if (vm == null || _plantilla == null || _crudCategoriaActual == null) return;
                if (_plantilla.Any(b => b.Tipo == "keyword" && b.Valor.Equals(vm.Config.Nombre, StringComparison.OrdinalIgnoreCase)))
                { MessageBox.Show($"'{vm.Config.Nombre}' ya está en la plantilla.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                var bloque = new PlantillaBloque { Tipo = "keyword", Valor = vm.Config.Nombre };
                _crudCategoriaActual.Plantilla.Add(bloque);
                _plantilla.Add(bloque);
                DisciplinasRepository.Save();
                ActualizarPreview();
            }
            catch { }
        }

        private void BloqueEliminar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var bloque = (sender as FrameworkElement)?.DataContext as PlantillaBloque;
                if (bloque == null || _crudCategoriaActual == null || _plantilla == null) return;

                _crudCategoriaActual.Plantilla.Remove(bloque);
                _plantilla.Remove(bloque);
                DisciplinasRepository.Save();
                ActualizarPreview();
            }
            catch { }
        }

        private void Bloque_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var bloque = (sender as FrameworkElement)?.DataContext as PlantillaBloque;
                if (bloque == null) return;

                var dlg = new EditarBloqueDialog(bloque.Valor);
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Resultado))
                {
                    bloque.Valor = dlg.Resultado;
                    icPlantilla.Items.Refresh();
                    _crudCategoriaActual.Plantilla = new List<PlantillaBloque>(_plantilla);
                    DisciplinasRepository.Save();
                    ActualizarPreview();
                }
                e.Handled = true;
            }
        }

        private void BtnGuardarConfiguracion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisciplinasRepository.Save();
                MessageBox.Show("Configuración guardada correctamente.", "Éxito",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                RefrescarComboValidacion();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAgregarParamAPlantilla_Click(object sender, RoutedEventArgs e)
        {
            if (_crudCategoriaActual == null || _plantilla == null) return;

            string paramNombre = (sender as Button)?.Tag as string;
            if (string.IsNullOrEmpty(paramNombre)) return;

            // No agregar duplicados
            if (_plantilla.Any(b => b.Tipo == "parametro" && b.Valor.Equals(paramNombre, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"'{paramNombre}' ya está en la plantilla.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bloque = new PlantillaBloque { Tipo = "parametro", Valor = paramNombre };
            _crudCategoriaActual.Plantilla.Add(bloque);
            _plantilla.Add(bloque);
            DisciplinasRepository.Save();
            ActualizarPreview();
        }

        // ─── Reordenar bloques ───────────────────────────────────────────────────

        private void BloqueSubir_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var bloque = (sender as FrameworkElement)?.DataContext as PlantillaBloque;
                if (bloque == null || _plantilla == null) return;
                int idx = _plantilla.IndexOf(bloque);
                if (idx <= 0) return;
                _plantilla.Move(idx, idx - 1);
                GuardarPlantilla();
            }
            catch { }
        }

        private void BloqueBajar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var bloque = (sender as FrameworkElement)?.DataContext as PlantillaBloque;
                if (bloque == null || _plantilla == null) return;
                int idx = _plantilla.IndexOf(bloque);
                if (idx < 0 || idx >= _plantilla.Count - 1) return;
                _plantilla.Move(idx, idx + 1);
                GuardarPlantilla();
            }
            catch { }
        }

        private void GuardarPlantilla()
        {
            if (_crudCategoriaActual == null || _plantilla == null) return;
            _crudCategoriaActual.Plantilla = new List<PlantillaBloque>(_plantilla);
            DisciplinasRepository.Save();
            ActualizarPreview();
        }
    }

    // ─── KeywordViewModel ─────────────────────────────────────────────────────

    public class KeywordViewModel
    {
        public KeywordConfig Config { get; }
        public string Nombre => Config.Nombre;
        public string ParametrosTexto => Config.Parametros.Count > 0
            ? string.Join(", ", Config.Parametros)
            : "(sin asociar)";

        public KeywordViewModel(KeywordConfig config) { Config = config; }
        public void RefrescarTexto() { }
    }

    // ─── Seleccionar Parámetro Dialog ────────────────────────────────────────

    public class SeleccionarParamDialog : Window
    {
        public string Seleccionado { get; private set; }

        public SeleccionarParamDialog(List<string> parametros)
        {
            Title = "Seleccionar parámetro de familia";
            Width = 480;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = "Parámetros disponibles en la familia:",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var lb = new ListBox
            {
                ItemsSource = parametros,
                Height = 250,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 16),
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1)
            };
            sp.Children.Add(lb);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var btnOk = new Button
            {
                Content = "Agregar",
                Padding = new Thickness(18, 7, 18, 7),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 11,
                Background = System.Windows.Media.Brushes.CornflowerBlue,
                Foreground = System.Windows.Media.Brushes.White
            };
            btnOk.Click += (s, ev) => { Seleccionado = (string)lb.SelectedItem; DialogResult = true; };

            var btnCancelar = new Button
            {
                Content = "Cancelar",
                Padding = new Thickness(18, 7, 18, 7),
                FontSize = 11
            };
            btnCancelar.Click += (s, ev) => DialogResult = false;

            row.Children.Add(btnOk);
            row.Children.Add(btnCancelar);
            sp.Children.Add(row);

            Content = sp;
            if (lb.Items.Count > 0) lb.SelectedIndex = 0;
        }
    }

    // ─── Editar Bloque Dialog ────────────────────────────────────────────────

    public class EditarBloqueDialog : Window
    {
        public string Resultado { get; private set; }

        public EditarBloqueDialog(string valorActual)
        {
            Title  = "Editar bloque";
            Width  = 340;
            Height = 140;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Nuevo texto:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,6) });

            var txt = new TextBox { Text = valorActual, Padding = new Thickness(6,4,6,4), Margin = new Thickness(0,0,0,12) };
            sp.Children.Add(txt);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Aceptar", Padding = new Thickness(16,6,16,6), Margin = new Thickness(0,0,8,0) };
            btnOk.Click += (s, ev) => { Resultado = txt.Text; DialogResult = true; };
            var btnCancelar = new Button { Content = "Cancelar", Padding = new Thickness(16,6,16,6) };
            btnCancelar.Click += (s, ev) => DialogResult = false;

            row.Children.Add(btnOk);
            row.Children.Add(btnCancelar);
            sp.Children.Add(row);

            Content = sp;
            txt.Focus();
            txt.SelectAll();
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
                ItemsSource   = parameters.Select(p => p.Name).ToList(),
                SelectedIndex = 0,
                Padding       = new Thickness(6, 4, 6, 4),
                Margin        = new Thickness(0, 0, 0, 16)
            };
            sp.Children.Add(cbo);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Aplicar", Padding = new Thickness(20, 7, 20, 7), Margin = new Thickness(0, 0, 8, 0) };
            btnOk.Click += (s, ev) => { SelectedParameter = (string)cbo.SelectedItem; DialogResult = true; };
            var btnCancelar = new Button { Content = "Cancelar", Padding = new Thickness(20, 7, 20, 7) };
            btnCancelar.Click += (s, ev) => DialogResult = false;

            row.Children.Add(btnOk);
            row.Children.Add(btnCancelar);
            sp.Children.Add(row);

            Content = sp;
        }
    }
}
