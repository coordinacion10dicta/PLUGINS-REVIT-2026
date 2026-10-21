using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Forms;
using AutoCAD.SGH.Models;
using AutoCAD.SGH.Services;
using MessageBox = System.Windows.Forms.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace AutoCAD.SGH.UI
{
    public partial class SghReportDialog : Window
    {
        public SghReportModel Model { get; private set; }
        public bool Confirmed { get; private set; } = false;
        private string _targetFolder;
        private bool _isInitializing = true;
        private readonly string _drawingName;

        private readonly List<FloorRowControlItem> _floorRowControls = new List<FloorRowControlItem>();
        private readonly List<SghSpace> _spaces;

        public SghReportDialog(List<SghSpace> spaces, string drawingName)
        {
            _drawingName = drawingName;
            _spaces = spaces ?? new List<SghSpace>();
            InitializeComponent();

            // Cargar modelo con datos previos / predeterminados
            Model = SghReportModel.LoadWithDefaults(drawingName);
            // La fecha del informe siempre predeterminada al día actual
            Model.ReportDate = DateTime.Now.ToString("yyyy/MM/dd");

            // Calcular resumen de espacios y pisos
            int totalSpaces = spaces?.Count ?? 0;
            double totalArea = spaces?.Sum(s => s.Area) ?? 0.0;

            // Inicializar tabla de salidas por piso si aún no existe
            InitializeFloorExitsData(spaces);

            TxtBadgeEspacios.Text = totalSpaces.ToString();
            TxtBadgeArea.Text = $"{totalArea.ToString("0.00", CultureInfo.InvariantCulture)} m²";

            // Carpeta inicial: carpeta del dibujo si existe, sino el Escritorio
            _targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrWhiteSpace(drawingName))
            {
                try
                {
                    string dir = Path.GetDirectoryName(drawingName);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        _targetFolder = dir;
                    }
                }
                catch { }
            }

            // Vincular a los controles
            BindModelToUI();
            _isInitializing = false;
            HookLivePreviewEvents();

            // La vista previa se abre predeterminadamente
            BtnToggleSidePreview.IsChecked = true;
            PnlSidePreview.Visibility = Visibility.Visible;
            PreviewSplitter.Visibility = Visibility.Visible;
            UpdateLivePreview();
            SideWordPreview?.ScrollToSection(MainTabControl.SelectedIndex);
        }

        private void InitializeFloorExitsData(List<SghSpace> spaces)
        {
            if (Model.FloorExits != null && Model.FloorExits.Count > 0)
            {
                Model.FloorExits.RemoveAll(f => f.IsTotal || f.Piso == "TOTAL EDIFICIO");
                return;
            }

            Model.FloorExits = new List<SghFloorExitItem>();

            var groupedSpaces = (spaces ?? new List<SghSpace>())
                .GroupBy(s => OccupancyService.GetCanonicalPisoKey(s.Piso))
                .OrderBy(g => g.Key)
                .ToList();

            if (groupedSpaces.Count == 0)
            {
                // Pisos estándar de respaldo si no hay áreas modeladas
                var defaultFloors = new[] { "N1", "S1", "S2" };
                foreach (var piso in defaultFloors)
                {
                    int occ = piso == "N1" ? 82 : (piso == "S1" ? 30 : 20);
                    int req = CalculateRequiredExits(occ);
                    int exist = 2;
                    Model.FloorExits.Add(new SghFloorExitItem
                    {
                        Piso = piso,
                        Ocupacion = occ,
                        SalidasRequeridas = req,
                        SalidasExistentes = exist,
                        Evaluacion = exist >= req ? "Cumple" : "No Cumple",
                        IsTotal = false
                    });
                }
            }
            else
            {
                foreach (var g in groupedSpaces)
                {
                    int occSum = 0;
                    foreach (var s in g)
                    {
                        double area = s.Area;
                        double? factor = OccupancyService.GetFactor_NSR10(s.GrupoOcupacionNsr);
                        int occ = (int.TryParse(s.CargaOcupacionNsr?.Trim(), out int val) && val > 0)
                            ? val
                            : (factor.HasValue && factor.Value > 0 ? (int)Math.Max(1, Math.Ceiling(area / factor.Value)) : 1);
                        occSum += occ;
                    }

                    int req = CalculateRequiredExits(occSum);
                    int exist = 2;
                    Model.FloorExits.Add(new SghFloorExitItem
                    {
                        Piso = g.Key,
                        Ocupacion = occSum,
                        SalidasRequeridas = req,
                        SalidasExistentes = exist,
                        Evaluacion = exist >= req ? "Cumple" : "No Cumple",
                        IsTotal = false
                    });
                }
            }

            if (Model.FloorExits.Count > 0)
            {
                Model.OcupacionTotalPiso = Model.FloorExits.Sum(f => f.Ocupacion);
                Model.SalidasRequeridas = Model.FloorExits.Max(f => f.SalidasRequeridas);
                Model.SalidasExistentes = Model.FloorExits.Min(f => f.SalidasExistentes);
                Model.CumplimientoCantidadSalidas = Model.FloorExits.All(f => f.Evaluacion == "Cumple") ? "Cumple" : "No Cumple";
            }
        }

        private void BindModelToUI()
        {
            // Portada
            TxtProjectName.Text = Model.ProjectName;
            TxtProjectDescription.Text = Model.ProjectDescription;
            TxtVersion.Text = Model.Version;
            TxtReportDate.Text = Model.ReportDate;
            TxtArchBaseDate.Text = Model.ArchBaseDate;

            TxtDirectorName.Text = Model.DirectorName;
            TxtDirectorCert.Text = Model.DirectorCert;
            TxtCoordinatorName.Text = Model.CoordinatorName;
            TxtAuthorName.Text = Model.AuthorName;
            TxtCollaboratorName.Text = Model.CollaboratorName;

            // Parámetros del Edificio - Selector de Norma
            if (Model.SelectedNorm != null && Model.SelectedNorm.Equals("NFPA", StringComparison.OrdinalIgnoreCase))
            {
                RbNormNfpa.IsChecked = true;
            }
            else
            {
                RbNormNsr.IsChecked = true;
            }

            // Población de Clasificación Principal y Secundaria según la norma seleccionada
            PopulateOccupancyGroups(Model.SelectedNorm ?? "NSR-10", Model.BuildingClassification, Model.SecondaryClassification);

            TxtBuildingHeight.Text = Model.BuildingHeight.ToString("0.0", CultureInfo.InvariantCulture);

            // Rociadores
            ChkSprinklers.IsChecked = Model.HasSprinklers;
            if (ChkSprinklersSalidas != null) ChkSprinklersSalidas.IsChecked = Model.HasSprinklers;

            // Salidas y Evacuación - Construcción dinámica de la tabla de salidas por piso
            BuildFloorExitTableUI();

            TxtDiagonalEdificio.Text = Model.DiagonalEdificioM.ToString("0.0", CultureInfo.InvariantCulture);
            TxtSeparacionSalidas.Text = Model.SeparacionSalidasExistenteM.ToString("0.0", CultureInfo.InvariantCulture);
            UpdateSeparationEvaluation();

            TxtDistanciaMax.Text = Model.DistanciaRecorridoMaxPermitidaM.ToString("0.0", CultureInfo.InvariantCulture);
            TxtDistanciaExistente.Text = Model.DistanciaRecorridoExistenteM.ToString("0.0", CultureInfo.InvariantCulture);
            UpdateTravelDistanceEvaluation();

            SelectComboBoxItem(CmbCumplimientoDescarga, Model.CumplimientoDescargaSalidas);
            SelectComboBoxItem(CmbCumplimientoCapacidad, Model.CumplimientoCapacidadMedios);

            // Conclusiones
            TxtConclusiones.Text = Model.Conclusiones;

            // Plantilla Word y Checklist de Secciones
            if (Model.UseCustomTemplate && !string.IsNullOrWhiteSpace(Model.CustomTemplatePath))
            {
                RbTemplateCustom.IsChecked = true;
                TxtCustomTemplatePath.Text = Model.CustomTemplatePath;
                BrdCustomTemplatePicker.Visibility = Visibility.Visible;
            }
            else
            {
                RbTemplateStandard.IsChecked = true;
                TxtCustomTemplatePath.Text = string.Empty;
                BrdCustomTemplatePicker.Visibility = Visibility.Collapsed;
            }

            ChkReplaceFromSection5Only.IsChecked = Model.ReplaceFromSection5Only;
            ChkReplaceMetadata.IsChecked = Model.ReplaceMetadata;
            ChkReplaceClasificacionRiesgo.IsChecked = Model.ReplaceClasificacionRiesgo;
            ChkReplaceCargasOcupacion.IsChecked = Model.ReplaceCargasOcupacion;
            ChkReplaceMediosEvacuacion.IsChecked = Model.ReplaceMediosEvacuacion;
            ChkReplaceProteccionActivaPasiva.IsChecked = Model.ReplaceProteccionActivaPasiva;
            ChkReplaceConclusiones.IsChecked = Model.ReplaceConclusiones;
            ChkReplaceImagenesProyecto.IsChecked = Model.ReplaceImagenesProyecto;

            UpdateHighRiseText();
            UpdateImagePreviews();
        }

        private void PopulateOccupancyGroups(string normName, string selectedPrimary, string selectedSecondary)
        {
            var groups = OccupancyService.GetDistinctGroupNamesForNorm(normName);
            if (groups == null || groups.Count == 0)
            {
                groups = normName.Equals("NFPA", StringComparison.OrdinalIgnoreCase)
                    ? new List<string> { "Reunión", "Educativo", "Cuidado de la Salud", "Residencial", "Mercantil", "Negocios", "Industrial", "Almacenamiento" }
                    : new List<string> { "Almacenamiento (A)", "Comercial (C)", "Especial (E)", "Fabril e Industrial (F)", "Institucional (I)", "Lugares de Reunión (L)", "Mixto (M)", "Alta Peligrosidad (P)", "Residencial (R)", "Temporal (T)" };
            }

            // 1. Cargar combo de Clasificación Principal
            CmbBuildingClassification.Items.Clear();
            foreach (var g in groups)
            {
                CmbBuildingClassification.Items.Add(g);
            }

            if (!string.IsNullOrWhiteSpace(selectedPrimary))
            {
                int matchIdx = groups.FindIndex(x => x.IndexOf(selectedPrimary, StringComparison.OrdinalIgnoreCase) >= 0 || selectedPrimary.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
                CmbBuildingClassification.SelectedIndex = matchIdx >= 0 ? matchIdx : 0;
            }
            else
            {
                CmbBuildingClassification.SelectedIndex = 0;
            }

            // 2. Cargar checkboxes de Clasificación Secundaria (Selección Múltiple)
            PnlSecondaryCheckboxes.Children.Clear();
            var selectedSecondaryList = (selectedSecondary ?? string.Empty)
                .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            foreach (var g in groups)
            {
                var chk = new CheckBox
                {
                    Content = g,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(4, 4, 4, 4),
                    IsChecked = selectedSecondaryList.Any(s => g.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)
                };
                chk.Checked += SecondaryCheckBox_Changed;
                chk.Unchecked += SecondaryCheckBox_Changed;
                PnlSecondaryCheckboxes.Children.Add(chk);
            }

            UpdateSecondaryTextFromCheckboxes();
            UpdateRiskCategoryOptions();
        }

        private void RbNorm_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            string norm = RbNormNfpa.IsChecked == true ? "NFPA" : "NSR-10";
            PopulateOccupancyGroups(norm, CmbBuildingClassification.SelectedItem as string, TxtSecondaryClassification.Text);
            UpdateHighRiseText();
            UpdateLivePreview();
        }

        private void CmbBuildingClassification_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateRiskCategoryOptions();
        }

        private bool IsResidentialClass(string classification)
        {
            if (string.IsNullOrWhiteSpace(classification)) return false;
            string bc = classification.Trim();
            if (bc.IndexOf("R-1", StringComparison.OrdinalIgnoreCase) >= 0 || 
                bc.IndexOf("R-2", StringComparison.OrdinalIgnoreCase) >= 0 || 
                bc.IndexOf("R1", StringComparison.OrdinalIgnoreCase) >= 0 || 
                bc.IndexOf("R2", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (bc.IndexOf("Residencial", StringComparison.OrdinalIgnoreCase) >= 0 && 
                bc.IndexOf("Hoteles", StringComparison.OrdinalIgnoreCase) < 0 && 
                bc.IndexOf("R-3", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (bc.Equals("R", StringComparison.OrdinalIgnoreCase) || 
                bc.StartsWith("R ", StringComparison.OrdinalIgnoreCase) || 
                bc.Contains("(R)"))
                return true;

            return false;
        }

        private void UpdateRiskCategoryOptions()
        {
            string selectedClass = CmbBuildingClassification?.SelectedItem as string ?? string.Empty;
            bool isR1R2 = IsResidentialClass(selectedClass);

            string currentSelection = (CmbRiskCategory?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? Model.RiskCategory ?? string.Empty;

            CmbRiskCategory.Items.Clear();

            if (isR1R2)
            {
                LblRiskCategoryTitle.Text = "Categoría de Riesgo (J.3.4):";
                TxtRiskTableHint.Text = "ℹ Se incluirán ambas tablas (J.3.4-3 Excepto R-1/R-2 y J.3.4-4 Residencial R-1/R-2) en el informe Word.";

                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría I (Edificaciones de 4 o más pisos)" });
                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría II (Edificaciones de 2 y 3 pisos)" });
                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría III (Edificaciones de 1 piso)" });

                SelectComboBoxItem(CmbRiskCategory, currentSelection);
                if (CmbRiskCategory.SelectedIndex < 0) CmbRiskCategory.SelectedIndex = 0;
            }
            else
            {
                LblRiskCategoryTitle.Text = "Categoría de Riesgo (J.3.4):";
                TxtRiskTableHint.Text = "ℹ Se incluirán ambas tablas (J.3.4-3 Excepto R-1/R-2 y J.3.4-4 Residencial R-1/R-2) en el informe Word.";

                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría I (Alta amenaza / Gran almacenamiento)" });
                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría II (Riesgo intermedio)" });
                CmbRiskCategory.Items.Add(new ComboBoxItem { Content = "Categoría III (Baja combustión / Riesgo leve)" });

                SelectComboBoxItem(CmbRiskCategory, currentSelection);
                if (CmbRiskCategory.SelectedIndex < 0) CmbRiskCategory.SelectedIndex = 1;
            }
        }

        private void SecondaryCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSecondaryTextFromCheckboxes();
            UpdateLivePreview();
        }

        private void UpdateSecondaryTextFromCheckboxes()
        {
            var selected = new List<string>();
            foreach (var child in PnlSecondaryCheckboxes.Children)
            {
                if (child is CheckBox chk && chk.IsChecked == true && chk.Content != null)
                {
                    selected.Add(chk.Content.ToString());
                }
            }

            if (selected.Count > 0)
            {
                TxtSecondaryClassification.Text = string.Join(", ", selected);
                TxtSecondaryClassification.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4));
            }
            else
            {
                TxtSecondaryClassification.Text = "Seleccionar usos secundarios...";
                TxtSecondaryClassification.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6C, 0x70, 0x86));
            }
        }

        private void BtnToggleSecondaryMenu_Click(object sender, RoutedEventArgs e)
        {
            PopupSecondaryGroups.IsOpen = !PopupSecondaryGroups.IsOpen;
        }

        private void BtnCloseSecondaryPopup_Click(object sender, RoutedEventArgs e)
        {
            PopupSecondaryGroups.IsOpen = false;
        }

        private void BtnCalReportDate_Click(object sender, RoutedEventArgs e)
        {
            if (DateTime.TryParse(TxtReportDate.Text, out DateTime dt))
            {
                CalReportDate.SelectedDate = dt;
                CalReportDate.DisplayDate = dt;
            }
            else
            {
                CalReportDate.SelectedDate = DateTime.Today;
                CalReportDate.DisplayDate = DateTime.Today;
            }
            PopupCalReportDate.IsOpen = true;
        }

        private void CalReportDate_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalReportDate.SelectedDate.HasValue)
            {
                TxtReportDate.Text = CalReportDate.SelectedDate.Value.ToString("yyyy/MM/dd");
                PopupCalReportDate.IsOpen = false;
            }
        }

        private void BtnCalArchBaseDate_Click(object sender, RoutedEventArgs e)
        {
            if (DateTime.TryParse(TxtArchBaseDate.Text, out DateTime dt))
            {
                CalArchBaseDate.SelectedDate = dt;
                CalArchBaseDate.DisplayDate = dt;
            }
            else
            {
                CalArchBaseDate.SelectedDate = DateTime.Today;
                CalArchBaseDate.DisplayDate = DateTime.Today;
            }
            PopupCalArchBaseDate.IsOpen = true;
        }

        private void CalArchBaseDate_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalArchBaseDate.SelectedDate.HasValue)
            {
                TxtArchBaseDate.Text = CalArchBaseDate.SelectedDate.Value.ToString("yyyy/MM/dd");
                PopupCalArchBaseDate.IsOpen = false;
            }
        }

        private void SelectComboBoxItem(System.Windows.Controls.ComboBox combo, string text)
        {
            if (combo == null || string.IsNullOrWhiteSpace(text)) return;
            string t = text.Trim();

            // 1. Coincidencia exacta primero
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string content = (combo.Items[i] is ComboBoxItem item) ? item.Content?.ToString()?.Trim() : combo.Items[i]?.ToString()?.Trim();
                if (string.Equals(content, t, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            // 2. Coincidencia parcial si no hubo exacta
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string content = (combo.Items[i] is ComboBoxItem item) ? item.Content?.ToString()?.Trim() : combo.Items[i]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(content) && content.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void BindUIToModel()
        {
            Model.ProjectName = TxtProjectName.Text.Trim();
            Model.ProjectDescription = TxtProjectDescription.Text.Trim();
            Model.Version = TxtVersion.Text.Trim();
            Model.ReportDate = TxtReportDate.Text.Trim();
            Model.ArchBaseDate = TxtArchBaseDate.Text.Trim();

            Model.DirectorName = TxtDirectorName.Text.Trim();
            Model.DirectorCert = TxtDirectorCert.Text.Trim();
            Model.CoordinatorName = TxtCoordinatorName.Text.Trim();
            Model.AuthorName = TxtAuthorName.Text.Trim();
            Model.CollaboratorName = TxtCollaboratorName.Text.Trim();

            Model.SelectedNorm = RbNormNfpa.IsChecked == true ? "NFPA" : "NSR-10";
            Model.BuildingClassification = CmbBuildingClassification.SelectedItem as string ?? string.Empty;
            
            string sec = TxtSecondaryClassification.Text.Trim();
            Model.SecondaryClassification = sec == "Seleccionar usos secundarios..." ? string.Empty : sec;

            if (double.TryParse(TxtBuildingHeight.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double h))
            {
                Model.BuildingHeight = h;
            }

            if (CmbRiskCategory.SelectedItem is ComboBoxItem itemRisk)
            {
                string content = itemRisk.Content.ToString();
                Model.RiskCategory = content.Split('(')[0].Trim();
            }

            // Deducir resistencia al fuego general automáticamente según la categoría de riesgo
            string rCat = Model.RiskCategory ?? string.Empty;
            if (rCat.IndexOf("III", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Model.GeneralFireResistance = "1 Hora";
            }
            else if (rCat.IndexOf("II", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Model.GeneralFireResistance = Model.IsResidentialR1R2 ? "1 Hora" : "2 Horas";
            }
            else
            {
                Model.GeneralFireResistance = Model.IsResidentialR1R2 ? "2 Horas" : "3 Horas";
            }

            Model.HasSprinklers = ChkSprinklers.IsChecked == true;

            // Salidas y Evacuación por piso
            Model.FloorExits.Clear();
            foreach (var r in _floorRowControls)
            {
                if (r.IsTotal || r.TxtPiso.Text.Trim().Equals("TOTAL EDIFICIO", StringComparison.OrdinalIgnoreCase))
                    continue;

                int occVal = int.TryParse(r.TxtOcupacion.Text.Trim(), out int o) ? o : 0;
                int reqVal = int.TryParse(r.TxtSalidasReq.Text.Trim(), out int rq) ? rq : 1;
                int existVal = int.TryParse(r.TxtSalidasExist.Text.Trim(), out int ex) ? ex : 2;
                string evalStr = (r.CmbEvaluacion.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? (existVal >= reqVal ? "Cumple" : "No Cumple");

                var item = new SghFloorExitItem
                {
                    Piso = r.TxtPiso.Text.Trim(),
                    Ocupacion = occVal,
                    SalidasRequeridas = reqVal,
                    SalidasExistentes = existVal,
                    Evaluacion = evalStr,
                    IsTotal = false
                };
                Model.FloorExits.Add(item);
            }

            if (Model.FloorExits.Count > 0)
            {
                Model.OcupacionTotalPiso = Model.FloorExits.Sum(f => f.Ocupacion);
                Model.SalidasRequeridas = Model.FloorExits.Max(f => f.SalidasRequeridas);
                Model.SalidasExistentes = Model.FloorExits.Min(f => f.SalidasExistentes);
                Model.CumplimientoCantidadSalidas = Model.FloorExits.All(f => f.Evaluacion == "Cumple") ? "Cumple" : "No Cumple";
            }

            if (double.TryParse(TxtDiagonalEdificio.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) Model.DiagonalEdificioM = dVal;
            if (double.TryParse(TxtSeparacionSalidas.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double sVal)) Model.SeparacionSalidasExistenteM = sVal;
            if (CmbCumplimientoSeparacion.SelectedItem is ComboBoxItem itemSep) Model.CumplimientoSeparacionSalidas = itemSep.Content.ToString();

            if (double.TryParse(TxtDistanciaMax.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double dMax)) Model.DistanciaRecorridoMaxPermitidaM = dMax;
            if (double.TryParse(TxtDistanciaExistente.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double dReal)) Model.DistanciaRecorridoExistenteM = dReal;
            if (CmbCumplimientoDistancia.SelectedItem is ComboBoxItem itemDist) Model.CumplimientoDistanciaRecorrido = itemDist.Content.ToString();

            if (CmbCumplimientoDescarga.SelectedItem is ComboBoxItem itemDesc) Model.CumplimientoDescargaSalidas = itemDesc.Content.ToString();
            if (CmbCumplimientoCapacidad.SelectedItem is ComboBoxItem itemCap) Model.CumplimientoCapacidadMedios = itemCap.Content.ToString();

            // Conclusiones
            Model.Conclusiones = TxtConclusiones.Text.Trim();

            // Plantilla Word y Checklist de Secciones
            Model.UseCustomTemplate = RbTemplateCustom.IsChecked == true;
            Model.CustomTemplatePath = TxtCustomTemplatePath.Text.Trim();
            Model.ReplaceFromSection5Only = ChkReplaceFromSection5Only.IsChecked == true;

            Model.ReplaceMetadata = ChkReplaceMetadata.IsChecked == true;
            Model.ReplaceClasificacionRiesgo = ChkReplaceClasificacionRiesgo.IsChecked == true;
            Model.ReplaceCargasOcupacion = ChkReplaceCargasOcupacion.IsChecked == true;
            Model.ReplaceMediosEvacuacion = ChkReplaceMediosEvacuacion.IsChecked == true;
            Model.ReplaceProteccionActivaPasiva = ChkReplaceProteccionActivaPasiva.IsChecked == true;
            Model.ReplaceConclusiones = ChkReplaceConclusiones.IsChecked == true;
            Model.ReplaceImagenesProyecto = ChkReplaceImagenesProyecto.IsChecked == true;

            Model.GenerateExcel = true;
            Model.GenerateWord = true;
        }

        private void RbTemplate_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || BrdCustomTemplatePicker == null) return;
            bool isCustom = RbTemplateCustom.IsChecked == true;
            BrdCustomTemplatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            Model.UseCustomTemplate = isCustom;
            UpdateLivePreview();
        }

        private void BtnBrowseCustomTemplate_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Seleccionar Plantilla Word del Proyecto (.docx)";
                dlg.Filter = "Documentos de Word (*.docx)|*.docx|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && File.Exists(dlg.FileName))
                {
                    TxtCustomTemplatePath.Text = dlg.FileName;
                    Model.CustomTemplatePath = dlg.FileName;
                    Model.UseCustomTemplate = true;
                    RbTemplateCustom.IsChecked = true;
                    BrdCustomTemplatePicker.Visibility = Visibility.Visible;
                    UpdateLivePreview();
                }
            }
        }

        private void BtnClearCustomTemplate_Click(object sender, RoutedEventArgs e)
        {
            TxtCustomTemplatePath.Text = string.Empty;
            Model.CustomTemplatePath = string.Empty;
            Model.UseCustomTemplate = false;
            RbTemplateStandard.IsChecked = true;
            BrdCustomTemplatePicker.Visibility = Visibility.Collapsed;
            UpdateLivePreview();
        }

        private void ChkReplaceFromSection5Only_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            Model.ReplaceFromSection5Only = ChkReplaceFromSection5Only.IsChecked == true;
            UpdateLivePreview();
        }

        private void BtnCheckAllSections_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecklistItems(true);
        }

        private void BtnUncheckAllSections_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecklistItems(false);
        }

        private void SetAllChecklistItems(bool check)
        {
            ChkReplaceMetadata.IsChecked = check;
            ChkReplaceClasificacionRiesgo.IsChecked = check;
            ChkReplaceCargasOcupacion.IsChecked = check;
            ChkReplaceMediosEvacuacion.IsChecked = check;
            ChkReplaceProteccionActivaPasiva.IsChecked = check;
            ChkReplaceConclusiones.IsChecked = check;
            ChkReplaceImagenesProyecto.IsChecked = check;
            UpdateLivePreview();
        }

        public static int CalculateRequiredExits(int occ)
        {
            // Fórmula NSR-10 K.3.4 / Excel: =+IF(C5<101;1;(IF(C5<501;2;(IF(C5<1001;3;4)))))
            if (occ <= 100) return 1;
            if (occ <= 500) return 2;
            if (occ <= 1000) return 3;
            return 4;
        }

        private void BuildFloorExitTableUI()
        {
            if (PnlFloorExitRows == null) return;

            PnlFloorExitRows.Children.Clear();
            _floorRowControls.Clear();

            var brushConverter = new System.Windows.Media.BrushConverter();
            var bgBox = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#313244");
            var borderBox = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#45475A");
            var fgText = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#CDD6F4");
            var totalBg = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#181825");
            var accentBlue = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#89B4FA");

            foreach (var item in Model.FloorExits)
            {
                if (item.IsTotal || item.Piso.Equals("TOTAL EDIFICIO", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rowBorder = new Border
                {
                    Background = item.IsTotal ? totalBg : System.Windows.Media.Brushes.Transparent,
                    BorderBrush = item.IsTotal ? accentBlue : borderBox,
                    BorderThickness = item.IsTotal ? new Thickness(0, 1.5, 0, 0) : new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(4, 5, 4, 5),
                    Margin = item.IsTotal ? new Thickness(0, 4, 0, 0) : new Thickness(0)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Columna 0: Piso / Nivel (Editable)
                var txtPiso = new System.Windows.Controls.TextBox
                {
                    Text = item.Piso,
                    Foreground = item.IsTotal ? accentBlue : fgText,
                    FontWeight = item.IsTotal ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 12.5,
                    Background = item.IsTotal ? (System.Windows.Media.Brush)brushConverter.ConvertFromString("#11111B") : bgBox,
                    BorderBrush = borderBox,
                    BorderThickness = new Thickness(1),
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(8, 3, 4, 3),
                    Margin = new Thickness(2, 0, 4, 0)
                };
                Grid.SetColumn(txtPiso, 0);
                grid.Children.Add(txtPiso);

                // Columna 1: Ocupación Total (Editable)
                var txtOcc = new System.Windows.Controls.TextBox
                {
                    Text = item.Ocupacion.ToString(),
                    Foreground = fgText,
                    FontWeight = item.IsTotal ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 13,
                    Background = bgBox,
                    BorderBrush = borderBox,
                    BorderThickness = new Thickness(1),
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    Padding = new Thickness(2, 2, 2, 2),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(txtOcc, 1);
                grid.Children.Add(txtOcc);

                // Columna 2: Salidas Requeridas (Editable)
                var txtReq = new System.Windows.Controls.TextBox
                {
                    Text = item.SalidasRequeridas.ToString(),
                    Foreground = fgText,
                    FontWeight = item.IsTotal ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 13,
                    Background = bgBox,
                    BorderBrush = borderBox,
                    BorderThickness = new Thickness(1),
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    Padding = new Thickness(2, 2, 2, 2),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(txtReq, 2);
                grid.Children.Add(txtReq);

                // Columna 3: Salidas Existentes (Editable)
                var txtExist = new System.Windows.Controls.TextBox
                {
                    Text = item.SalidasExistentes.ToString(),
                    Foreground = fgText,
                    FontWeight = item.IsTotal ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 13,
                    Background = bgBox,
                    BorderBrush = borderBox,
                    BorderThickness = new Thickness(1),
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    Padding = new Thickness(2, 2, 2, 2),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(txtExist, 3);
                grid.Children.Add(txtExist);

                // Columna 4: Evaluación (ComboBox Editable/Seleccionable)
                var cmbEval = new System.Windows.Controls.ComboBox
                {
                    Height = 32,
                    FontSize = 12,
                    FontWeight = item.IsTotal ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(4, 0, 2, 0)
                };
                cmbEval.Items.Add(new ComboBoxItem { Content = "Cumple" });
                cmbEval.Items.Add(new ComboBoxItem { Content = "No Cumple" });
                SelectComboBoxItem(cmbEval, item.Evaluacion ?? "Cumple");
                Grid.SetColumn(cmbEval, 4);
                grid.Children.Add(cmbEval);

                rowBorder.Child = grid;
                PnlFloorExitRows.Children.Add(rowBorder);

                var rowCtrl = new FloorRowControlItem
                {
                    Piso = item.Piso,
                    IsTotal = item.IsTotal,
                    TxtPiso = txtPiso,
                    TxtOcupacion = txtOcc,
                    TxtSalidasReq = txtReq,
                    TxtSalidasExist = txtExist,
                    CmbEvaluacion = cmbEval
                };
                _floorRowControls.Add(rowCtrl);

                // Conectar eventos reactivos
                txtOcc.TextChanged += (s, e) => OnFloorOccupancyChanged(rowCtrl);
                txtReq.TextChanged += (s, e) => OnFloorExitsChanged(rowCtrl);
                txtExist.TextChanged += (s, e) => OnFloorExitsChanged(rowCtrl);
            }
        }

        private void OnFloorOccupancyChanged(FloorRowControlItem row)
        {
            if (_isInitializing) return;

            if (int.TryParse(row.TxtOcupacion.Text.Trim(), out int occ))
            {
                int req = CalculateRequiredExits(occ);
                row.TxtSalidasReq.Text = req.ToString();
                UpdateRowEvaluation(row);
            }
        }

        private void OnFloorExitsChanged(FloorRowControlItem row)
        {
            if (_isInitializing) return;
            UpdateRowEvaluation(row);
        }

        private void UpdateRowEvaluation(FloorRowControlItem row)
        {
            if (row == null || row.TxtSalidasReq == null || row.TxtSalidasExist == null || row.CmbEvaluacion == null) return;

            if (int.TryParse(row.TxtSalidasReq.Text.Trim(), out int req) &&
                int.TryParse(row.TxtSalidasExist.Text.Trim(), out int exist))
            {
                if (exist >= req)
                {
                    SelectComboBoxItem(row.CmbEvaluacion, "Cumple");
                }
                else
                {
                    SelectComboBoxItem(row.CmbEvaluacion, "No Cumple");
                }
            }
            UpdateLivePreview();
        }

        private void RecalculateTotalFloorRow()
        {
            var totalRow = _floorRowControls.FirstOrDefault(r => r.IsTotal);
            if (totalRow == null) return;

            int sumOcc = 0;
            foreach (var r in _floorRowControls.Where(r => !r.IsTotal))
            {
                if (int.TryParse(r.TxtOcupacion.Text.Trim(), out int occ))
                {
                    sumOcc += occ;
                }
            }

            totalRow.TxtOcupacion.Text = sumOcc.ToString();
            int totalReq = CalculateRequiredExits(sumOcc);
            totalRow.TxtSalidasReq.Text = totalReq.ToString();
            UpdateRowEvaluation(totalRow);
        }

        private class FloorRowControlItem
        {
            public string Piso { get; set; }
            public bool IsTotal { get; set; }
            public System.Windows.Controls.TextBox TxtPiso { get; set; }
            public System.Windows.Controls.TextBox TxtOcupacion { get; set; }
            public System.Windows.Controls.TextBox TxtSalidasReq { get; set; }
            public System.Windows.Controls.TextBox TxtSalidasExist { get; set; }
            public System.Windows.Controls.ComboBox CmbEvaluacion { get; set; }
        }

        private void TxtSeparacion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateSeparationEvaluation();
        }

        private void ChkSprinklers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (ChkSprinklersSalidas != null && ChkSprinklersSalidas.IsChecked != ChkSprinklers.IsChecked)
            {
                ChkSprinklersSalidas.IsChecked = ChkSprinklers.IsChecked;
            }
            UpdateSeparationEvaluation();
            UpdateLivePreview();
        }

        private void ChkSprinklersSalidas_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (ChkSprinklers != null && ChkSprinklers.IsChecked != ChkSprinklersSalidas.IsChecked)
            {
                ChkSprinklers.IsChecked = ChkSprinklersSalidas.IsChecked;
            }
            UpdateSeparationEvaluation();
            UpdateLivePreview();
        }

        private void UpdateSeparationEvaluation()
        {
            if (CmbCumplimientoSeparacion == null || TxtDiagonalEdificio == null || TxtSeparacionSalidas == null) return;

            bool hasSprinklers = ChkSprinklers?.IsChecked == true;
            if (ChkSprinklersSalidas != null && ChkSprinklersSalidas.IsChecked != hasSprinklers)
            {
                ChkSprinklersSalidas.IsChecked = hasSprinklers;
            }

            if (double.TryParse(TxtDiagonalEdificio.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double diag) &&
                double.TryParse(TxtSeparacionSalidas.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double sep))
            {
                double reqSep = hasSprinklers ? (diag / 3.0) : (diag / 2.0);
                string fractionStr = hasSprinklers ? "1/3 de D" : "1/2 de D (la mitad)";

                if (sep >= reqSep - 0.01)
                {
                    SelectComboBoxItem(CmbCumplimientoSeparacion, "Cumple");
                    if (TxtSeparacionFormulaIndicator != null)
                    {
                        TxtSeparacionFormulaIndicator.Text = $"✓ Cumple: Separación ({sep:0.00}m) ≥ {fractionStr} ({reqSep:0.00}m)";
                        TxtSeparacionFormulaIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                    }
                }
                else
                {
                    SelectComboBoxItem(CmbCumplimientoSeparacion, "No Cumple");
                    if (TxtSeparacionFormulaIndicator != null)
                    {
                        TxtSeparacionFormulaIndicator.Text = $"⚠ No Cumple: Separación ({sep:0.00}m) < {fractionStr} ({reqSep:0.00}m)";
                        TxtSeparacionFormulaIndicator.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                }
            }
            UpdateLivePreview();
        }

        private void TxtDistancia_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateTravelDistanceEvaluation();
        }

        private void UpdateTravelDistanceEvaluation()
        {
            if (CmbCumplimientoDistancia == null || TxtDistanciaExistente == null || TxtDistanciaMax == null) return;

            if (double.TryParse(TxtDistanciaExistente.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double distExist) &&
                double.TryParse(TxtDistanciaMax.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double distMax))
            {
                if (distExist <= distMax + 0.001)
                {
                    SelectComboBoxItem(CmbCumplimientoDistancia, "Cumple");
                    if (TxtDistanciaFormulaIndicator != null)
                    {
                        TxtDistanciaFormulaIndicator.Text = $"✓ Cumple: Distancia Existente ({distExist:0.00}m) ≤ Distancia Máxima Permitida ({distMax:0.00}m)";
                        TxtDistanciaFormulaIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                    }
                }
                else
                {
                    SelectComboBoxItem(CmbCumplimientoDistancia, "No Cumple");
                    if (TxtDistanciaFormulaIndicator != null)
                    {
                        TxtDistanciaFormulaIndicator.Text = $"⚠ No Cumple: Distancia Existente ({distExist:0.00}m) > Distancia Máxima Permitida ({distMax:0.00}m)";
                        TxtDistanciaFormulaIndicator.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                }
            }
            UpdateLivePreview();
        }

        private void UpdateHighRiseText()
        {
            if (TxtHighRiseIndicator == null || TxtBuildingHeight == null) return;

            bool isNfpa = RbNormNfpa?.IsChecked == true;
            double threshold = isNfpa ? 23.0 : 28.0;
            string normRef = isNfpa ? "NFPA 101" : "NSR-10 K.3.1.3";

            if (double.TryParse(TxtBuildingHeight.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double h))
            {
                if (h >= threshold)
                {
                    TxtHighRiseIndicator.Text = $"⚠ Edificio de Gran Altura (≥ {threshold:0}m) - Aplican requisitos especiales {normRef}";
                    TxtHighRiseIndicator.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    TxtHighRiseIndicator.Text = $"ℹ Edificio de Altura Convencional (< {threshold:0}m)";
                    TxtHighRiseIndicator.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                }
            }
        }

        private void TxtBuildingHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateHighRiseText();
        }

        private void UpdateImagePreviews()
        {
            // Implantación
            if (!string.IsNullOrWhiteSpace(Model.ImplantacionImagePath) && File.Exists(Model.ImplantacionImagePath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(Model.ImplantacionImagePath);
                    bmp.EndInit();
                    ImgPreviewImplantacion.Source = bmp;
                    TxtPlaceholderImplantacion.Visibility = Visibility.Collapsed;
                    TxtImplantacionPath.Text = Model.ImplantacionImagePath;
                }
                catch
                {
                    ImgPreviewImplantacion.Source = null;
                    TxtPlaceholderImplantacion.Visibility = Visibility.Visible;
                    TxtImplantacionPath.Text = string.Empty;
                }
            }
            else
            {
                ImgPreviewImplantacion.Source = null;
                TxtPlaceholderImplantacion.Visibility = Visibility.Visible;
                TxtImplantacionPath.Text = string.Empty;
            }

            // Localización
            if (!string.IsNullOrWhiteSpace(Model.LocalizacionImagePath) && File.Exists(Model.LocalizacionImagePath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(Model.LocalizacionImagePath);
                    bmp.EndInit();
                    ImgPreviewLocalizacion.Source = bmp;
                    TxtPlaceholderLocalizacion.Visibility = Visibility.Collapsed;
                    TxtLocalizacionPath.Text = Model.LocalizacionImagePath;
                }
                catch
                {
                    ImgPreviewLocalizacion.Source = null;
                    TxtPlaceholderLocalizacion.Visibility = Visibility.Visible;
                    TxtLocalizacionPath.Text = string.Empty;
                }
            }
            else
            {
                ImgPreviewLocalizacion.Source = null;
                TxtPlaceholderLocalizacion.Visibility = Visibility.Visible;
                TxtLocalizacionPath.Text = string.Empty;
            }
            UpdateLivePreview();
        }

        private void BtnBrowseImplantacion_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Seleccionar Imagen de Implantación";
                dlg.Filter = "Archivos de Imagen (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && File.Exists(dlg.FileName))
                {
                    Model.ImplantacionImagePath = dlg.FileName;
                    UpdateImagePreviews();
                }
            }
        }

        private void BtnClearImplantacion_Click(object sender, RoutedEventArgs e)
        {
            Model.ImplantacionImagePath = string.Empty;
            UpdateImagePreviews();
        }

        private void BtnBrowseLocalizacion_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Seleccionar Imagen de Localización";
                dlg.Filter = "Archivos de Imagen (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && File.Exists(dlg.FileName))
                {
                    Model.LocalizacionImagePath = dlg.FileName;
                    UpdateImagePreviews();
                }
            }
        }

        private void BtnClearLocalizacion_Click(object sender, RoutedEventArgs e)
        {
            Model.LocalizacionImagePath = string.Empty;
            UpdateImagePreviews();
        }

        private void BtnSaveDefaults_Click(object sender, RoutedEventArgs e)
        {
            BindUIToModel();
            Model.SaveDefaults(_drawingName);
            MessageBox.Show(
                "Los datos del proyecto, equipo, normativas y parámetros han sido guardados correctamente.\n\nSi cierra y vuelve a abrir AutoCAD, sus datos continuarán guardados para este dibujo y para nuevos proyectos.",
                "SGH - Datos Guardados Correctamente",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void BtnSaveConclusionsAsDefault_Click(object sender, RoutedEventArgs e)
        {
            BindUIToModel();
            Model.SaveDefaults(_drawingName);
            MessageBox.Show(
                "Las conclusiones actuales han sido guardadas como la plantilla predeterminada para todos los proyectos.",
                "SGH - Conclusiones Predeterminadas Guardadas",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void BtnResetConclusions_Click(object sender, RoutedEventArgs e)
        {
            TxtConclusiones.Text = SghReportModel.DefaultConclusionsText;
        }

        private void BtnClearConclusions_Click(object sender, RoutedEventArgs e)
        {
            TxtConclusiones.Text = string.Empty;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            BindUIToModel();

            // Ventana en el explorador de archivos para seleccionar la carpeta de destino
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Seleccione la carpeta donde desea guardar los entregables SGH (Excel y Word):";
                dlg.SelectedPath = _targetFolder;
                dlg.ShowNewFolderButton = true;

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    _targetFolder = dlg.SelectedPath;
                }
                else
                {
                    return; // El usuario canceló la selección de carpeta
                }
            }

            string baseName = "Proyecto";
            if (!string.IsNullOrWhiteSpace(Model.ProjectName) && Model.ProjectName != "Nombre del Proyecto")
            {
                baseName = string.Join("_", Model.ProjectName.Split(Path.GetInvalidFileNameChars()));
            }

            string timeStamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            Model.ExcelOutputPath = Path.Combine(_targetFolder, $"SGH-CargaDeOcupacion-{baseName}-{timeStamp}.xlsx");
            Model.WordOutputPath = Path.Combine(_targetFolder, $"SGH-INF-{baseName}-{timeStamp}.docx");
            Model.GenerateExcel = true;
            Model.GenerateWord = true;

            Confirmed = true;
            Close();
        }

        private void HookLivePreviewEvents()
        {
            TxtProjectName.TextChanged += (s, e) => UpdateLivePreview();
            TxtProjectDescription.TextChanged += (s, e) => UpdateLivePreview();
            TxtVersion.TextChanged += (s, e) => UpdateLivePreview();
            TxtReportDate.TextChanged += (s, e) => UpdateLivePreview();
            TxtArchBaseDate.TextChanged += (s, e) => UpdateLivePreview();

            TxtDirectorName.TextChanged += (s, e) => UpdateLivePreview();
            TxtDirectorCert.TextChanged += (s, e) => UpdateLivePreview();
            TxtCoordinatorName.TextChanged += (s, e) => UpdateLivePreview();
            TxtAuthorName.TextChanged += (s, e) => UpdateLivePreview();
            TxtCollaboratorName.TextChanged += (s, e) => UpdateLivePreview();

            TxtBuildingHeight.TextChanged += (s, e) => UpdateLivePreview();
            CmbBuildingClassification.SelectionChanged += (s, e) => UpdateLivePreview();
            CmbRiskCategory.SelectionChanged += (s, e) => UpdateLivePreview();
            ChkSprinklers.Checked += (s, e) => UpdateLivePreview();
            ChkSprinklers.Unchecked += (s, e) => UpdateLivePreview();

            TxtDiagonalEdificio.TextChanged += (s, e) => UpdateLivePreview();
            TxtSeparacionSalidas.TextChanged += (s, e) => UpdateLivePreview();
            CmbCumplimientoSeparacion.SelectionChanged += (s, e) => UpdateLivePreview();
            TxtDistanciaMax.TextChanged += (s, e) => UpdateLivePreview();
            TxtDistanciaExistente.TextChanged += (s, e) => UpdateLivePreview();
            CmbCumplimientoDistancia.SelectionChanged += (s, e) => UpdateLivePreview();

            TxtConclusiones.TextChanged += (s, e) => UpdateLivePreview();
        }

        private bool _isUpdatingLivePreview = false;

        public void UpdateLivePreview()
        {
            if (_isInitializing || _isUpdatingLivePreview) return;
            try
            {
                _isUpdatingLivePreview = true;
                BindUIToModel();
                SideWordPreview?.RefreshPreview(Model);
            }
            finally
            {
                _isUpdatingLivePreview = false;
            }
        }

        private void BtnToggleSidePreview_Click(object sender, RoutedEventArgs e)
        {
            bool isExpanded = BtnToggleSidePreview.IsChecked == true;
            if (isExpanded)
            {
                PnlSidePreview.Visibility = Visibility.Visible;
                PreviewSplitter.Visibility = Visibility.Visible;
                if (this.Width < 1520)
                {
                    this.Width = 1560;
                }
                UpdateLivePreview();
                SideWordPreview?.ScrollToSection(MainTabControl.SelectedIndex);
            }
            else
            {
                PnlSidePreview.Visibility = Visibility.Collapsed;
                PreviewSplitter.Visibility = Visibility.Collapsed;
                if (this.Width > 1150)
                {
                    this.Width = 1080;
                }
                UpdateLivePreview();
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (PnlSidePreview?.Visibility == Visibility.Visible)
            {
                UpdateLivePreview();
                SideWordPreview?.ScrollToSection(MainTabControl.SelectedIndex);
            }
        }
    }
}

