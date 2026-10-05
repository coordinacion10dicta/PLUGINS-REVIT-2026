using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AutoCAD.SGH.Services;

namespace AutoCAD.SGH.UI
{
    public partial class NormsSelectionDialog : Window
    {
        public bool Confirmed { get; private set; } = false;
        private bool _isUpdatingUi = true;

        public NormsSelectionDialog()
        {
            InitializeComponent();
            RefreshData();
        }

        private void RefreshData()
        {
            _isUpdatingUi = true;

            try
            {
                if (TxtRutaNormas != null)
                {
                    TxtRutaNormas.Text = string.IsNullOrEmpty(OccupancyService.LoadedNormsFilePath)
                        ? "Normas Estándar en Memoria (NSR-10 / NFPA)"
                        : OccupancyService.LoadedNormsFilePath;
                }

                if (RbMultiNorma != null && RbSoloNsr != null && RbSoloNfpa != null)
                {
                    RbMultiNorma.IsEnabled = OccupancyService.HasNsrTable && OccupancyService.HasNfpaTable;
                    RbSoloNsr.IsEnabled = OccupancyService.HasNsrTable;
                    RbSoloNfpa.IsEnabled = OccupancyService.HasNfpaTable;

                    if (OccupancyService.IsMultiNormMode && OccupancyService.HasNsrTable && OccupancyService.HasNfpaTable)
                    {
                        RbMultiNorma.IsChecked = true;
                        if (LblPreviewHeader != null)
                            LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (MULTINORMA: NSR-10 + NFPA):";
                    }
                    else if (OccupancyService.PrimaryNormName.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
                    {
                        RbSoloNfpa.IsChecked = true;
                        if (LblPreviewHeader != null)
                            LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (NORMA ÚNICA: SOLO NFPA):";
                    }
                    else
                    {
                        RbSoloNsr.IsChecked = true;
                        if (LblPreviewHeader != null)
                            LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (NORMA ÚNICA: SOLO NSR-10):";
                    }
                }

                if (GridNormas != null)
                {
                    GridNormas.ItemsSource = null;
                    var items = OccupancyService.GetNormItemsCombined();
                    GridNormas.ItemsSource = items;
                    GridNormas.Items.Refresh();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SGH RefreshData Error]: {ex.Message}");
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || RbMultiNorma == null || RbSoloNsr == null || RbSoloNfpa == null) return;

            if (RbMultiNorma.IsChecked == true)
            {
                OccupancyService.IsMultiNormMode = true;
                OccupancyService.PrimaryNormName = "NSR-10";
                if (LblPreviewHeader != null)
                    LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (MULTINORMA: NSR-10 + NFPA):";
            }
            else if (RbSoloNfpa.IsChecked == true)
            {
                OccupancyService.IsMultiNormMode = false;
                OccupancyService.PrimaryNormName = "NFPA";
                if (LblPreviewHeader != null)
                    LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (NORMA ÚNICA: SOLO NFPA):";
            }
            else if (RbSoloNsr.IsChecked == true)
            {
                OccupancyService.IsMultiNormMode = false;
                OccupancyService.PrimaryNormName = "NSR-10";
                if (LblPreviewHeader != null)
                    LblPreviewHeader.Text = "VISTA PREVIA DE FACTORES Y NORMAS ACTIVAS (NORMA ÚNICA: SOLO NSR-10):";
            }

            if (GridNormas != null)
            {
                GridNormas.ItemsSource = null;
                var items = OccupancyService.GetNormItemsCombined();
                GridNormas.ItemsSource = items;
                GridNormas.Items.Refresh();
            }
        }

        private void BtnExaminar_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new OpenFileDialog
            {
                Title = "Seleccionar Archivo Excel de Normas (NSR-10 / NFPA)",
                Filter = "Libro de Excel (*.xlsx;*.xls)|*.xlsx;*.xls",
                CheckFileExists = true
            };

            if (openDlg.ShowDialog() == true)
            {
                string selectedPath = openDlg.FileName;
                bool ok = OccupancyService.LoadNormsFromExcel(selectedPath);
                if (ok)
                {
                    RefreshData();
                    string normName = OccupancyService.PrimaryNormName;
                    string modeStr = OccupancyService.IsMultiNormMode
                        ? "Multinorma (Detectadas tablas NSR-10 + NFPA)"
                        : $"Norma Única (Detectada 1 tabla: {normName})";

                    MessageBox.Show(
                        $"Se cargaron exitosamente las normas desde el archivo:\n\n{selectedPath}\n\nModo auto-detectado: {modeStr}.",
                        "SGH - Normas Cargadas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "No se pudieron leer las normas desde el archivo seleccionado.\n\nAsegúrese de que el archivo contenga la estructura requerida (Código/USO/Nomenclatura, Descripción/Grupos de Ocupación, Factor).",
                        "SGH - Error al Cargar Normas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }
    }
}
