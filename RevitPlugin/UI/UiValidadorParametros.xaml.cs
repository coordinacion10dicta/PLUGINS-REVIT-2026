using System;
using System.Collections.Generic;
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
        private string _excelPath;

        public UiValidadorParametros(Document doc, string defaultExcelPath)
        {
            InitializeComponent();
            _doc = doc;
            _excelPath = defaultExcelPath;
            txtExcelPath.Text = _excelPath;

            if (File.Exists(_excelPath))
                CargarExcel();
            else
                lblStatus.Text = $"No se encontró '{Path.GetFileName(_excelPath)}'. Use 'Crear plantilla' o 'Examinar'.";
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

                lblStatus.Text = $"Archivo cargado: {_parametros.Count} regla(s) | {disciplinas.Count} disciplina(s).";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el archivo Excel:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                lblStatus.Text = "Error al cargar el archivo.";
            }
        }

        private void BtnExaminar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls",
                Title = "Seleccionar archivo de parámetros requeridos"
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
                Title = "Guardar plantilla de parámetros",
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
                MessageBox.Show(
                    $"Plantilla creada en:\n{dlg.FileName}\n\nCompleta el archivo con los parámetros requeridos de tu proyecto.",
                    "Plantilla creada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear la plantilla:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnValidar_Click(object sender, RoutedEventArgs e)
        {
            if (cboDisciplina.SelectedItem == null)
            {
                MessageBox.Show("Seleccione una disciplina antes de validar.",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_parametros.Any())
            {
                MessageBox.Show("Cargue primero el archivo Excel de parámetros requeridos.",
                    "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsEnabled = false;
                lblStatus.Text = "Validando modelo...";
                lblResumen.Text = "Procesando...";

                string disciplina = cboDisciplina.SelectedItem.ToString();
                var issues = ParameterValidator.Validate(_doc, disciplina, _parametros);

                dgResultados.ItemsSource = issues;

                int faltantes  = issues.Count(i => i.TipoDeProblema == TipoProblema.ParametroFaltante);
                int vacios     = issues.Count(i => i.TipoDeProblema == TipoProblema.ValorVacio);
                int incorrectos = issues.Count(i => i.TipoDeProblema == TipoProblema.NombreIncorrecto);
                int duplicados = issues.Count(i => i.TipoDeProblema == TipoProblema.Duplicado);

                if (!issues.Any())
                {
                    lblResumen.Text = "Validación completada sin problemas.";
                    lblStatus.Text  = $"Disciplina: {disciplina} — Sin problemas detectados.";
                }
                else
                {
                    lblResumen.Text = $"Total: {issues.Count} problema(s) — "
                                    + $"Faltantes: {faltantes}  |  "
                                    + $"Vacíos: {vacios}  |  "
                                    + $"Nombre incorrecto: {incorrectos}  |  "
                                    + $"Duplicados: {duplicados}";
                    lblStatus.Text = $"Disciplina: {disciplina} — {issues.Count} problema(s) encontrado(s).";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la validación:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                lblStatus.Text = "Error durante la validación.";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void BtnAutoFix_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "EJECUTAR AUTO FIX\n\n" +
                "Esta función está planificada para una versión futura.\n\n" +
                "Incluirá:\n" +
                "  •  Agregar parámetros faltantes al proyecto\n" +
                "  •  Corregir nombres de parámetros (casing y typos)\n" +
                "  •  Aplicar plantillas de valores por defecto\n" +
                "  •  Sincronizar parámetros entre elementos similares",
                "Función en desarrollo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
