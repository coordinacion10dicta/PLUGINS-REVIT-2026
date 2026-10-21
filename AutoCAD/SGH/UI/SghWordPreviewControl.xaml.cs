using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoCAD.SGH.Models;

namespace AutoCAD.SGH.UI
{
    public partial class SghWordPreviewControl : UserControl
    {
        private double _currentZoom = 1.0;
        private BitmapImage _cachedLogo;
        private BitmapImage _cachedFooterBar;

        public SghWordPreviewControl()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadInstitutionalAssets();
        }

        private void LoadInstitutionalAssets()
        {
            try
            {
                if (_cachedLogo == null)
                {
                    _cachedLogo = LoadBitmapAsset("dicta_logo.png");
                }

                if (_cachedLogo != null)
                {
                    if (ImgLogoHeader1 != null) ImgLogoHeader1.Source = _cachedLogo;
                    if (ImgLogoHeader2 != null) ImgLogoHeader2.Source = _cachedLogo;
                    if (ImgLogoHeader3 != null) ImgLogoHeader3.Source = _cachedLogo;
                    if (ImgLogoHeader4 != null) ImgLogoHeader4.Source = _cachedLogo;
                    if (ImgLogoHeader5 != null) ImgLogoHeader5.Source = _cachedLogo;
                }
                else
                {
                    // Fallback a texto si no se encuentra el logo
                    if (TxtFallbackLogo1 != null) TxtFallbackLogo1.Visibility = Visibility.Visible;
                    if (TxtFallbackLogo2 != null) TxtFallbackLogo2.Visibility = Visibility.Visible;
                    if (TxtFallbackLogo3 != null) TxtFallbackLogo3.Visibility = Visibility.Visible;
                    if (TxtFallbackLogo4 != null) TxtFallbackLogo4.Visibility = Visibility.Visible;
                    if (TxtFallbackLogo5 != null) TxtFallbackLogo5.Visibility = Visibility.Visible;
                }

                if (_cachedFooterBar == null)
                {
                    _cachedFooterBar = LoadBitmapAsset("dicta_footer_bar.png");
                }

                if (_cachedFooterBar != null)
                {
                    if (ImgFooterBar1 != null) ImgFooterBar1.Source = _cachedFooterBar;
                    if (ImgFooterBar2 != null) ImgFooterBar2.Source = _cachedFooterBar;
                    if (ImgFooterBar3 != null) ImgFooterBar3.Source = _cachedFooterBar;
                    if (ImgFooterBar4 != null) ImgFooterBar4.Source = _cachedFooterBar;
                    if (ImgFooterBar5 != null) ImgFooterBar5.Source = _cachedFooterBar;
                }
            }
            catch { }
        }

        private BitmapImage LoadBitmapAsset(string fileName)
        {
            try
            {
                string dllDir = string.Empty;
                try
                {
                    dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                }
                catch { }

                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(dllDir))
                {
                    candidates.Add(Path.Combine(dllDir, "Images", fileName));
                    candidates.Add(Path.Combine(dllDir, "Resources", fileName));
                    candidates.Add(Path.Combine(dllDir, fileName));
                    candidates.Add(Path.Combine(dllDir, @"..\..\Images", fileName));
                    candidates.Add(Path.Combine(dllDir, @"..\..\Resources", fileName));
                }

                candidates.Add(Path.Combine(@"C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\AutoCAD\Images", fileName));
                candidates.Add(Path.Combine(@"C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\AutoCAD\Resources", fileName));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents\Images", fileName));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents\Resources", fileName));

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path);
                        bmp.EndInit();
                        return bmp;
                    }
                }
            }
            catch { }
            return null;
        }

        #region Navegación entre Hojas

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
                ? new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)) 
                : Brushes.White;
            btn.Foreground = isActive 
                ? Brushes.White 
                : new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
            btn.BorderBrush = isActive 
                ? new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)) 
                : new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
        }

        #endregion

        #region Controles de Zoom

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(_currentZoom + 0.1);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(_currentZoom - 0.1);
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(1.0);
        }

        private void DocScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                double delta = e.Delta > 0 ? 0.08 : -0.08;
                ApplyZoom(_currentZoom + delta);
            }
        }

        private void ApplyZoom(double newZoom)
        {
            _currentZoom = Math.Max(0.5, Math.Min(2.0, newZoom));
            if (DocScaleTransform != null)
            {
                DocScaleTransform.ScaleX = _currentZoom;
                DocScaleTransform.ScaleY = _currentZoom;
            }
            if (TxtZoomLevel != null)
            {
                TxtZoomLevel.Text = $"{Math.Round(_currentZoom * 100)}%";
            }
        }

        #endregion

        public void RefreshPreview(SghReportModel model)
        {
            if (model == null) return;

            LoadInstitutionalAssets();

            string versionText = $"Versión: {model.Version ?? "01"}";
            string dateText = $"Fecha: {model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd")}";

            if (TxtHeaderVersion1 != null) TxtHeaderVersion1.Text = versionText;
            if (TxtHeaderVersion2 != null) TxtHeaderVersion2.Text = versionText;
            if (TxtHeaderVersion3 != null) TxtHeaderVersion3.Text = versionText;
            if (TxtHeaderVersion4 != null) TxtHeaderVersion4.Text = versionText;
            if (TxtHeaderVersion5 != null) TxtHeaderVersion5.Text = versionText;

            if (TxtHeaderDate1 != null) TxtHeaderDate1.Text = dateText;
            if (TxtHeaderDate2 != null) TxtHeaderDate2.Text = dateText;
            if (TxtHeaderDate3 != null) TxtHeaderDate3.Text = dateText;
            if (TxtHeaderDate4 != null) TxtHeaderDate4.Text = dateText;
            if (TxtHeaderDate5 != null) TxtHeaderDate5.Text = dateText;

            // ========================================================
            // HOJA 1: PORTADA & APROBACIONES
            // ========================================================
            TxtPrevProjectName.Text = !string.IsNullOrWhiteSpace(model.ProjectName) ? model.ProjectName.ToUpperInvariant() : "NOMBRE DE PROYECTO";
            TxtPrevProjectDesc.Text = !string.IsNullOrWhiteSpace(model.ProjectDescription) ? model.ProjectDescription : "Edificación destinada a diferentes usos y servicios bajo normativa NSR-10 y NFPA 101.";

            TxtPrevDirector.Text = model.DirectorName ?? "Ing. Pedro Diaz";
            TxtPrevCert.Text = model.DirectorCert ?? "Certificado NFPA 1 & 101 & 72";
            TxtPrevCoordinator.Text = model.CoordinatorName ?? string.Empty;
            TxtPrevAuthor.Text = model.AuthorName ?? string.Empty;
            TxtPrevCollaborator.Text = model.CollaboratorName ?? string.Empty;
            TxtPrevDate.Text = model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd");

            TxtPrevElabCargo.Text = model.ElaboroCargo ?? "Coordinadora Senior";
            TxtPrevElabNombre.Text = model.ElaboroNombre ?? "Alejandra Velandia";
            TxtPrevElabFecha.Text = model.ElaboroFecha ?? model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd");

            TxtPrevRevCargo.Text = model.RevisoCargo ?? "Responsable de SGC";
            TxtPrevRevNombre.Text = model.RevisoNombre ?? "Catherin Zamora";
            TxtPrevRevFecha.Text = model.RevisoFecha ?? model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd");

            TxtPrevAproCargo.Text = model.AproboCargo ?? "Responsable de SGC";
            TxtPrevAproNombre.Text = model.AproboNombre ?? "Catherin Zamora";
            TxtPrevAproFecha.Text = model.AproboFecha ?? model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd");

            if (TxtHistVersion != null) TxtHistVersion.Text = model.Version ?? "01";
            if (TxtHistDate != null) TxtHistDate.Text = model.ReportDate ?? DateTime.Now.ToString("yyyy/MM/dd");
            if (TxtHistApproved != null) TxtHistApproved.Text = model.AproboNombre ?? "Catherin Zamora";

            // ========================================================
            // HOJA 2: INTRODUCCIÓN & IMAGEN 1
            // ========================================================
            RunIntroProjName.Text = !string.IsNullOrWhiteSpace(model.ProjectName) ? model.ProjectName : "PROYECTO";

            // Imagen 1 (Implantación Inicial)
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

            // Construir tabla visual de clasificación (Diseño oficial Word con bordes negros y encabezado #D9D9D9)
            PnlPrevClassificationTable.Children.Clear();
            AddClassificationRow("Grupos y subgrupos de ocupación", "Clasificación", isHeader: true);
            AddClassificationRow("C", "COMERCIAL", isHeader: false, isGroup: true);
            AddClassificationRow("(C-1)", "Servicios comerciales", isHeader: false);
            if (!string.IsNullOrWhiteSpace(secClass))
            {
                AddClassificationRow("A", "ALMACENAMIENTO", isHeader: false, isGroup: true);
                AddClassificationRow("(A-1)", "Riesgo moderado", isHeader: false);
            }

            // Altura y Gran Altura (Formato de párrafo oficial continuo de Word)
            bool isHigh = model.IsHighRise;
            double threshold = model.HighRiseThreshold;
            string relation = isHigh ? $"superior o igual a {threshold:0}m" : $"inferior a {threshold:0}m";
            string highText = isHigh ? "ES DE GRAN ALTURA" : "NO ES DE GRAN ALTURA";
            string normCitation = string.Equals(model.SelectedNorm, "NFPA", StringComparison.OrdinalIgnoreCase) ? "NFPA 101" : "NSR-10 K.3.1.3";
            
            TxtPrevHeightParagraph.Inlines.Clear();
            TxtPrevHeightParagraph.Inlines.Add("El edificio tiene una altura de ");
            TxtPrevHeightParagraph.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{model.BuildingHeight.ToString("0.0", CultureInfo.InvariantCulture)} m")));
            TxtPrevHeightParagraph.Inlines.Add(", siendo ");
            TxtPrevHeightParagraph.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(relation)));
            TxtPrevHeightParagraph.Inlines.Add(" desde el nivel de calle al último nivel habitable, por lo que se cataloga como edificio que ");
            TxtPrevHeightParagraph.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(highText)));
            TxtPrevHeightParagraph.Inlines.Add(" de acuerdo con ");
            TxtPrevHeightParagraph.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(normCitation)));
            TxtPrevHeightParagraph.Inlines.Add(".");

            TxtPrevFireText.Text = $"La clasificación del proyecto corresponde a la {model.RiskCategory ?? "Categoría II"}. Según el reglamento NSR-10 J.3.4 el proyecto deberá contemplar una resistencia general de {model.GeneralFireResistance ?? "2 Horas"} para sus elementos estructurales principales.";

            // ========================================================
            // HOJA 4: SALIDAS Y EVACUACIÓN POR PISO
            // ========================================================
            PnlPrevFloorRows.Children.Clear();

            var floorList = model.FloorExits ?? new List<SghFloorExitItem>();
            foreach (var f in floorList)
            {
                var rowBorder = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = f.IsTotal ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)) : Brushes.Transparent
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
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

                // Evaluación (En Word es texto negrita limpio sin píldoras web)
                bool cumple = (f.Evaluacion ?? "Cumple").IndexOf("Cumple", StringComparison.OrdinalIgnoreCase) >= 0 && (f.Evaluacion ?? "").IndexOf("No", StringComparison.OrdinalIgnoreCase) < 0;
                var tbEval = new TextBlock
                {
                    Text = cumple ? "Cumple" : "No Cumple",
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                Grid.SetColumn(tbEval, 4);
                rowGrid.Children.Add(tbEval);

                rowBorder.Child = rowGrid;
                PnlPrevFloorRows.Children.Add(rowBorder);
            }

            // Separación entre salidas (Párrafo formal Word)
            double diag = model.DiagonalEdificioM;
            double reqSep = model.HasSprinklers ? (diag / 3.0) : (diag / 2.0);
            double actSep = model.SeparacionSalidasExistenteM;
            bool sepCumple = (model.CumplimientoSeparacionSalidas ?? "Cumple").IndexOf("No", StringComparison.OrdinalIgnoreCase) < 0;
            string sepEvalText = sepCumple ? "CUMPLE" : "NO CUMPLE";

            TxtPrevSeparationCalc.Inlines.Clear();
            TxtPrevSeparationCalc.Inlines.Add("La diagonal máxima del edificio es de ");
            TxtPrevSeparationCalc.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{diag:0.0}m")));
            TxtPrevSeparationCalc.Inlines.Add($". La separación mínima requerida entre salidas {(model.HasSprinklers ? "(1/3 con rociadores)" : "(1/2 sin rociadores)")} es de ");
            TxtPrevSeparationCalc.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{reqSep:0.0}m")));
            TxtPrevSeparationCalc.Inlines.Add(". La distancia existente entre salidas en el proyecto es de ");
            TxtPrevSeparationCalc.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{actSep:0.0}m")));
            TxtPrevSeparationCalc.Inlines.Add(", por lo que ");
            TxtPrevSeparationCalc.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(sepEvalText)));
            TxtPrevSeparationCalc.Inlines.Add(" con el requerimiento normativo.");

            // Distancia máxima de recorrido
            string distEval = model.CumplimientoDistanciaRecorrido ?? "Cumple";
            TxtPrevDistanceText.Inlines.Clear();
            TxtPrevDistanceText.Inlines.Add("La distancia máxima de recorrido permitida hasta una salida es de ");
            TxtPrevDistanceText.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{model.DistanciaRecorridoMaxPermitidaM:0.0}m")));
            TxtPrevDistanceText.Inlines.Add(". La distancia máxima existente calculada en el proyecto es de ");
            TxtPrevDistanceText.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run($"{model.DistanciaRecorridoExistenteM:0.0}m")));
            TxtPrevDistanceText.Inlines.Add($", con lo cual la edificación ");
            TxtPrevDistanceText.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(distEval.ToUpperInvariant())));
            TxtPrevDistanceText.Inlines.Add(" con los parámetros de evacuación.");

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

                // Viñeta Word oficial con sangría francesa
                var bulletGrid = new Grid { Margin = new Thickness(0, 1.5, 0, 3) };
                bulletGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                bulletGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "•",
                    FontWeight = FontWeights.Bold,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(dot, 0);
                bulletGrid.Children.Add(dot);

                var tb = new TextBlock
                {
                    Text = tr,
                    FontSize = 9,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Justify,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 13.5
                };
                Grid.SetColumn(tb, 1);
                bulletGrid.Children.Add(tb);

                PnlPrevConclusionsList.Children.Add(bulletGrid);
            }

            // Imagen 2 (Localización / Edificio)
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
            var border = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = isHeader || isGroup 
                    ? new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)) 
                    : Brushes.Transparent
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tb1 = new TextBlock
            {
                Text = col1,
                FontWeight = isHeader || isGroup ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 8.5,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 2.5, 4, 2.5)
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
                Margin = new Thickness(4, 2.5, 4, 2.5)
            };
            Grid.SetColumn(tb2, 1);
            grid.Children.Add(tb2);

            border.Child = grid;
            PnlPrevClassificationTable.Children.Add(border);
        }
    }
}
