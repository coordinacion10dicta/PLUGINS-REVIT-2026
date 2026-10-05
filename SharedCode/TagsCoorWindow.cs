using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

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
        CotasAlineadasPorSeleccion,
        CotasAlineadasAuto,
        NivelDeUbicacion,
        CamasConduitsPorSeleccion,
        CamasConduitsAuto,
        // Redes Húmedas (Desagües)
        PendientesPorClic,
        PendientesPorSeleccion,
        PendientesTodoEnVista,
        MaterialPorSeleccion,
        MaterialTodoEnVista,
        CambioDeNivelHumedasPorSeleccion,
        CambioDeNivelHumedasAuto,
        EmbebidasPlacaPorSeleccion,
        EmbebidasPlacaAuto,
        EmbebidasPisoPorSeleccion,
        EmbebidasPisoAuto,
        ElevadasPorSeleccion,
        ElevadasAuto,
        PasesVigaPorSeleccion,
        PasesVigaAuto
    }

    public class TagsCoorWindow : Form
    {
        public TagsCoorAction SelectedAction { get; private set; } = TagsCoorAction.Cancelar;

        public TagsCoorWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "DICTA - Tags Coordinación";
            this.Size = new Size(610, 375);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            ToolTip toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // 1. Panel Superior / Banner Oscuro
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(24, 43, 73)
            };

            Label lblTitle = new Label
            {
                Text = "TAGS DE COORDINACIÓN",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(16, 8),
                AutoSize = true
            };

            Label lblSub = new Label
            {
                Text = "Herramientas de etiquetado para redes secas y redes húmedas (desagües)",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 215, 235),
                Location = new Point(17, 29),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);
            this.Controls.Add(pnlHeader);

            // =========================================================================
            // 2. SECCIÓN REDES SECAS
            // =========================================================================
            Panel pnlSecas = new Panel
            {
                Location = new Point(14, 60),
                Size = new Size(566, 80),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblSecas = new Label
            {
                Text = "REDES SECAS",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(10, 6),
                AutoSize = true
            };
            pnlSecas.Controls.Add(lblSecas);

            FlowLayoutPanel flowSecas = new FlowLayoutPanel
            {
                Location = new Point(6, 24),
                Size = new Size(552, 48),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White
            };

            Size btnSize = new Size(130, 40);
            Color secasFore = Color.FromArgb(0, 102, 204);
            Color secasBack = Color.FromArgb(242, 247, 255);
            Color secasBorder = Color.FromArgb(145, 190, 240);
            Color secasHover = Color.FromArgb(228, 240, 255);

            // --- BOTÓN 1: Cotas de Nivel (Desplegable) ---
            ContextMenuStrip menuNiveles = new ContextMenuStrip();
            ToolStripMenuItem itemPorSeleccion = new ToolStripMenuItem("Por Selección Múltiple");
            itemPorSeleccion.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.NivelesPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemAuto = new ToolStripMenuItem("Cotas Automático");
            itemAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.NivelesTodaLaVista;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuNiveles.Items.Add(itemPorSeleccion);
            menuNiveles.Items.Add(itemAuto);
            ConfigureModernMenu(menuNiveles, secasFore, secasHover);

            Button btnNivelesDropdown = CreateDropdownButton("Cotas Nivel", btnSize, secasBack, secasFore, menuNiveles, secasBorder);
            toolTip.SetToolTip(btnNivelesDropdown, "Selecciona 'Por Selección Múltiple' o 'Cotas Automático' para generar cotas de nivel en redes secas.");

            // --- BOTÓN 2: Tags "C.N" (Desplegable) ---
            ContextMenuStrip menuCambioNivel = new ContextMenuStrip();
            ToolStripMenuItem itemCnSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCnSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CambioDeNivelPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCnAuto = new ToolStripMenuItem("Tag Automático");
            itemCnAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CambioDeNivelAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCambioNivel.Items.Add(itemCnSel);
            menuCambioNivel.Items.Add(itemCnAuto);
            ConfigureModernMenu(menuCambioNivel, secasFore, secasHover);

            Button btnCambioNivel = CreateDropdownButton("Tags \"C.N\"", btnSize, secasBack, secasFore, menuCambioNivel, secasBorder);
            toolTip.SetToolTip(btnCambioNivel, "Selecciona 'Por Selección Múltiple' o 'Tag Automático' para cambios de nivel en redes secas.");

            // --- BOTÓN 3: Cotas Alineadas (Desplegable) ---
            ContextMenuStrip menuCotasAlineadas = new ContextMenuStrip();
            ToolStripMenuItem itemCotasAlineadasSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCotasAlineadasSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CotasAlineadasPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCotasAlineadasAuto = new ToolStripMenuItem("Cotas Automático");
            itemCotasAlineadasAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CotasAlineadasAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCotasAlineadas.Items.Add(itemCotasAlineadasSel);
            menuCotasAlineadas.Items.Add(itemCotasAlineadasAuto);
            ConfigureModernMenu(menuCotasAlineadas, secasFore, secasHover);

            Button btnCotasAlineadas = CreateDropdownButton("Cotas Alineadas", btnSize, secasBack, secasFore, menuCotasAlineadas, secasBorder);
            toolTip.SetToolTip(btnCotasAlineadas, "Coloca cotas alineadas en cadenas entre referencias paralelas (ejes en instalaciones, caras en arquitectura).");

            // --- BOTÓN 4: Camas Conduits (Desplegable) ---
            ContextMenuStrip menuCamas = new ContextMenuStrip();
            ToolStripMenuItem itemCamasSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCamasSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasConduitsPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCamasAuto = new ToolStripMenuItem("Camas Automático");
            itemCamasAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasConduitsAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCamas.Items.Add(itemCamasSel);
            menuCamas.Items.Add(itemCamasAuto);
            ConfigureModernMenu(menuCamas, secasFore, secasHover);

            Button btnCamas = CreateDropdownButton("Camas Conduits", btnSize, secasBack, secasFore, menuCamas, secasBorder);
            toolTip.SetToolTip(btnCamas, "Etiqueta camas de conduits indicando cantidad por nivel: (N)- Size - Material/Tipo.");

            flowSecas.Controls.Add(btnNivelesDropdown);
            flowSecas.Controls.Add(btnCambioNivel);
            flowSecas.Controls.Add(btnCotasAlineadas);
            flowSecas.Controls.Add(btnCamas);
            pnlSecas.Controls.Add(flowSecas);
            this.Controls.Add(pnlSecas);

            // =========================================================================
            // 3. SECCIÓN REDES HÚMEDAS (DESAGÜES)
            // =========================================================================
            Panel pnlHumedas = new Panel
            {
                Location = new Point(14, 146),
                Size = new Size(566, 130),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblHumedas = new Label
            {
                Text = "REDES HÚMEDAS (DESAGÜES)",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 130, 110),
                Location = new Point(10, 6),
                AutoSize = true
            };
            pnlHumedas.Controls.Add(lblHumedas);

            FlowLayoutPanel flowHumedas = new FlowLayoutPanel
            {
                Location = new Point(6, 24),
                Size = new Size(552, 98),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.White
            };

            Color humedasFore = Color.FromArgb(0, 130, 110);
            Color humedasBack = Color.FromArgb(238, 250, 246);
            Color humedasBorder = Color.FromArgb(135, 205, 195);
            Color humedasHover = Color.FromArgb(235, 248, 245);

            // --- BOTÓN 1: Tag Pendientes (Desplegable) ---
            ContextMenuStrip menuPendientes = new ContextMenuStrip();
            ToolStripMenuItem itemPendClic = new ToolStripMenuItem("Por Clic Individual");
            itemPendClic.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.PendientesPorClic;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemPendSeleccion = new ToolStripMenuItem("Por Selección Múltiple");
            itemPendSeleccion.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.PendientesPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuPendientes.Items.Add(itemPendClic);
            menuPendientes.Items.Add(itemPendSeleccion);
            ConfigureModernMenu(menuPendientes, humedasFore, humedasHover);

            Button btnPendientes = CreateDropdownButton("Tag Pendientes", btnSize, humedasBack, humedasFore, menuPendientes, humedasBorder);
            toolTip.SetToolTip(btnPendientes, "Etiqueta pendientes (%) con flecha de flujo en tuberías de desagüe.");

            // --- BOTÓN 2: Tag Material (Desplegable) ---
            ContextMenuStrip menuMaterial = new ContextMenuStrip();
            ToolStripMenuItem itemMatSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemMatSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.MaterialPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemMatAuto = new ToolStripMenuItem("Tag Automático");
            itemMatAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.MaterialTodoEnVista;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuMaterial.Items.Add(itemMatSel);
            menuMaterial.Items.Add(itemMatAuto);
            ConfigureModernMenu(menuMaterial, humedasFore, humedasHover);

            Button btnMaterial = CreateDropdownButton("Tag Material", btnSize, humedasBack, humedasFore, menuMaterial, humedasBorder);
            toolTip.SetToolTip(btnMaterial, "Etiqueta el material en tuberías y accesorios.");

            // --- BOTÓN 3: Tag Cambio Nivel Desagües (Desplegable) ---
            ContextMenuStrip menuCambioNivelH = new ContextMenuStrip();
            ToolStripMenuItem itemCnHhumSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCnHhumSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CambioDeNivelHumedasPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCnHhumAuto = new ToolStripMenuItem("Tag Automático");
            itemCnHhumAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CambioDeNivelHumedasAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCambioNivelH.Items.Add(itemCnHhumSel);
            menuCambioNivelH.Items.Add(itemCnHhumAuto);
            ConfigureModernMenu(menuCambioNivelH, humedasFore, humedasHover);

            Button btnCambioNivelH = CreateDropdownButton("Tags \"C.N\"", btnSize, humedasBack, humedasFore, menuCambioNivelH, humedasBorder);
            toolTip.SetToolTip(btnCambioNivelH, "Etiqueta cambios de nivel en codos y bajantes de desagües.");

            // --- BOTÓN 4: Tuberías Embebidas en Placa (Desplegable) ---
            ContextMenuStrip menuEmbebidasPlaca = new ContextMenuStrip();
            ToolStripMenuItem itemPlacaSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemPlacaSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.EmbebidasPlacaPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemPlacaAuto = new ToolStripMenuItem("Placa Automático");
            itemPlacaAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.EmbebidasPlacaAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuEmbebidasPlaca.Items.Add(itemPlacaSel);
            menuEmbebidasPlaca.Items.Add(itemPlacaAuto);
            ConfigureModernMenu(menuEmbebidasPlaca, humedasFore, humedasHover);

            Button btnEmbebidasPlaca = CreateDropdownButton("Embebidas Placa", btnSize, humedasBack, humedasFore, menuEmbebidasPlaca, humedasBorder);
            toolTip.SetToolTip(btnEmbebidasPlaca, "Acota tuberías embebidas en placa a elementos estructurales y ajusta el rango de vista (-15 cm).");

            // --- BOTÓN 5: Tuberías Embebidas en Piso / Afinado (Desplegable) ---
            ContextMenuStrip menuEmbebidasPiso = new ContextMenuStrip();
            ToolStripMenuItem itemPisoSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemPisoSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.EmbebidasPisoPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemPisoAuto = new ToolStripMenuItem("Piso Automático");
            itemPisoAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.EmbebidasPisoAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuEmbebidasPiso.Items.Add(itemPisoSel);
            menuEmbebidasPiso.Items.Add(itemPisoAuto);
            ConfigureModernMenu(menuEmbebidasPiso, humedasFore, humedasHover);

            Button btnEmbebidasPiso = CreateDropdownButton("Embebidas Piso", btnSize, humedasBack, humedasFore, menuEmbebidasPiso, humedasBorder);
            toolTip.SetToolTip(btnEmbebidasPiso, "Acota tuberías en afinado de piso a muros arquitectónicos o estructura.");

            // --- BOTÓN 6: Tuberías Elevadas / Cielo Raso (Desplegable) ---
            ContextMenuStrip menuElevadas = new ContextMenuStrip();
            ToolStripMenuItem itemElevSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemElevSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ElevadasPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemElevAuto = new ToolStripMenuItem("Elevadas Automático");
            itemElevAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ElevadasAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuElevadas.Items.Add(itemElevSel);
            menuElevadas.Items.Add(itemElevAuto);
            ConfigureModernMenu(menuElevadas, humedasFore, humedasHover);

            Button btnElevadas = CreateDropdownButton("Tub. Elevadas", btnSize, humedasBack, humedasFore, menuElevadas, humedasBorder);
            toolTip.SetToolTip(btnElevadas, "Acota tuberías elevadas o sobre cielo raso a la estructura inmediatamente superior.");

            // --- BOTÓN 7: Tags de Pases en Viga (Desplegable) ---
            ContextMenuStrip menuPasesViga = new ContextMenuStrip();
            ToolStripMenuItem itemPasesSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemPasesSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.PasesVigaPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemPasesAuto = new ToolStripMenuItem("Pases Automático");
            itemPasesAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.PasesVigaAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuPasesViga.Items.Add(itemPasesSel);
            menuPasesViga.Items.Add(itemPasesAuto);
            ConfigureModernMenu(menuPasesViga, humedasFore, humedasHover);

            Button btnPasesViga = CreateDropdownButton("Pases en Viga", btnSize, humedasBack, humedasFore, menuPasesViga, humedasBorder);
            toolTip.SetToolTip(btnPasesViga, "Etiqueta pases de tuberías en vigas mostrando únicamente la dimensión (Size / Diámetro).");

            flowHumedas.Controls.Add(btnPendientes);
            flowHumedas.Controls.Add(btnMaterial);
            flowHumedas.Controls.Add(btnCambioNivelH);
            flowHumedas.Controls.Add(btnEmbebidasPlaca);
            flowHumedas.Controls.Add(btnEmbebidasPiso);
            flowHumedas.Controls.Add(btnElevadas);
            flowHumedas.Controls.Add(btnPasesViga);
            pnlHumedas.Controls.Add(flowHumedas);
            this.Controls.Add(pnlHumedas);

            // 4. Botón Inferior Cerrar
            Button btnCerrar = new Button
            {
                Text = "Cerrar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(490, 288),
                Size = new Size(90, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(70, 70, 70),
                Font = new Font("Segoe UI", 8.5F),
                Cursor = Cursors.Hand
            };
            btnCerrar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnCerrar, 6, Color.LightGray);
            btnCerrar.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.Cancelar;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(btnCerrar);
            this.CancelButton = btnCerrar;
        }

        private static void ConfigureModernMenu(ContextMenuStrip menu, Color activeTextColor, Color hoverBg)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.BackColor = Color.White;
            menu.Renderer = new ModernMenuRenderer(hoverBg, activeTextColor);
            menu.Font = new Font("Segoe UI", 8.75F, FontStyle.Regular);
            menu.Padding = new Padding(4, 4, 4, 4);
            menu.AutoSize = true;
            menu.DropShadowEnabled = true;

            menu.Opened += (s, e) =>
            {
                if (menu.Width > 0 && menu.Height > 0)
                {
                    Rectangle rect = new Rectangle(0, 0, menu.Width, menu.Height);
                    using (var path = GetRoundedRectanglePath(rect, 8))
                    {
                        menu.Region = new Region(path);
                    }
                }
            };

            foreach (ToolStripItem item in menu.Items)
            {
                item.Padding = new Padding(12, 7, 12, 7);
                item.Margin = new Padding(2, 2, 2, 2);
                item.Font = new Font("Segoe UI", 8.75F, FontStyle.Regular);
                item.ForeColor = Color.FromArgb(51, 65, 85);
            }
        }

        private static Button CreateDropdownButton(string text, Size size, Color backColor, Color foreColor, ContextMenuStrip menu, Color? borderColor = null)
        {
            Button btn = new Button
            {
                Text = text + "  ",
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 2, 3, 2)
            };
            btn.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btn, 8, borderColor);
            btn.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(foreColor, 1.8f))
                {
                    int right = btn.Width - 12;
                    int cy = btn.Height / 2 - 1;
                    pe.Graphics.DrawLines(pen, new PointF[] {
                        new PointF(right - 4, cy - 2),
                        new PointF(right, cy + 2),
                        new PointF(right + 4, cy - 2)
                    });
                }
            };
            btn.Click += (s, e) =>
            {
                menu.Show(btn, new Point(0, btn.Height + 3));
            };
            return btn;
        }

        public static void ApplyRoundedStyle(Control control, int radius, Color? borderColor)
        {
            Rectangle rect = new Rectangle(0, 0, control.Width, control.Height);
            using (GraphicsPath path = GetRoundedRectanglePath(rect, radius))
            {
                control.Region = new Region(path);
            }

            if (borderColor.HasValue)
            {
                control.Paint += (s, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle borderRect = new Rectangle(1, 1, control.Width - 3, control.Height - 3);
                    using (GraphicsPath path = GetRoundedRectanglePath(borderRect, radius))
                    using (Pen pen = new Pen(borderColor.Value, 1.6f))
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

    public class ModernMenuColorTable : ProfessionalColorTable
    {
        private readonly Color _hoverBg;
        public ModernMenuColorTable(Color hoverBg)
        {
            _hoverBg = hoverBg;
        }

        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuBorder => Color.FromArgb(226, 232, 240);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => _hoverBg;
        public override Color MenuItemSelectedGradientBegin => _hoverBg;
        public override Color MenuItemSelectedGradientEnd => _hoverBg;
        public override Color MenuItemPressedGradientBegin => _hoverBg;
        public override Color MenuItemPressedGradientEnd => _hoverBg;
        public override Color MenuStripGradientBegin => Color.White;
        public override Color MenuStripGradientEnd => Color.White;
        public override Color SeparatorDark => Color.FromArgb(241, 245, 249);
        public override Color SeparatorLight => Color.White;
    }

    public class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color _hoverBg;
        private readonly Color _activeTextColor;
        private readonly Color _menuBorder = Color.FromArgb(226, 232, 240);

        public ModernMenuRenderer(Color hoverBg, Color activeTextColor)
            : base(new ModernMenuColorTable(hoverBg))
        {
            _hoverBg = hoverBg;
            _activeTextColor = activeTextColor;
            this.RoundedEdges = true;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Sin margen lateral de imágenes
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(_menuBorder, 1.0f))
            {
                Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                using (var path = TagsCoorWindow.GetRoundedRectanglePath(r, 8))
                {
                    e.Graphics.DrawPath(p, path);
                }
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (e.Item.Selected || e.Item.Pressed)
            {
                Rectangle r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                if (r.Width <= 0 || r.Height <= 0) return;

                using (var path = TagsCoorWindow.GetRoundedRectanglePath(r, 6))
                using (SolidBrush b = new SolidBrush(_hoverBg))
                {
                    e.Graphics.FillPath(b, path);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                e.TextColor = _activeTextColor;
            }
            else
            {
                e.TextColor = Color.FromArgb(51, 65, 85);
            }
            base.OnRenderItemText(e);
        }
    }
}
