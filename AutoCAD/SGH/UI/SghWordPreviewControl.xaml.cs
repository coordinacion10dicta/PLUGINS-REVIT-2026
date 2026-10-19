using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoCAD.SGH.Models;

namespace AutoCAD.SGH.UI
{
    public partial class SghWordPreviewControl : UserControl
    {
        public SghWordPreviewControl()
        {
            InitializeComponent();
        }

        private void BtnJumpSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null && int.TryParse(btn.Tag.ToString(), out int tag))
            {
                switch (tag)
                {
                    case 1: Sheet1?.BringIntoView(); break;
                    case 2: Sheet2?.BringIntoView(); break;
                    case 3: Sheet3?.BringIntoView(); break;
                    case 4: Sheet4?.BringIntoView(); break;
                    case 5: Sheet5?.BringIntoView(); break;
                }
                HighlightNavButton(tag);
            }
        }

        public void ScrollToSection(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: // Portada
                    Sheet1?.BringIntoView();
                    HighlightNavButton(1);
                    break;
                case 1: // Parámetros del Edificio
                    Sheet3?.BringIntoView();
                    HighlightNavButton(3);
                    break;
                case 2: // Salidas y Evacuación
                    Sheet4?.BringIntoView();
                    HighlightNavButton(4);
                    break;
                case 3: // Conclusiones
                    Sheet5?.BringIntoView();
                    HighlightNavButton(5);
                    break;
                case 4: // Imágenes del Proyecto
                    Sheet2?.BringIntoView();
                    HighlightNavButton(2);
                    break;
            }
        }

        public void HighlightNavButton(int sheetNum)
        {
            SetNavButtonStyle(BtnNav1, sheetNum == 1);
            SetNavButtonStyle(BtnNav2, sheetNum == 2);
            SetNavButtonStyle(BtnNav3, sheetNum == 3);
            SetNavButtonStyle(BtnNav4, sheetNum == 4);
            SetNavButtonStyle(BtnNav5, sheetNum == 5);
        }

        private void SetNavButtonStyle(Button btn, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive 
                ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) 
                : new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44));
            btn.Foreground = isActive 
                ? Brushes.White 
                : new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            btn.BorderBrush = isActive 
                ? new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)) 
                : new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
        }

        public void RefreshPreview(SghReportModel model)
        {
            if (model == null) return;

            string versionText = $"Versión {model.Version ?? "01"}";
            if (TxtHeaderVersion1 != null) TxtHeaderVersion1.Text = versionText;
            if (TxtHeaderVersion2 != null) TxtHeaderVersion2.Text = versionText;
            if (TxtHeaderVersion3 != null) TxtHeaderVersion3.Text = versionText;
            if (TxtHeaderVersion4 != null) TxtHeaderVersion4.Text = versionText;
            if (TxtHeaderVersion5 != null) TxtHeaderVersion5.Text = versionText;

            // ========================================================
            // HOJA 1: PORTADA & APROBACIONES
            // ========================================================
            TxtPrevProjectName.Text = !string.IsNullOrWhiteSpace(model.ProjectName) ? model.ProjectName.ToUpperInvariant() : "NOMBRE DEL PROYECTO";
            TxtPrevProjectDesc.Text = !string.IsNullOrWhiteSpace(model.ProjectDescription) ? model.ProjectDescription : "Edificación destinada a diferentes usos y servicios bajo normativa NSR-10 y NFPA 101.";

            TxtPrevDirector.Text = model.DirectorName ?? string.Empty;
            TxtPrevCert.Text = model.DirectorCert ?? string.Empty;
            TxtPrevCoordinator.Text = model.CoordinatorName ?? string.Empty;
            TxtPrevAuthor.Text = model.AuthorName ?? string.Empty;
            TxtPrevCollaborator.Text = model.CollaboratorName ?? string.Empty;
            TxtPrevDate.Text = model.ReportDate ?? string.Empty;

            TxtPrevElabCargo.Text = model.ElaboroCargo ?? "Coordinadora Senior";
            TxtPrevElabNombre.Text = model.ElaboroNombre ?? "Alejandra Velandia";
            TxtPrevElabFecha.Text = model.ElaboroFecha ?? model.ReportDate ?? string.Empty;

            TxtPrevRevCargo.Text = model.RevisoCargo ?? "Responsable de SGC";
            TxtPrevRevNombre.Text = model.RevisoNombre ?? "Catherin Zamora";
            TxtPrevRevFecha.Text = model.RevisoFecha ?? model.ReportDate ?? string.Empty;

            TxtPrevAproCargo.Text = model.AproboCargo ?? "Responsable de SGC";
            TxtPrevAproNombre.Text = model.AproboNombre ?? "Catherin Zamora";
            TxtPrevAproFecha.Text = model.AproboFecha ?? model.ReportDate ?? string.Empty;

            // ========================================================
            // HOJA 2: INTRODUCCIÓN & IMAGEN 1
            // ========================================================
            RunIntroProjName.Text = !string.IsNullOrWhiteSpace(model.ProjectName) ? model.ProjectName : "PROYECTO";

            // Imagen 1 (Implantación)
            if (!string.IsNullOrWhiteSpace(model.ImplantacionImagePath) && File.Exists(model.ImplantacionImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(model.ImplantacionImagePath);
                    bmp.EndInit();
                    ImgPrevImplantacion.Source = bmp;
                    TxtPrevNoImg1.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ImgPrevImplantacion.Source = null;
                    TxtPrevNoImg1.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ImgPrevImplantacion.Source = null;
                TxtPrevNoImg1.Visibility = Visibility.Visible;
            }

            // ========================================================
            // HOJA 3: CLASIFICACIÓN, GRAN ALTURA & FUEGO
            // ========================================================
            string mainClass = !string.IsNullOrWhiteSpace(model.BuildingClassification) ? model.BuildingClassification : "Comercial (C)";
            string secClass = !string.IsNullOrWhiteSpace(model.SecondaryClassification) ? model.SecondaryClassification : "Almacenamiento (A)";
            TxtPrevClassifText.Text = $"De acuerdo con la clasificación de edificaciones estipulada en el capítulo K.2 se determina que el edificio se clasifica en {mainClass}, con espacios secundarios clasificados como {secClass}.";

            // Construir tabla visual de clasificación
            PnlPrevClassificationTable.Children.Clear();
            AddClassificationRow("Grupos y subgrupos de ocupación", "Clasificación", isHeader: true);
            AddClassificationRow("C", "COMERCIAL", isHeader: false, isGroup: true);
            AddClassificationRow("(C-1)", "Servicios comerciales", isHeader: false);
            if (!string.IsNullOrWhiteSpace(secClass))
            {
                AddClassificationRow("A", "ALMACENAMIENTO", isHeader: false, isGroup: true);
                AddClassificationRow("(A-1)", "Riesgo moderado", isHeader: false);
            }

            // Altura y Gran Altura
            bool isHigh = model.IsHighRise;
            string relation = isHigh ? "superior a 23m" : "inferior a 23m";
            string highText = isHigh ? "ES DE GRAN ALTURA" : "NO ES DE GRAN ALTURA";
            TxtPrevHeightParagraph.Text = $"El edificio tiene una altura de {model.BuildingHeight.ToString("0.0", CultureInfo.InvariantCulture)} m, siendo {relation} desde el nivel de calle al último nivel habitable, por lo que se cataloga como edificio que {highText} de acuerdo con NSR-10 K.3.1.3 y NFPA 101.";

            if (isHigh)
            {
                BadgePrevHighRise.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
                BadgePrevHighRise.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
                TxtPrevHighRiseBadge.Text = "⚠ Edificio de Gran Altura (> 23m)";
                TxtPrevHighRiseBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            }
            else
            {
                BadgePrevHighRise.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7));
                BadgePrevHighRise.BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
                TxtPrevHighRiseBadge.Text = "✓ Edificio de Altura Convencional (≤ 23m)";
                TxtPrevHighRiseBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
            }

            TxtPrevFireText.Text = $"La clasificación del proyecto corresponde a la {model.RiskCategory ?? "Categoría II"}. Según el reglamento NSR-10 J.3.4 el proyecto deberá contemplar una resistencia general de {model.GeneralFireResistance ?? "2 Horas"} para sus elementos estructurales principales.";

            // ========================================================
            // HOJA 4: SALIDAS Y EVACUACIÓN POR PISO
            // ========================================================
            PnlPrevFloorRows.Children.Clear();

            var floorList = model.FloorExits ?? new List<SghFloorExitItem>();
            foreach (var f in floorList)
            {
                var rowGrid = new Grid
                {
                    Background = f.IsTotal ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)) : Brushes.Transparent
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Piso
                var tbPiso = new TextBlock
                {
                    Text = f.Piso,
                    FontWeight = f.IsTotal ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 8.5,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                Grid.SetColumn(tbPiso, 0);
                rowGrid.Children.Add(tbPiso);

                // Ocupación
                var tbOcc = new TextBlock
                {
                    Text = f.Ocupacion.ToString(),
                    FontWeight = f.IsTotal ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 8.5,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                Grid.SetColumn(tbOcc, 1);
                rowGrid.Children.Add(tbOcc);

                // Salidas Req
                var tbReq = new TextBlock
                {
                    Text = f.SalidasRequeridas.ToString(),
                    FontWeight = f.IsTotal ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 8.5,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                Grid.SetColumn(tbReq, 2);
                rowGrid.Children.Add(tbReq);

                // Salidas Exist
                var tbExist = new TextBlock
                {
                    Text = f.SalidasExistentes.ToString(),
                    FontWeight = f.IsTotal ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 8.5,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                Grid.SetColumn(tbExist, 3);
                rowGrid.Children.Add(tbExist);

                // Evaluación
                bool cumple = (f.Evaluacion ?? "Cumple").IndexOf("Cumple", StringComparison.OrdinalIgnoreCase) >= 0 && (f.Evaluacion ?? "").IndexOf("No", StringComparison.OrdinalIgnoreCase) < 0;
                var badgeEval = new Border
                {
                    Background = cumple ? new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)) : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(6, 1, 6, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                badgeEval.Child = new TextBlock
                {
                    Text = cumple ? "Cumple" : "No Cumple",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = cumple ? new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)) : new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B))
                };
                Grid.SetColumn(badgeEval, 4);
                rowGrid.Children.Add(badgeEval);

                PnlPrevFloorRows.Children.Add(rowGrid);
            }

            // Separación
            double diag = model.DiagonalEdificioM;
            double reqSep = model.HasSprinklers ? (diag / 3.0) : (diag / 2.0);
            double actSep = model.SeparacionSalidasExistenteM;
            bool sepCumple = (model.CumplimientoSeparacionSalidas ?? "Cumple").IndexOf("No", StringComparison.OrdinalIgnoreCase) < 0;

            TxtPrevSeparationCalc.Text = $"Diagonal edificio: {diag:0.0}m. Separación mínima requerida: {reqSep:0.0}m {(model.HasSprinklers ? "(1/3 con rociadores)" : "(1/2 sin rociadores)")}. Existente: {actSep:0.0}m.";
            TxtPrevSeparationResult.Text = sepCumple ? "Cumple" : "No Cumple";
            BadgePrevSeparation.Background = sepCumple ? new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)) : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            TxtPrevSeparationResult.Foreground = sepCumple ? new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)) : new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            TxtPrevDistanceText.Text = $"Distancia máxima de recorrido permitida: {model.DistanciaRecorridoMaxPermitidaM:0.0}m. Distancia existente en el proyecto: {model.DistanciaRecorridoExistenteM:0.0}m. Evaluación: {model.CumplimientoDistanciaRecorrido ?? "Cumple"}.";

            // ========================================================
            // HOJA 5: CONCLUSIONES & IMAGEN 2
            // ========================================================
            PnlPrevConclusionsList.Children.Clear();
            string rawConc = !string.IsNullOrWhiteSpace(model.Conclusiones) ? model.Conclusiones : SghReportModel.DefaultConclusionsText;
            var lines = rawConc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                string tr = line.Trim();
                if (string.IsNullOrWhiteSpace(tr)) continue;
                if (tr.StartsWith("•") || tr.StartsWith("-") || tr.StartsWith("*"))
                {
                    tr = tr.Substring(1).Trim();
                }

                var bulletGrid = new Grid { Margin = new Thickness(0, 2, 0, 3) };
                bulletGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                bulletGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "•",
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x36, 0x5D)),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(dot, 0);
                bulletGrid.Children.Add(dot);

                var tb = new TextBlock
                {
                    Text = tr,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 13.5
                };
                Grid.SetColumn(tb, 1);
                bulletGrid.Children.Add(tb);

                PnlPrevConclusionsList.Children.Add(bulletGrid);
            }

            // Imagen 2 (Localización)
            if (!string.IsNullOrWhiteSpace(model.LocalizacionImagePath) && File.Exists(model.LocalizacionImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(model.LocalizacionImagePath);
                    bmp.EndInit();
                    ImgPrevLocalizacion.Source = bmp;
                    TxtPrevNoImg2.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ImgPrevLocalizacion.Source = null;
                    TxtPrevNoImg2.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ImgPrevLocalizacion.Source = null;
                TxtPrevNoImg2.Visibility = Visibility.Visible;
            }
        }

        private void AddClassificationRow(string col1, string col2, bool isHeader = false, bool isGroup = false)
        {
            var grid = new Grid
            {
                Background = isHeader || isGroup 
                    ? new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)) 
                    : Brushes.Transparent
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tb1 = new TextBlock
            {
                Text = col1,
                FontWeight = isHeader || isGroup ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 8.5,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2)
            };
            Grid.SetColumn(tb1, 0);
            grid.Children.Add(tb1);

            var tb2 = new TextBlock
            {
                Text = col2,
                FontWeight = isHeader || isGroup ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 8.5,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2)
            };
            Grid.SetColumn(tb2, 1);
            grid.Children.Add(tb2);

            PnlPrevClassificationTable.Children.Add(grid);
        }
    }
}
