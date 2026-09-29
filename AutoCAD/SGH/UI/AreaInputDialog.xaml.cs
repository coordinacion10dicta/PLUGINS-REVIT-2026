using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.SGH.Models;
using AutoCAD.SGH.Services;
using AutoCAD.SGH.Commands;

namespace AutoCAD.SGH.UI
{
    public partial class AreaInputDialog : Window
    {
        public SghSpace Space { get; private set; }
        public ObjectId ExistingBlockRefId { get; private set; } = ObjectId.Null;
        public bool IsEditMode => !ExistingBlockRefId.IsNull;

        private readonly double _areaCalculada;
        private readonly Point3d _puntoInsercion;
        private readonly double _escala;
        private bool _isInitializing = true;

        public AreaInputDialog(SghSpace space, ObjectId existingBlockRefId = default)
        {
            InitializeComponent();
            Space = space ?? new SghSpace();
            ExistingBlockRefId = existingBlockRefId;
            _areaCalculada = Space.Area;
            _puntoInsercion = Space.InsertionPoint;
            _escala = Space.DrawingScale;

            // Cargar listas de Grupos de Ocupación (NSR y NFPA)
            CboGrupoOcupacionNsr.ItemsSource = OccupancyService.NormsNSR10.Groups;
            var defaultNsr = OccupancyService.NormsNSR10.Groups.FirstOrDefault(g => g.Code.Equals(Space.GrupoOcupacion, StringComparison.OrdinalIgnoreCase))
                           ?? OccupancyService.NormsNSR10.Groups.FirstOrDefault(g => g.DisplayText.StartsWith(Space.GrupoOcupacion + " ", StringComparison.OrdinalIgnoreCase))
                           ?? OccupancyService.NormsNSR10.Groups.FirstOrDefault(g => g.Name.Equals(Space.GrupoOcupacion, StringComparison.OrdinalIgnoreCase))
                           ?? OccupancyService.NormsNSR10.Groups.First();
            CboGrupoOcupacionNsr.SelectedItem = defaultNsr;

            CboGrupoOcupacionNfpa.ItemsSource = OccupancyService.NormsNFPA.Groups;
            var defaultNfpa = !string.IsNullOrWhiteSpace(Space.GrupoOcupacionNfpa)
                ? (OccupancyService.NormsNFPA.Groups.FirstOrDefault(g => g.DisplayText.Equals(Space.GrupoOcupacionNfpa, StringComparison.OrdinalIgnoreCase))
                   ?? OccupancyService.NormsNFPA.Groups.FirstOrDefault(g => g.Name.Equals(Space.GrupoOcupacionNfpa, StringComparison.OrdinalIgnoreCase))
                   ?? OccupancyService.NormsNFPA.Groups.FirstOrDefault(g => g.Code.Equals(Space.GrupoOcupacionNfpa, StringComparison.OrdinalIgnoreCase))
                   ?? OccupancyService.NormsNFPA.Groups.FirstOrDefault(g => Space.GrupoOcupacionNfpa.IndexOf(g.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? OccupancyService.NormsNFPA.Groups.FirstOrDefault(g => g.DisplayText.StartsWith(Space.GrupoOcupacionNfpa + " ", StringComparison.OrdinalIgnoreCase)))
                : null;

            if (defaultNfpa == null)
            {
                defaultNfpa = OccupancyService.FindNfpaGroupForNsr(defaultNsr)
                            ?? OccupancyService.NormsNFPA.Groups.First();
            }
            CboGrupoOcupacionNfpa.SelectedItem = defaultNfpa;

            TxtNumero.Text = Space.Numero;
            TxtArea.Text = Space.Area.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            if (TxtCoNsr != null) TxtCoNsr.Text = string.IsNullOrWhiteSpace(Space.CargaOcupacionNsr) ? (string.IsNullOrWhiteSpace(Space.CargaOcupacion) ? "" : Space.CargaOcupacion) : Space.CargaOcupacionNsr;
            if (TxtCoNfpa != null) TxtCoNfpa.Text = !string.IsNullOrWhiteSpace(Space.CargaOcupacionNfpa) ? Space.CargaOcupacionNfpa : (string.IsNullOrWhiteSpace(Space.CargaOcupacion) ? "" : Space.CargaOcupacion);
            TxtEspacio.Text = string.IsNullOrWhiteSpace(Space.Espacio) ? "" : Space.Espacio;
            TxtPiso.Text = string.IsNullOrWhiteSpace(Space.Piso) ? "" : Space.Piso;

            bool isSavedNfpa = !string.IsNullOrWhiteSpace(Space.GrupoOcupacion) &&
                (OccupancyService.NormsNFPA.Groups.Any(g => g.Code.Equals(Space.GrupoOcupacion, StringComparison.OrdinalIgnoreCase)) ||
                 (defaultNfpa != null && (Space.GrupoOcupacion.Equals(defaultNfpa.Code, StringComparison.OrdinalIgnoreCase) || Space.GrupoOcupacion.Equals(defaultNfpa.Name, StringComparison.OrdinalIgnoreCase))));

            if (isSavedNfpa && RbNormaNfpa != null)
            {
                RbNormaNfpa.IsChecked = true;
            }
            else if (RbNormaNsr != null)
            {
                RbNormaNsr.IsChecked = true;
            }

            if (IsEditMode)
            {
                Title = "SGH - Editar Bloque de Área";
                LblHeader.Text = "Editar Parámetros de Área";
                BtnAccion.Content = "Actualizar Bloque";
                _isManualOverrideNsr = true;
                _isManualOverrideNfpa = true;
            }
            else
            {
                Title = "SGH - Asignación de Área y Ocupación";
                LblHeader.Text = "Parámetros de Espacio / Área";
                BtnAccion.Content = "Insertar Bloque";
            }

            _isInitializing = false;
            if (!IsEditMode)
            {
                RecalculateCO();
            }
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private string GetActiveCo()
        {
            bool isNfpa = RbNormaNfpa?.IsChecked == true;
            TextBox activeBox = isNfpa ? TxtCoNfpa : TxtCoNsr;
            return string.IsNullOrWhiteSpace(activeBox?.Text) ? "1" : activeBox.Text.Trim();
        }

        private string GetActiveGroupShortCode()
        {
            bool isNfpa = RbNormaNfpa?.IsChecked == true;
            if (isNfpa)
            {
                OccupancyGroupInfo selNfpa = CboGrupoOcupacionNfpa?.SelectedItem as OccupancyGroupInfo;
                if (selNfpa != null) return OccupancyService.ExtractShortCode(selNfpa.DisplayText);
                return OccupancyService.ExtractShortCode(CboGrupoOcupacionNfpa?.Text ?? "Mercantil");
            }
            else
            {
                OccupancyGroupInfo selNsr = CboGrupoOcupacionNsr?.SelectedItem as OccupancyGroupInfo;
                if (selNsr != null) return selNsr.Code;
                return OccupancyService.ExtractShortCode(CboGrupoOcupacionNsr?.Text ?? "A");
            }
        }

        private bool _isManualOverrideNsr = false;
        private bool _isManualOverrideNfpa = false;

        private void RbNorma_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (CboGrupoOcupacionNsr != null)
                CboGrupoOcupacionNsr.IsEnabled = true;

            if (CboGrupoOcupacionNfpa != null)
                CboGrupoOcupacionNfpa.IsEnabled = true;

            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void RecalculateCO()
        {
            if (_isInitializing) return;

            double area = GetCurrentArea();

            OccupancyGroupInfo selNsr = CboGrupoOcupacionNsr?.SelectedItem as OccupancyGroupInfo;
            OccupancyGroupInfo selNfpa = CboGrupoOcupacionNfpa?.SelectedItem as OccupancyGroupInfo;

            string groupTextNsr = selNsr != null ? selNsr.DisplayText : (CboGrupoOcupacionNsr?.Text ?? "");
            string groupTextNfpa = selNfpa != null ? selNfpa.DisplayText : (CboGrupoOcupacionNfpa?.Text ?? "");

            int? coNsr = (selNsr != null && selNsr.FactorM2PerPerson.HasValue && selNsr.FactorM2PerPerson.Value > 0)
                ? Math.Max(1, (int)Math.Ceiling(area / selNsr.FactorM2PerPerson.Value))
                : OccupancyService.CalculateCO_NSR10(area, groupTextNsr);

            int? coNfpa = (selNfpa != null && selNfpa.FactorM2PerPerson.HasValue && selNfpa.FactorM2PerPerson.Value > 0)
                ? Math.Max(1, (int)Math.Ceiling(area / selNfpa.FactorM2PerPerson.Value))
                : OccupancyService.CalculateCO_NFPA(area, groupTextNfpa);

            if (TxtCoNsr != null && !_isManualOverrideNsr)
            {
                TxtCoNsr.Text = coNsr.HasValue ? coNsr.Value.ToString() : "1";
            }

            if (TxtCoNfpa != null && !_isManualOverrideNfpa)
            {
                TxtCoNfpa.Text = coNfpa.HasValue ? coNfpa.Value.ToString() : "1";
            }
        }

        private void RecalculateEgressWidths()
        {
            if (TxtAnchoPasillos == null || TxtAnchoEscaleras == null)
                return;

            string shortCode = GetActiveGroupShortCode();

            bool esManual = shortCode.Equals("E", StringComparison.OrdinalIgnoreCase) ||
                            shortCode.Equals("M", StringComparison.OrdinalIgnoreCase) ||
                            shortCode.Equals("T", StringComparison.OrdinalIgnoreCase);

            if (esManual)
            {
                if (TxtAnchoPasillos.Text == "Según ocupación" ||
                    TxtAnchoPasillos.Text == "—")
                {
                    TxtAnchoPasillos.Text = "";
                }

                if (TxtAnchoEscaleras.Text == "Según ocupación" ||
                    TxtAnchoEscaleras.Text == "—")
                {
                    TxtAnchoEscaleras.Text = "";
                }

                return;
            }

            if (int.TryParse(GetActiveCo(), out int co))
            {
                var (anchoPasillos, anchoEscaleras) =
                    OccupancyService.CalculateEgressWidths(co, shortCode);

                if (string.IsNullOrWhiteSpace(TxtAnchoPasillos.Text) || TxtAnchoPasillos.Text == "—" || TxtAnchoPasillos.Text == "Según ocupación" || TxtAnchoPasillos.Text.EndsWith(" mm"))
                {
                    TxtAnchoPasillos.Text = anchoPasillos.HasValue
                        ? $"{anchoPasillos.Value.ToString("0", CultureInfo.InvariantCulture)} mm"
                        : "Según ocupación";
                }

                if (string.IsNullOrWhiteSpace(TxtAnchoEscaleras.Text) || TxtAnchoEscaleras.Text == "—" || TxtAnchoEscaleras.Text == "Según ocupación" || TxtAnchoEscaleras.Text.EndsWith(" mm"))
                {
                    TxtAnchoEscaleras.Text = anchoEscaleras.HasValue
                        ? $"{anchoEscaleras.Value.ToString("0", CultureInfo.InvariantCulture)} mm"
                        : "Según ocupación";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TxtAnchoPasillos.Text)) TxtAnchoPasillos.Text = "—";
                if (string.IsNullOrWhiteSpace(TxtAnchoEscaleras.Text)) TxtAnchoEscaleras.Text = "—";
            }
        }

        private double GetCurrentArea()
        {
            if (double.TryParse(TxtArea?.Text?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedArea))
            {
                return parsedArea;
            }
            return _areaCalculada;
        }

        private void TxtArea_TextChanged(object sender, TextChangedEventArgs e)
        {
            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void CboGrupoOcupacionNsr_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _isManualOverrideNsr = false;
                _isManualOverrideNfpa = false;
                if (RbNormaNsr != null && RbNormaNsr.IsChecked != true)
                {
                    RbNormaNsr.IsChecked = true;
                }
                if (!IsEditMode && CboGrupoOcupacionNsr.SelectedItem is OccupancyGroupInfo selectedNsr)
                {
                    var matchingNfpa = OccupancyService.FindNfpaGroupForNsr(selectedNsr);
                    if (matchingNfpa != null)
                    {
                        CboGrupoOcupacionNfpa.SelectedItem = matchingNfpa;
                    }
                }
            }
            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void CboGrupoOcupacionNsr_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _isManualOverrideNsr = false;
                if (RbNormaNsr != null && RbNormaNsr.IsChecked != true)
                {
                    RbNormaNsr.IsChecked = true;
                }
            }
            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void CboGrupoOcupacionNfpa_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _isManualOverrideNfpa = false;
                if (RbNormaNfpa != null && RbNormaNfpa.IsChecked != true)
                {
                    RbNormaNfpa.IsChecked = true;
                }
            }
            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void CboGrupoOcupacionNfpa_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _isManualOverrideNfpa = false;
                if (RbNormaNfpa != null && RbNormaNfpa.IsChecked != true)
                {
                    RbNormaNfpa.IsChecked = true;
                }
            }
            RecalculateCO();
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            if (!_isInitializing)
            {
                if (sender == TxtCoNsr)
                {
                    _isManualOverrideNsr = true;
                    if (RbNormaNsr != null && RbNormaNsr.IsChecked != true)
                    {
                        RbNormaNsr.IsChecked = true;
                    }
                }
                if (sender == TxtCoNfpa)
                {
                    _isManualOverrideNfpa = true;
                    if (RbNormaNfpa != null && RbNormaNfpa.IsChecked != true)
                    {
                        RbNormaNfpa.IsChecked = true;
                    }
                }
            }
            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void TxtPiso_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || IsEditMode)
            {
                UpdatePreview();
                RecalculateEgressWidths();
                return;
            }

            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    string nuevoPiso = TxtPiso?.Text?.Trim() ?? "";
                    int nextNum = SpaceService.GetNextSpaceNumber(doc.Database, nuevoPiso);
                    TxtNumero.Text = nextNum.ToString();
                }
            }
            catch
            {
            }

            UpdatePreview();
            RecalculateEgressWidths();
        }

        private void UpdatePreview()
        {
            if (LblPreviewNum == null || LblPreviewArea == null || LblPreviewUso == null || LblPreviewCo == null)
                return;

            string num = string.IsNullOrWhiteSpace(TxtNumero?.Text) ? "1" : TxtNumero.Text.Trim();
            string areaText = string.IsNullOrWhiteSpace(TxtArea?.Text) ? "0,00" : TxtArea.Text.Trim().Replace('.', ',');

            string shortCode = GetActiveGroupShortCode();
            string co = GetActiveCo();

            LblPreviewNum.Text = num;
            LblPreviewArea.Text = $"A: {areaText}m2";
            LblPreviewUso.Text = $"U:  ({shortCode})";
            LblPreviewCo.Text = $"CO:  {co}";
        }

        private void BtnAccion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("No se encontró ningún documento activo en AutoCAD.", "Error SGH", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var db = doc.Database;
                double areaFinal = GetCurrentArea();

                OccupancyGroupInfo selNsrGroup = CboGrupoOcupacionNsr?.SelectedItem as OccupancyGroupInfo;
                OccupancyGroupInfo selNfpaGroup = CboGrupoOcupacionNfpa?.SelectedItem as OccupancyGroupInfo;

                string shortCodeNsr = selNsrGroup != null ? selNsrGroup.Code : OccupancyService.ExtractShortCode(CboGrupoOcupacionNsr?.Text ?? "A");
                string groupNsrVal = selNsrGroup != null ? selNsrGroup.DisplayText : (CboGrupoOcupacionNsr?.Text ?? "A");
                string groupNfpaVal = selNfpaGroup != null ? selNfpaGroup.DisplayText : (CboGrupoOcupacionNfpa?.Text ?? "Mercantil");
                string shortCodeNfpa = selNfpaGroup != null ? OccupancyService.ExtractShortCode(selNfpaGroup.DisplayText) : OccupancyService.ExtractShortCode(CboGrupoOcupacionNfpa?.Text ?? "Mercantil");

                string coNsrStr = string.IsNullOrWhiteSpace(TxtCoNsr?.Text) ? "1" : TxtCoNsr.Text.Trim();
                string coNfpaStr = string.IsNullOrWhiteSpace(TxtCoNfpa?.Text) ? coNsrStr : TxtCoNfpa.Text.Trim();

                bool isNfpaSelected = RbNormaNfpa?.IsChecked == true;
                string activeUsoCode = isNfpaSelected ? shortCodeNfpa : shortCodeNsr;
                string activeCoStr = isNfpaSelected ? coNfpaStr : coNsrStr;

                double? anchoPasillosFinal = null;
                double? anchoEscalerasFinal = null;

                bool esManual =
                    activeUsoCode.Equals("E", StringComparison.OrdinalIgnoreCase) ||
                    activeUsoCode.Equals("M", StringComparison.OrdinalIgnoreCase) ||
                    activeUsoCode.Equals("T", StringComparison.OrdinalIgnoreCase);

                if (esManual)
                {
                    if (double.TryParse(
                        TxtAnchoPasillos.Text
                            .Replace("mm", "")
                            .Trim()
                            .Replace(',', '.'),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double pasillosManual))
                    {
                        anchoPasillosFinal = pasillosManual;
                    }

                    if (double.TryParse(
                        TxtAnchoEscaleras.Text
                            .Replace("mm", "")
                            .Trim()
                            .Replace(',', '.'),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double escalerasManual))
                    {
                        anchoEscalerasFinal = escalerasManual;
                    }
                }
                else
                {
                    var resultado = OccupancyService.CalculateEgressWidths(
                        int.TryParse(activeCoStr, out int coFinal) ? coFinal : 1,
                        activeUsoCode);

                    anchoPasillosFinal = resultado.AnchoCorredoresMm;
                    anchoEscalerasFinal = resultado.AnchoEscalerasMm;
                }

                var space = new SghSpace
                {
                    Numero = string.IsNullOrWhiteSpace(TxtNumero.Text) ? "1" : TxtNumero.Text.Trim(),
                    Area = areaFinal,
                    GrupoOcupacion = activeUsoCode,
                    GrupoOcupacionNsr = groupNsrVal,
                    GrupoOcupacionNfpa = groupNfpaVal,
                    CargaOcupacion = activeCoStr,
                    CargaOcupacionNsr = coNsrStr,
                    CargaOcupacionNfpa = coNfpaStr,
                    Espacio = string.IsNullOrWhiteSpace(TxtEspacio.Text) ? "" : TxtEspacio.Text.Trim(),
                    Piso = string.IsNullOrWhiteSpace(TxtPiso.Text) ? "1" : TxtPiso.Text.Trim(),
                    AnchoPasillosMm = anchoPasillosFinal,
                    AnchoEscalerasMm = anchoEscalerasFinal,
                    InsertionPoint = _puntoInsercion,
                    DrawingScale = _escala,
                    PolylineId = Space.PolylineId
                };

                using (doc.LockDocument())
                {
                    AreaService.EnsureLayersExist(db);

                    if (IsEditMode)
                    {
                        SpaceService.UpdateBlockReferenceAttributes(db, ExistingBlockRefId, space);
                        doc.Editor.WriteMessage($"\n[SGH] Bloque #{space.Numero} actualizado exitosamente ({space.FormattedArea}, {space.FormattedUso}, {space.FormattedCo}).\n");
                    }
                    else
                    {
                        var blockId = SpaceService.InsertSpaceTag(db, space);
                        doc.Editor.WriteMessage($"\n[SGH] Bloque #{space.Numero} insertado exitosamente ({space.FormattedArea}, {space.FormattedUso}, {space.FormattedCo}).\n");
                    }

                    try
                    {
                        doc.Editor.Regen();
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(space.Piso))
                {
                    AreasCommand.LastUsedPiso = space.Piso;
                }

                this.Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error al guardar el bloque:\n{ex.Message}\n\nDetalle:\n{ex.StackTrace}", "Error SGH", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
