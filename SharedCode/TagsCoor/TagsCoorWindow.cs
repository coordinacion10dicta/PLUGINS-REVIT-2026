using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Control = System.Windows.Forms.Control;

namespace MiNamespace
{
    public enum TagsCoorAction
    {
        Cancelar,
        // Redes Secas
        NivelesPorSeleccion,
        NivelesTodaLaVista,
        CambioDeNivelPorSeleccion,
        CambioDeNivelAuto,
        CambioDeNivelManual,
        CotasAlineadasPorSeleccion,
        CotasAlineadasAuto,
        NivelDeUbicacion,
        CamasConduitsPorSeleccion,
        CamasConduitsAuto,
        CamasTubosPorSeleccion,
        CamasTubosAuto,
        CotasBandejasPorSeleccion,
        CotasBandejasAuto,
        ExostosPorSeleccion,
        ExostosAuto,
        TagRoomPorSeleccion,
        TagRoomAuto,
        BuitronesCuartosPorSeleccion = TagRoomPorSeleccion,
        BuitronesCuartosAuto = TagRoomAuto,
        ConduitTagsPorSeleccion,
        ConduitTagsAuto,
        // Redes Húmedas (Desagües)
        PendientesPorClic,
        PendientesPorSeleccion,
        PendientesTodoEnVista,
        MaterialPorSeleccion,
        MaterialTodoEnVista,
        EmbebidasPlacaPorSeleccion,
        EmbebidasPlacaAuto,
        EmbebidasPisoPorSeleccion,
        EmbebidasPisoAuto,
        ElevadasPorSeleccion,
        ElevadasAuto,
        PasesVigaPorSeleccion,
        PasesVigaAuto,
        // Tags de Ductos (HVAC)
        TagDuctosSoloDimensionesSel,
        TagDuctosSoloDimensionesAuto,
        TagDuctosDimensionesMaterialSel,
        TagDuctosDimensionesMaterialAuto,
        // Generales (Redes Secas y Húmedas)
        TagSubePorSeleccion,
        TagSubeAuto,
        TagBajaPorSeleccion,
        TagBajaAuto
    }

    public class TagsCoorWindow : System.Windows.Forms.Form
    {
        public TagsCoorAction SelectedAction { get; private set; } = TagsCoorAction.Cancelar;
        public string SelectedLevelName { get; private set; }

        private readonly Autodesk.Revit.DB.Document _doc;
        private readonly List<FlipCardControl> _cards = new List<FlipCardControl>();
        private GradientFlowLayoutPanel _pnlGrid;
        private Label _lblFooterDesc;
        private string _activeCategoryFilter = "TODAS";

        public TagsCoorWindow() : this(null)
        {
        }

        public TagsCoorWindow(Autodesk.Revit.DB.Document doc)
        {
            _doc = doc;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "DICTA - Tags de Coordinación";
            this.Size = new Size(1020, 730);
            this.MinimumSize = new Size(880, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(230, 236, 244);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;

            ToolTip toolTip = new ToolTip
            {
                AutoPopDelay = 6000,
                InitialDelay = 300,
                ReshowDelay = 150,
                ShowAlways = true
            };

            // =========================================================================
            // 1. HEADER MODERNO CON GRADIENTE, TÍTULO Y BUSCADOR
            // =========================================================================
            // 1. HEADER MODERNO CON GRADIENTE DICTA Y TÍTULO CENTRADO RESPONSIVO
            // =========================================================================
            GradientHeaderPanel pnlHeader = new GradientHeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 68,
                Padding = new Padding(20, 10, 20, 10)
            };

            Label lblTitle = new Label
            {
                Text = "TAGS DE COORDINACIÓN",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            Label lblSub = new Label
            {
                Text = "Mosaico de herramientas de etiquetado y acotado para Redes Secas, Húmedas, HVAC y Generales",
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 218, 240),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            // Centrado horizontal responsivo del título y subtítulo
            Action centerHeaderLabels = () =>
            {
                if (pnlHeader.ClientSize.Width <= 0) return;
                lblTitle.Location = new Point(Math.Max(10, (pnlHeader.ClientSize.Width - lblTitle.Width) / 2), 12);
                lblSub.Location = new Point(Math.Max(10, (pnlHeader.ClientSize.Width - lblSub.Width) / 2), 38);
            };

            pnlHeader.Resize += (s, e) => centerHeaderLabels();
            this.Shown += (s, e) => centerHeaderLabels();

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);

            // =========================================================================
            // 2. BARRA DE FILTROS POR DISCIPLINA (CHIPS CON RELIEVE Y CENTRADO RESPONSIVO)
            // =========================================================================
            Panel pnlFilters = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(255, 255, 255),
                Padding = new Padding(20, 8, 20, 8)
            };
            pnlFilters.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    pe.Graphics.DrawLine(p, 0, pnlFilters.Height - 1, pnlFilters.Width, pnlFilters.Height - 1);
                }
            };

            FlowLayoutPanel flowFilters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent
            };

            var filterChips = new[]
            {
                new { Tag = "TODAS", Text = "Todas las herramientas (17)", Color = Color.FromArgb(24, 43, 73) },
                new { Tag = "SECAS", Text = "Redes Secas (7)", Color = Color.FromArgb(29, 78, 216) },
                new { Tag = "HUMEDAS", Text = "Redes Húmedas (6)", Color = Color.FromArgb(13, 148, 136) },
                new { Tag = "HVAC", Text = "HVAC (2)", Color = Color.FromArgb(225, 29, 72) },
                new { Tag = "GENERALES", Text = "Generales (2)", Color = Color.FromArgb(124, 58, 237) }
            };

            var chipButtons = new List<Button>();
            foreach (var fc in filterChips)
            {
                Button btnChip = new Button
                {
                    Text = fc.Text,
                    Tag = fc.Tag,
                    AutoSize = true,
                    Height = 32,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Margin = new Padding(0, 0, 10, 0),
                    Padding = new Padding(14, 0, 14, 0)
                };
                btnChip.FlatAppearance.BorderSize = 0;

                bool isSelected = fc.Tag == "TODAS";
                UpdateChipVisual(btnChip, isSelected, fc.Color);

                btnChip.Click += (s, e) =>
                {
                    _activeCategoryFilter = (string)btnChip.Tag;
                    foreach (var b in chipButtons)
                    {
                        var info = filterChips.First(x => x.Tag == (string)b.Tag);
                        UpdateChipVisual(b, b == btnChip, info.Color);
                    }
                    FilterCards();
                };

                chipButtons.Add(btnChip);
                flowFilters.Controls.Add(btnChip);
            }

            // Centrado horizontal responsivo de los filtros
            Action centerFilters = () =>
            {
                int totalChipsWidth = 0;
                foreach (Control c in flowFilters.Controls)
                {
                    totalChipsWidth += c.Width + c.Margin.Horizontal;
                }
                int padLeft = Math.Max(16, (pnlFilters.ClientSize.Width - totalChipsWidth) / 2);
                flowFilters.Padding = new Padding(padLeft, 0, 16, 0);
            };

            pnlFilters.Resize += (s, e) => centerFilters();
            this.Shown += (s, e) => centerFilters();

            pnlFilters.Controls.Add(flowFilters);

            // =========================================================================
            // 3. FOOTER INFERIOR CON PANEL DE DESCRIPCIÓN DINÁMICA Y BOTÓN CERRAR
            // =========================================================================
            Panel pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                BackColor = Color.White,
                Padding = new Padding(20, 9, 20, 9)
            };
            pnlFooter.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    pe.Graphics.DrawLine(p, 0, 0, pnlFooter.Width, 0);
                }
            };

            _lblFooterDesc = new Label
            {
                Text = "💡 Pasa el cursor sobre cualquier tarjeta para ver su descripción o haz clic para girarla y elegir el modo de ejecución.",
                Font = new Font("Segoe UI", 8.75F, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(20, 17),
                AutoSize = true,
                AutoEllipsis = true
            };

            Button btnCerrar = new Button
            {
                Text = "Cerrar",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(pnlFooter.ClientSize.Width - 118, 10)
            };
            btnCerrar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnCerrar, 6, Color.FromArgb(203, 213, 225));
            btnCerrar.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.Cancelar;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            // Ajuste responsivo de la descripción del pie para evitar colisión con el botón Cerrar
            Action updateFooterDescSize = () =>
            {
                int maxW = Math.Max(200, pnlFooter.ClientSize.Width - btnCerrar.Width - 60);
                _lblFooterDesc.MaximumSize = new Size(maxW, 36);
            };
            pnlFooter.Resize += (s, e) => updateFooterDescSize();
            this.Shown += (s, e) => updateFooterDescSize();

            pnlFooter.Controls.Add(_lblFooterDesc);
            pnlFooter.Controls.Add(btnCerrar);

            // =========================================================================
            // 4. PANEL CENTRAL: MOSAICO CON DEGRADADO SUAVE DE FONDO (NO PLANO)
            // =========================================================================
            _pnlGrid = new GradientFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16, 14, 16, 14),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            // Construir las 17 tarjetas de herramientas con volteo y descripción en la tarjeta
            BuildFlipCards(toolTip);

            // Montar la interfaz
            this.Controls.Add(_pnlGrid);
            this.Controls.Add(pnlFilters);
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlFooter);
            this.CancelButton = btnCerrar;
        }

        private static void UpdateChipVisual(Button btn, bool isSelected, Color themeColor)
        {
            if (isSelected)
            {
                btn.BackColor = themeColor;
                btn.ForeColor = Color.White;
                ApplyRoundedStyle(btn, 16, themeColor);
            }
            else
            {
                btn.BackColor = Color.FromArgb(248, 250, 252);
                btn.ForeColor = Color.FromArgb(71, 85, 105);
                ApplyRoundedStyle(btn, 16, Color.FromArgb(203, 213, 225));
            }
        }

        private void BuildFlipCards(ToolTip toolTip)
        {
            _cards.Clear();
            _pnlGrid.Controls.Clear();

            // -------------------------------------------------------------
            // REDES SECAS (7 Herramientas) - Azul Real (#1D4ED8 / #2563EB)
            // -------------------------------------------------------------
            Color secasTheme = Color.FromArgb(29, 78, 216);
            Color secasSoft = Color.FromArgb(239, 246, 255);

            AddCard("Cotas Nivel", "SECAS", "📏", secasTheme, secasSoft,
                "Cotas de nivel en bandejas y conduits.",
                "Coloca cotas de nivel en elementos de redes secas (Bandejas portacables, Conduits, etc.).",
                "Por Selección Múltiple", TagsCoorAction.NivelesPorSeleccion,
                "Cotas Automático", TagsCoorAction.NivelesTodaLaVista,
                toolTip);

            AddCard("Tags \"C.N\"", "SECAS", "⚡", secasTheme, secasSoft,
                "Cambios de nivel con tags 'C.N'.",
                "Etiqueta cambios de nivel en bandejas y conduits con Tags 'C.N'.",
                "Por Selección Múltiple", TagsCoorAction.CambioDeNivelPorSeleccion,
                "Tag Automático", TagsCoorAction.CambioDeNivelAuto,
                toolTip);

            AddCard("Cotas Alineadas", "SECAS", "📐", secasTheme, secasSoft,
                "Cadenas de cotas entre ejes y referencias.",
                "Coloca cotas alineadas en cadenas entre referencias paralelas (ejes en instalaciones, caras en arquitectura).",
                "Por Selección Múltiple", TagsCoorAction.CotasAlineadasPorSeleccion,
                "Cotas Automático", TagsCoorAction.CotasAlineadasAuto,
                toolTip);

            AddCard("Camas Conduits", "SECAS", "⚎", secasTheme, secasSoft,
                "Camas de conduits (N)-Size-Material.",
                "Etiqueta camas de conduits en planta indicando cantidad por nivel: (N)- Size - Material/Tipo.",
                "Por Selección Múltiple", TagsCoorAction.CamasConduitsPorSeleccion,
                "Camas Automático", TagsCoorAction.CamasConduitsAuto,
                toolTip);

            AddCard("Camas Tuberías", "SECAS", "🔀", secasTheme, secasSoft,
                "Salidas en tableros y cortes técnicos.",
                "Etiqueta y acota camas de tuberías/salidas de tablero en detalles y cortes.",
                "Por Selección Múltiple", TagsCoorAction.CamasTubosPorSeleccion,
                "Camas Automático", TagsCoorAction.CamasTubosAuto,
                toolTip);

            AddCard("Cotas Bandejas", "SECAS", "▤", secasTheme, secasSoft,
                "Acota bandejas a la estructura más cercana.",
                "Acota bandejas portacables a la estructura más cercana (muros, vigas, columnas) sin repetir ancho.",
                "Por Selección Múltiple", TagsCoorAction.CotasBandejasPorSeleccion,
                "Bandejas Automático", TagsCoorAction.CotasBandejasAuto,
                toolTip);

            AddCard("Conduit Tags", "SECAS", "🏷️", secasTheme, secasSoft,
                "Etiqueta conduits Type Name + Size.",
                "Etiqueta conduits de Eléctrico, Electrónico y HVAC (Control) con el formato Type Name + Size.",
                "Por Selección Múltiple", TagsCoorAction.ConduitTagsPorSeleccion,
                "Tag Automático", TagsCoorAction.ConduitTagsAuto,
                toolTip);

            // -------------------------------------------------------------
            // REDES HÚMEDAS (6 Herramientas) - Teal (#0D9488)
            // -------------------------------------------------------------
            Color humedasTheme = Color.FromArgb(13, 148, 136);
            Color humedasSoft = Color.FromArgb(240, 253, 250);

            AddCard("Tag Pendientes", "HUMEDAS", "∠%", humedasTheme, humedasSoft,
                "Pendientes (%) con flecha de flujo.",
                "Etiqueta pendientes (%) por clic con flecha de flujo en tuberías de desagüe.",
                "Por Clic", TagsCoorAction.PendientesPorClic,
                "Automático en Vista", TagsCoorAction.PendientesTodoEnVista,
                toolTip);

            AddCard("Tag Material", "HUMEDAS", "💧", humedasTheme, humedasSoft,
                "Material en tuberías y accesorios.",
                "Etiqueta el material en tuberías y accesorios hidrosanitarios.",
                "Por Selección Múltiple", TagsCoorAction.MaterialPorSeleccion,
                "Tag Automático", TagsCoorAction.MaterialTodoEnVista,
                toolTip);

            AddCard("Embebidas Placa", "HUMEDAS", "▰", humedasTheme, humedasSoft,
                "Tuberías en placa con ajuste de rango.",
                "Acota tuberías embebidas en placa a elementos estructurales y ajusta el rango de vista (-15 cm).",
                "Por Selección Múltiple", TagsCoorAction.EmbebidasPlacaPorSeleccion,
                "Placa Automático", TagsCoorAction.EmbebidasPlacaAuto,
                toolTip);

            AddCard("Embebidas Piso", "HUMEDAS", "▬", humedasTheme, humedasSoft,
                "Tuberías en afinado a muros o estructura.",
                "Acota tuberías en afinado de piso a muros arquitectónicos o estructura.",
                "Por Selección Múltiple", TagsCoorAction.EmbebidasPisoPorSeleccion,
                "Piso Automático", TagsCoorAction.EmbebidasPisoAuto,
                toolTip);

            AddCard("Tub. Elevadas", "HUMEDAS", "↥", humedasTheme, humedasSoft,
                "Tuberías elevadas a estructura superior.",
                "Acota tuberías elevadas o sobre cielo raso a la estructura inmediatamente superior.",
                "Por Selección Múltiple", TagsCoorAction.ElevadasPorSeleccion,
                "Elevadas Automático", TagsCoorAction.ElevadasAuto,
                toolTip);

            AddCard("Pases en Viga", "HUMEDAS", "▣", humedasTheme, humedasSoft,
                "Dimensión de pases en vigas estructurales.",
                "Etiqueta pases de tuberías en vigas mostrando únicamente la dimensión (Size / Diámetro).",
                "Por Selección Múltiple", TagsCoorAction.PasesVigaPorSeleccion,
                "Pases Automático", TagsCoorAction.PasesVigaAuto,
                toolTip);

            // -------------------------------------------------------------
            // HVAC (2 Herramientas) - Coral / Rosa Carmín (#E11D48)
            // -------------------------------------------------------------
            Color hvacTheme = Color.FromArgb(225, 29, 72);
            Color hvacSoft = Color.FromArgb(255, 241, 242);

            AddCard("Tags Ductos", "HVAC", "▭", hvacTheme, hvacSoft,
                "Ductos de climatización (WxH + Mat).",
                "Etiqueta ductos de climatización y ventilación con dimensiones (ancho x alto) y materialidad.",
                "Solo Dimensiones", TagsCoorAction.TagDuctosSoloDimensionesSel,
                "Dimensiones + Material", TagsCoorAction.TagDuctosDimensionesMaterialSel,
                toolTip);

            AddCard("Tags Exostos", "HVAC", "🌀", hvacTheme, hvacSoft,
                "Ductos y tuberías de exostos.",
                "Etiqueta ductos y tuberías de exostos indicando diámetro y materialidad.",
                "Por Selección Múltiple", TagsCoorAction.ExostosPorSeleccion,
                "Tag Automático", TagsCoorAction.ExostosAuto,
                toolTip);

            // -------------------------------------------------------------
            // GENERALES (2 Herramientas) - Púrpura Imperial (#7C3AED)
            // -------------------------------------------------------------
            Color genTheme = Color.FromArgb(124, 58, 237);
            Color genSoft = Color.FromArgb(245, 243, 255);

            AddCard("Sube / Baja", "GENERALES", "⇅", genTheme, genSoft,
                "Etiquetas SUBE o BAJA en verticales.",
                "Coloca etiquetas 'SUBE' o 'BAJA' en accesorios y tuberías verticales.",
                "Tag SUBE", TagsCoorAction.TagSubePorSeleccion,
                "Tag BAJA", TagsCoorAction.TagBajaPorSeleccion,
                toolTip);

            AddCard("Tag Room", "GENERALES", "🚪", genTheme, genSoft,
                "Buitrones y cuartos técnicos con tags.",
                "Etiqueta habitaciones y buitrones técnicos (HID, ELE, HVAC, RCI y cuartos técnicos).",
                "Por Selección Múltiple", TagsCoorAction.TagRoomPorSeleccion,
                "Tag Automático", TagsCoorAction.TagRoomAuto,
                toolTip);
        }

        private void AddCard(string title, string category, string iconSymbol, Color themeColor, Color softBg,
            string shortDescription, string fullDescription,
            string opt1Text, TagsCoorAction opt1Action, string opt2Text, TagsCoorAction opt2Action,
            ToolTip toolTip)
        {
            var card = new FlipCardControl(title, category, iconSymbol, themeColor, softBg,
                shortDescription, fullDescription, opt1Text, opt1Action, opt2Text, opt2Action);

            card.ActionTriggered += (act) =>
            {
                SelectedAction = act;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            card.CardHovered += (desc) =>
            {
                if (_lblFooterDesc != null)
                {
                    _lblFooterDesc.Text = desc;
                    _lblFooterDesc.ForeColor = Color.FromArgb(15, 23, 42);
                }
            };

            card.CardUnhovered += () =>
            {
                if (_lblFooterDesc != null)
                {
                    _lblFooterDesc.Text = "💡 Pasa el cursor sobre cualquier tarjeta para ver su descripción o haz clic para girarla y elegir el modo de ejecución.";
                    _lblFooterDesc.ForeColor = Color.FromArgb(71, 85, 105);
                }
            };

            card.FlipRequested += () =>
            {
                // Al girar una tarjeta, desvoltear cualquier otra abierta
                foreach (var c in _cards)
                {
                    if (c != card && c.IsFlipped)
                    {
                        c.FlipBack();
                    }
                }
            };

            toolTip.SetToolTip(card, $"{title}\n\n{fullDescription}\n\n👉 Clic para girar y seleccionar modo.");

            _cards.Add(card);
            _pnlGrid.Controls.Add(card);
        }

        private void FilterCards()
        {
            _pnlGrid.SuspendLayout();
            foreach (var card in _cards)
            {
                bool matchesCat = _activeCategoryFilter == "TODAS" || card.Category == _activeCategoryFilter;
                card.Visible = matchesCat;
                if (!card.Visible && card.IsFlipped)
                {
                    card.FlipBack(animate: false);
                }
            }
            _pnlGrid.ResumeLayout();
        }

        public static void ApplyRoundedStyle(Control control, int radius, Color? borderColor)
        {
            Action updateRegion = () =>
            {
                if (control.Width > 0 && control.Height > 0)
                {
                    Rectangle rect = new Rectangle(0, 0, control.Width, control.Height);
                    using (GraphicsPath path = GetRoundedRectanglePath(rect, radius))
                    {
                        control.Region = new Region(path);
                    }
                }
            };

            updateRegion();
            control.SizeChanged += (s, e) => updateRegion();

            if (borderColor.HasValue)
            {
                control.Paint += (s, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle borderRect = new Rectangle(1, 1, control.Width - 3, control.Height - 3);
                    using (GraphicsPath path = GetRoundedRectanglePath(borderRect, radius))
                    using (Pen pen = new Pen(borderColor.Value, 1.4f))
                    {
                        pe.Graphics.DrawPath(pen, path);
                    }
                };
            }
        }

        public static GraphicsPath GetRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arcRect = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arcRect, 180, 90);
            arcRect.X = rect.Right - diameter;
            path.AddArc(arcRect, 270, 90);
            arcRect.Y = rect.Bottom - diameter;
            path.AddArc(arcRect, 0, 90);
            arcRect.X = rect.Left;
            path.AddArc(arcRect, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// Panel contenedor con degradado suave vertical para eliminar el fondo plano
    /// </summary>
    public class GradientFlowLayoutPanel : FlowLayoutPanel
    {
        public GradientFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (this.ClientRectangle.Width <= 0 || this.ClientRectangle.Height <= 0) return;
            using (LinearGradientBrush brush = new LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(238, 242, 248), // Gris azulado nórdico superior
                Color.FromArgb(220, 228, 239), // Sombra inferior con profundidad suave
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }
    }

    /// <summary>
    /// Cabecera con degradado Azul Marino Corporativo DICTA y línea de acento
    /// </summary>
    public class GradientHeaderPanel : Panel
    {
        public GradientHeaderPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (this.ClientRectangle.Width <= 0 || this.ClientRectangle.Height <= 0) return;
            using (LinearGradientBrush brush = new LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(24, 43, 73),   // Azul DICTA Corporativo (como en NivelReferenciaSelectorWindow)
                Color.FromArgb(32, 58, 98),   // Azul Profundo
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            // Línea de brillo inferior azul DICTA
            using (Pen pen = new Pen(Color.FromArgb(0, 102, 204), 2f))
            {
                e.Graphics.DrawLine(pen, 0, this.Height - 1, this.Width, this.Height - 1);
            }
        }
    }

    /// <summary>
    /// Tarjeta Flotante con Relieve 3D y Efecto Flip (Giro):
    /// Muestra ícono, nombre y descripción visible directamente en la tarjeta frontal.
    /// Al hacer clic, gira revelando los modos de ejecución en el reverso.
    /// </summary>
    public class FlipCardControl : UserControl
    {
        public string Title { get; }
        public string Category { get; }
        public string IconSymbol { get; }
        public Color ThemeColor { get; }
        public Color SoftBg { get; }
        public string ShortDescription { get; }
        public string FullDescription { get; }

        public string Opt1Text { get; }
        public TagsCoorAction Opt1Action { get; }
        public string Opt2Text { get; }
        public TagsCoorAction Opt2Action { get; }

        public bool IsFlipped { get; private set; } = false;

        public event Action<TagsCoorAction> ActionTriggered;
        public event Action<string> CardHovered;
        public event Action CardUnhovered;
        public event Action FlipRequested;

        private CardFacePanel _pnlFront;
        private CardFacePanel _pnlBack;
        private Label _lblShortDesc;
        private BorderLabel _lblFlipHint;
        private System.Windows.Forms.Timer _flipTimer;
        private int _flipStep = 0;
        private const int TOTAL_FLIP_STEPS = 16;
        private bool _isFlippingToBack = true;
        private const int TARGET_WIDTH = 168;
        private const int TARGET_HEIGHT = 196;

        public FlipCardControl(string title, string category, string iconSymbol, Color themeColor, Color softBg,
            string shortDescription, string fullDescription,
            string opt1Text, TagsCoorAction opt1Action, string opt2Text, TagsCoorAction opt2Action)
        {
            Title = title;
            Category = category;
            IconSymbol = iconSymbol;
            ThemeColor = themeColor;
            SoftBg = softBg;
            ShortDescription = shortDescription;
            FullDescription = fullDescription;
            Opt1Text = opt1Text;
            Opt1Action = opt1Action;
            Opt2Text = opt2Text;
            Opt2Action = opt2Action;

            this.Size = new Size(TARGET_WIDTH, TARGET_HEIGHT);
            this.Margin = new Padding(5, 7, 5, 7);
            this.BackColor = Color.Transparent;
            this.DoubleBuffered = true;

            BuildFrontPanel();
            BuildBackPanel();

            this.Controls.Add(_pnlFront);
            this.Controls.Add(_pnlBack);

            _pnlFront.BringToFront();
            _pnlBack.Visible = false;

            _flipTimer = new System.Windows.Forms.Timer { Interval = 10 };
            _flipTimer.Tick += FlipTimer_Tick;
        }

        private void BuildFrontPanel()
        {
            _pnlFront = new CardFacePanel(ThemeColor, isBackFace: false)
            {
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };

            // Círculo de ícono superior con sombra y borde suave
            Panel pnlIconCircle = new Panel
            {
                Size = new Size(42, 42),
                Location = new Point((TARGET_WIDTH - 42) / 2, 12),
                BackColor = SoftBg,
                Cursor = Cursors.Hand
            };
            TagsCoorWindow.ApplyRoundedStyle(pnlIconCircle, 21, Color.FromArgb(226, 232, 240));

            Label lblIcon = new Label
            {
                Text = IconSymbol,
                Font = new Font("Segoe UI Emoji", 13.5F, FontStyle.Regular),
                ForeColor = ThemeColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            pnlIconCircle.Controls.Add(lblIcon);

            // Título de la herramienta en negrita
            Label lblTitle = new Label
            {
                Text = Title,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42), // Slate 900
                Location = new Point(4, 58),
                Size = new Size(TARGET_WIDTH - 8, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            // DESCRIPCIÓN VISIBLE EN LA TARJETA (2 líneas elegantes y legibles)
            _lblShortDesc = new Label
            {
                Text = ShortDescription,
                Font = new Font("Segoe UI", 7.75F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139), // Slate 500
                Location = new Point(6, 80),
                Size = new Size(TARGET_WIDTH - 12, 36),
                TextAlign = ContentAlignment.TopCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            // Botón / Pista inferior de giro
            _lblFlipHint = new BorderLabel
            {
                Text = "↻ Elegir modo",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = ThemeColor,
                BackColor = SoftBg,
                BorderColor = Color.FromArgb(203, 213, 225),
                BorderRadius = 5,
                Size = new Size(106, 24),
                Location = new Point((TARGET_WIDTH - 106) / 2, 128),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };

            _pnlFront.Controls.Add(pnlIconCircle);
            _pnlFront.Controls.Add(lblTitle);
            _pnlFront.Controls.Add(_lblShortDesc);
            _pnlFront.Controls.Add(_lblFlipHint);

            // Interacción Hover en la cara frontal
            Action onEnter = () =>
            {
                if (!IsFlipped)
                {
                    _pnlFront.IsHovered = true;
                    _pnlFront.Invalidate();
                    _lblShortDesc.ForeColor = Color.FromArgb(30, 41, 59);
                    _lblFlipHint.BackColor = ThemeColor;
                    _lblFlipHint.ForeColor = Color.White;
                    _lblFlipHint.BorderColor = ThemeColor;
                    _lblFlipHint.Invalidate();
                    CardHovered?.Invoke($"📌 {Title}: {FullDescription}");
                }
            };

            Action onLeave = () =>
            {
                if (!IsFlipped)
                {
                    _pnlFront.IsHovered = false;
                    _pnlFront.Invalidate();
                    _lblShortDesc.ForeColor = Color.FromArgb(100, 116, 139);
                    _lblFlipHint.BackColor = SoftBg;
                    _lblFlipHint.ForeColor = ThemeColor;
                    _lblFlipHint.BorderColor = Color.FromArgb(203, 213, 225);
                    _lblFlipHint.Invalidate();
                    CardUnhovered?.Invoke();
                }
            };

            Action onClick = () =>
            {
                Flip();
            };

            BindEventsRecursive(_pnlFront, onEnter, onLeave, onClick);
        }

        private void BuildBackPanel()
        {
            _pnlBack = new CardFacePanel(ThemeColor, isBackFace: true)
            {
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };

            // Barra superior del reverso (sin la cruz '✕' - hacer clic en cualquier parte de la tarjeta la gira)
            Panel pnlBackHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = SoftBg,
                Padding = new Padding(4, 0, 4, 0),
                Cursor = Cursors.Hand
            };

            Label lblBackTitle = new Label
            {
                Text = Title,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                ForeColor = ThemeColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            pnlBackHeader.Controls.Add(lblBackTitle);

            // Botón Opción 1 (Por Selección / Por Clic)
            Button btnOpt1 = new Button
            {
                Text = "👆  " + Opt1Text,
                Size = new Size(TARGET_WIDTH - 20, 46),
                Location = new Point(10, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = SoftBg,
                ForeColor = ThemeColor,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnOpt1.FlatAppearance.BorderSize = 0;
            TagsCoorWindow.ApplyRoundedStyle(btnOpt1, 7, ThemeColor);
            btnOpt1.Click += (s, e) => ActionTriggered?.Invoke(Opt1Action);

            // Botón Opción 2 (Automático en Vista)
            Button btnOpt2 = new Button
            {
                Text = "⚡  " + Opt2Text,
                Size = new Size(TARGET_WIDTH - 20, 46),
                Location = new Point(10, 98),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnOpt2.FlatAppearance.BorderSize = 0;
            TagsCoorWindow.ApplyRoundedStyle(btnOpt2, 7, null);
            btnOpt2.Click += (s, e) => ActionTriggered?.Invoke(Opt2Action);

            // Pista inferior para volver
            Label lblBackHint = new Label
            {
                Text = "↺ Clic para volver",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(TARGET_WIDTH - 20, 20),
                Location = new Point(10, 150),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            // Al hacer clic sobre cualquier área de la tarjeta reversa (excepto los botones), se desvoltea
            Action flipBackAction = () => FlipBack();
            _pnlBack.Click += (s, e) => flipBackAction();
            pnlBackHeader.Click += (s, e) => flipBackAction();
            lblBackTitle.Click += (s, e) => flipBackAction();
            lblBackHint.Click += (s, e) => flipBackAction();

            _pnlBack.Controls.Add(lblBackHint);
            _pnlBack.Controls.Add(btnOpt2);
            _pnlBack.Controls.Add(btnOpt1);
            _pnlBack.Controls.Add(pnlBackHeader);
        }

        private static void BindEventsRecursive(Control parent, Action onEnter, Action onLeave, Action onClick)
        {
            parent.MouseEnter += (s, e) => onEnter();
            parent.MouseLeave += (s, e) => onLeave();
            parent.Click += (s, e) => onClick();

            foreach (Control child in parent.Controls)
            {
                child.MouseEnter += (s, e) => onEnter();
                child.MouseLeave += (s, e) => onLeave();
                child.Click += (s, e) => onClick();
            }
        }

        public void Flip()
        {
            if (IsFlipped || _flipTimer.Enabled) return;
            FlipRequested?.Invoke();
            _isFlippingToBack = true;
            IsFlipped = true;
            _flipStep = 0;
            _pnlFront.Dock = DockStyle.None;
            _pnlBack.Dock = DockStyle.None;
            _pnlFront.Visible = true;
            _pnlBack.Visible = false;
            _flipTimer.Start();
        }

        public void FlipBack(bool animate = true)
        {
            if (!IsFlipped) return;
            IsFlipped = false;
            if (animate)
            {
                _isFlippingToBack = false;
                _flipStep = 0;
                _pnlFront.Dock = DockStyle.None;
                _pnlBack.Dock = DockStyle.None;
                _pnlBack.Visible = true;
                _pnlFront.Visible = false;
                _flipTimer.Start();
            }
            else
            {
                _flipTimer.Stop();
                _pnlBack.Visible = false;
                _pnlFront.Visible = true;
                _pnlFront.Dock = DockStyle.Fill;
                _pnlFront.FlipShadeAlpha = 0;
                _pnlBack.FlipShadeAlpha = 0;
                _pnlFront.Invalidate();
            }
        }

        private void FlipTimer_Tick(object sender, EventArgs e)
        {
            _flipStep++;
            float t = (float)_flipStep / TOTAL_FLIP_STEPS; // Progreso normalizado de 0.0 a 1.0

            if (t >= 1.0f)
            {
                _flipTimer.Stop();
                if (_isFlippingToBack)
                {
                    _pnlFront.Visible = false;
                    _pnlBack.Visible = true;
                    _pnlBack.Dock = DockStyle.Fill;
                    _pnlBack.FlipShadeAlpha = 0;
                    _pnlBack.Invalidate();
                }
                else
                {
                    _pnlBack.Visible = false;
                    _pnlFront.Visible = true;
                    _pnlFront.Dock = DockStyle.Fill;
                    _pnlFront.FlipShadeAlpha = 0;
                    _pnlFront.Invalidate();
                }
                return;
            }

            // Easing de giro 3D: Proyección coseno suave de 0 a PI (desacelera al inicio y fin)
            double angle = t * Math.PI;
            float scaleX = (float)Math.Abs(Math.Cos(angle));
            scaleX = Math.Max(0.04f, scaleX);

            // Reducción sutil de altura para perspectiva óptica 3D
            int heightContract = (int)((1.0f - scaleX) * 10);
            int newW = Math.Max(4, (int)(TARGET_WIDTH * scaleX));
            int newH = TARGET_HEIGHT - heightContract;
            int newLeft = (TARGET_WIDTH - newW) / 2;
            int newTop = heightContract / 2;

            // Sombra dinámica que oscurece elegantemente durante el giro simulando iluminación real
            int shadeAlpha = (int)((1.0f - scaleX) * 75);

            if (t < 0.5f)
            {
                // Primera mitad del giro: la cara activa se contrae
                CardFacePanel activeFace = _isFlippingToBack ? _pnlFront : _pnlBack;
                CardFacePanel hiddenFace = _isFlippingToBack ? _pnlBack : _pnlFront;

                hiddenFace.Visible = false;
                activeFace.Visible = true;
                activeFace.Size = new Size(newW, newH);
                activeFace.Location = new Point(newLeft, newTop);
                activeFace.FlipShadeAlpha = shadeAlpha;
                activeFace.Invalidate();
            }
            else
            {
                // Segunda mitad del giro: la nueva cara se expande revelándose
                CardFacePanel activeFace = _isFlippingToBack ? _pnlBack : _pnlFront;
                CardFacePanel hiddenFace = _isFlippingToBack ? _pnlFront : _pnlBack;

                hiddenFace.Visible = false;
                activeFace.Visible = true;
                activeFace.BringToFront();
                activeFace.Size = new Size(newW, newH);
                activeFace.Location = new Point(newLeft, newTop);
                activeFace.FlipShadeAlpha = shadeAlpha;
                activeFace.Invalidate();
            }
        }
    }

    /// <summary>
    /// Panel de la cara de la tarjeta con sombra de elevación 3D, iluminación dinámica en giro y barra de acento superior
    /// </summary>
    public class CardFacePanel : Panel
    {
        public Color ThemeColor { get; }
        public bool IsBackFace { get; }
        public bool IsHovered { get; set; } = false;
        public int FlipShadeAlpha { get; set; } = 0;

        public CardFacePanel(Color themeColor, bool isBackFace)
        {
            ThemeColor = themeColor;
            IsBackFace = isBackFace;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.Width;
            int h = this.Height;
            if (w <= 8 || h <= 8) return;

            // 1. Sombra de elevación flotante (Drop Shadow suave multicapa)
            int shadowDepth = IsHovered ? 4 : 2;
            int shadowAlpha = IsHovered ? 26 : 14;

            for (int i = 0; i < shadowDepth; i++)
            {
                Rectangle shadowRect = new Rectangle(2 + i, 2 + i * 2, w - 4 - i * 2, h - 3 - i);
                using (GraphicsPath shadowPath = TagsCoorWindow.GetRoundedRectanglePath(shadowRect, 10))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(shadowAlpha - (i * 3), 0, 0, 0)))
                {
                    e.Graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            // 2. Fondo blanco puro de la tarjeta
            Rectangle cardRect = new Rectangle(1, 1, w - 3, h - 4);
            using (GraphicsPath cardPath = TagsCoorWindow.GetRoundedRectanglePath(cardRect, 10))
            {
                using (SolidBrush bgBrush = new SolidBrush(IsHovered ? Color.FromArgb(255, 255, 255) : Color.White))
                {
                    e.Graphics.FillPath(bgBrush, cardPath);
                }

                // 3. Barra de acento superior según la disciplina (5px de alto)
                if (!IsBackFace)
                {
                    e.Graphics.SetClip(cardPath);
                    Rectangle topBarRect = new Rectangle(0, 0, w, 5);
                    using (SolidBrush barBrush = new SolidBrush(ThemeColor))
                    {
                        e.Graphics.FillRectangle(barBrush, topBarRect);
                    }
                    e.Graphics.ResetClip();
                }

                // 4. Borde activo al hover o borde sutil en reposo
                Color borderColor = IsHovered 
                    ? ThemeColor 
                    : Color.FromArgb(226, 232, 240); // Slate 200
                float penWidth = IsHovered ? 1.6f : 1.1f;

                using (Pen pen = new Pen(borderColor, penWidth))
                {
                    e.Graphics.DrawPath(pen, cardPath);
                }

                // 5. Sombra de iluminación 3D durante el giro
                if (FlipShadeAlpha > 0)
                {
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(Math.Min(110, FlipShadeAlpha), 15, 23, 42)))
                    {
                        e.Graphics.FillPath(shade, cardPath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Etiqueta estilizada con borde redondeado
    /// </summary>
    public class BorderLabel : Label
    {
        public Color BorderColor { get; set; } = Color.Transparent;
        public int BorderRadius { get; set; } = 4;

        public BorderLabel()
        {
            this.DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (var path = TagsCoorWindow.GetRoundedRectanglePath(r, BorderRadius))
            using (SolidBrush bg = new SolidBrush(this.BackColor))
            using (Pen p = new Pen(BorderColor, 1.2f))
            {
                e.Graphics.FillPath(bg, path);
                e.Graphics.DrawPath(p, path);
            }

            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, r, this.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
