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
            this.Text = "DICTA - Tags Coordinación";
            this.Size = new Size(820, 700);
            this.MinimumSize = new Size(680, 540);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;

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
                Height = 56,
                BackColor = Color.FromArgb(24, 43, 73)
            };

            Label lblTitle = new Label
            {
                Text = "TAGS DE COORDINACIÓN",
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(16, 9),
                AutoSize = true
            };

            Label lblSub = new Label
            {
                Text = "Herramientas de etiquetado para redes secas, redes húmedas (desagües) y HVAC",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 215, 235),
                Location = new Point(17, 31),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);

            // 1.1 Panel Inferior (Footer) con botón Cerrar
            Panel pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(248, 249, 250)
            };

            // 1.2 Panel Central con Scroll Responsivo
            Panel pnlMain = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(12, 10, 12, 10)
            };



            // =========================================================================
            // 2. SECCIÓN REDES SECAS
            // =========================================================================
            Panel pnlSecas = new Panel
            {
                Location = new Point(12, 10),
                Size = new Size(780, 130),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblSecas = new Label
            {
                Text = "REDES SECAS",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(10, 8),
                AutoSize = true
            };
            pnlSecas.Controls.Add(lblSecas);

            FlowLayoutPanel flowSecas = new FlowLayoutPanel
            {
                Location = new Point(6, 28),
                Size = new Size(765, 98),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.White
            };

            Size btnSize = new Size(150, 42);
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
            ContextMenuStrip menuCamasConduits = new ContextMenuStrip();
            ToolStripMenuItem itemCamasConduitsSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCamasConduitsSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasConduitsPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCamasConduitsAuto = new ToolStripMenuItem("Camas Automático");
            itemCamasConduitsAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasConduitsAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCamasConduits.Items.Add(itemCamasConduitsSel);
            menuCamasConduits.Items.Add(itemCamasConduitsAuto);
            ConfigureModernMenu(menuCamasConduits, secasFore, secasHover);

            Button btnCamasConduits = CreateDropdownButton("Camas Conduits", btnSize, secasBack, secasFore, menuCamasConduits, secasBorder);
            toolTip.SetToolTip(btnCamasConduits, "Etiqueta camas de conduits en planta indicando cantidad por nivel: (N)- Size - Material/Tipo.");

            // --- BOTÓN 5: Camas de Tubos (Desplegable) ---
            ContextMenuStrip menuCamasTubos = new ContextMenuStrip();
            ToolStripMenuItem itemCamasTubosSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemCamasTubosSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasTubosPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemCamasTubosAuto = new ToolStripMenuItem("Camas Automático");
            itemCamasTubosAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CamasTubosAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCamasTubos.Items.Add(itemCamasTubosSel);
            menuCamasTubos.Items.Add(itemCamasTubosAuto);
            ConfigureModernMenu(menuCamasTubos, secasFore, secasHover);

            Button btnCamasTubos = CreateDropdownButton("Camas de Tuberías", btnSize, secasBack, secasFore, menuCamasTubos, secasBorder);
            toolTip.SetToolTip(btnCamasTubos, "Etiqueta y acota camas de tuberías/salidas de tablero en detalles y cortes (N.E. / N TUBERÍAS / Diámetro - Material).");

            // --- BOTÓN 6: Cotas Bandejas (Desplegable) ---
            ContextMenuStrip menuCotasBandejas = new ContextMenuStrip();
            ToolStripMenuItem itemBandejasSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemBandejasSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CotasBandejasPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemBandejasAuto = new ToolStripMenuItem("Bandejas Automático");
            itemBandejasAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.CotasBandejasAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuCotasBandejas.Items.Add(itemBandejasSel);
            menuCotasBandejas.Items.Add(itemBandejasAuto);
            ConfigureModernMenu(menuCotasBandejas, secasFore, secasHover);

            Button btnCotasBandejas = CreateDropdownButton("Cotas Bandejas", btnSize, secasBack, secasFore, menuCotasBandejas, secasBorder);
            toolTip.SetToolTip(btnCotasBandejas, "Acota bandejas portacables a la estructura más cercana (muros, vigas, columnas) sin repetir ancho.");

            // --- BOTÓN 7: Conduit Tags (Desplegable) ---
            ContextMenuStrip menuConduitTags = new ContextMenuStrip();
            ToolStripMenuItem itemConduitTagsSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemConduitTagsSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ConduitTagsPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemConduitTagsAuto = new ToolStripMenuItem("Tag Automático");
            itemConduitTagsAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ConduitTagsAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuConduitTags.Items.Add(itemConduitTagsSel);
            menuConduitTags.Items.Add(itemConduitTagsAuto);
            ConfigureModernMenu(menuConduitTags, secasFore, secasHover);

            Button btnConduitTags = CreateDropdownButton("Conduit Tags", btnSize, secasBack, secasFore, menuConduitTags, secasBorder);
            toolTip.SetToolTip(btnConduitTags, "Etiqueta conduits de Eléctrico, Electrónico y HVAC (Control) con el formato Type Name + Size.");

            flowSecas.Controls.Add(btnNivelesDropdown);
            flowSecas.Controls.Add(btnCambioNivel);
            flowSecas.Controls.Add(btnCotasAlineadas);
            flowSecas.Controls.Add(btnCamasConduits);
            flowSecas.Controls.Add(btnCamasTubos);
            flowSecas.Controls.Add(btnCotasBandejas);
            flowSecas.Controls.Add(btnConduitTags);
            pnlSecas.Controls.Add(flowSecas);

            // =========================================================================
            // 3. SECCIÓN REDES HÚMEDAS (DESAGÜES)
            // =========================================================================
            Panel pnlHumedas = new Panel
            {
                Location = new Point(12, 160),
                Size = new Size(780, 130),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblHumedas = new Label
            {
                Text = "REDES HÚMEDAS (DESAGÜES)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 130, 110),
                Location = new Point(10, 8),
                AutoSize = true
            };
            pnlHumedas.Controls.Add(lblHumedas);

            FlowLayoutPanel flowHumedas = new FlowLayoutPanel
            {
                Location = new Point(6, 28),
                Size = new Size(765, 98),
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
            ToolStripMenuItem itemPendClic = new ToolStripMenuItem("Por Clic");
            itemPendClic.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.PendientesPorClic;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuPendientes.Items.Add(itemPendClic);
            ConfigureModernMenu(menuPendientes, humedasFore, humedasHover);

            Button btnPendientes = CreateDropdownButton("Tag Pendientes", btnSize, humedasBack, humedasFore, menuPendientes, humedasBorder);
            toolTip.SetToolTip(btnPendientes, "Etiqueta pendientes (%) por clic con flecha de flujo en tuberías de desagüe.");

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

            // --- BOTÓN 3: Tuberías Embebidas en Placa (Desplegable) ---
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

            // --- BOTÓN 4: Tuberías Embebidas en Piso / Afinado (Desplegable) ---
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

            // --- BOTÓN 5: Tuberías Elevadas / Cielo Raso (Desplegable) ---
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

            // --- BOTÓN 6: Tags de Pases en Viga (Desplegable) ---
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

            // --- BOTÓN 7: Tags de Ductos (Desplegable) ---
            ContextMenuStrip menuDuctos = new ContextMenuStrip();
            ToolStripMenuItem itemDuctosSoloDimSel = new ToolStripMenuItem("Solo Dimensiones (Selección Múltiple)");
            itemDuctosSoloDimSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagDuctosSoloDimensionesSel;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemDuctosSoloDimAuto = new ToolStripMenuItem("Solo Dimensiones (Automático en Vista)");
            itemDuctosSoloDimAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagDuctosSoloDimensionesAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemDuctosMatSel = new ToolStripMenuItem("Dimensiones + Material (Selección Múltiple)");
            itemDuctosMatSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagDuctosDimensionesMaterialSel;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemDuctosMatAuto = new ToolStripMenuItem("Dimensiones + Material (Automático en Vista)");
            itemDuctosMatAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagDuctosDimensionesMaterialAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuDuctos.Items.Add(itemDuctosSoloDimSel);
            menuDuctos.Items.Add(itemDuctosSoloDimAuto);
            menuDuctos.Items.Add(new ToolStripSeparator());
            menuDuctos.Items.Add(itemDuctosMatSel);
            menuDuctos.Items.Add(itemDuctosMatAuto);
            ConfigureModernMenu(menuDuctos, humedasFore, humedasHover);

            Button btnDuctos = CreateDropdownButton("Tags Ductos", btnSize, humedasBack, humedasFore, menuDuctos, humedasBorder);
            toolTip.SetToolTip(btnDuctos, "Etiqueta ductos de climatización/ventilación con dimensiones (ancho x alto) y materialidad.");

            flowHumedas.Controls.Add(btnPendientes);
            flowHumedas.Controls.Add(btnMaterial);
            flowHumedas.Controls.Add(btnEmbebidasPlaca);
            flowHumedas.Controls.Add(btnEmbebidasPiso);
            flowHumedas.Controls.Add(btnElevadas);
            flowHumedas.Controls.Add(btnPasesViga);
            flowHumedas.Controls.Add(btnDuctos);
            pnlHumedas.Controls.Add(flowHumedas);

            // =========================================================================
            // 4. SECCIÓN HVAC
            // =========================================================================
            Panel pnlHvac = new Panel
            {
                Location = new Point(12, 310),
                Size = new Size(780, 75),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblHvac = new Label
            {
                Text = "HVAC",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(196, 70, 70),
                Location = new Point(10, 8),
                AutoSize = true
            };
            pnlHvac.Controls.Add(lblHvac);

            FlowLayoutPanel flowHvac = new FlowLayoutPanel
            {
                Location = new Point(6, 28),
                Size = new Size(765, 45),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.White
            };

            Color hvacFore = Color.FromArgb(196, 70, 70);
            Color hvacBack = Color.FromArgb(253, 240, 240);
            Color hvacBorder = Color.FromArgb(240, 175, 175);
            Color hvacHover = Color.FromArgb(250, 228, 228);

            // --- BOTÓN 1: Tags Exostos (Desplegable) ---
            ContextMenuStrip menuExostos = new ContextMenuStrip();
            ToolStripMenuItem itemExostosSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemExostosSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ExostosPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemExostosAuto = new ToolStripMenuItem("Tag Automático");
            itemExostosAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.ExostosAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuExostos.Items.Add(itemExostosSel);
            menuExostos.Items.Add(itemExostosAuto);
            ConfigureModernMenu(menuExostos, hvacFore, hvacHover);

            Button btnExostos = CreateDropdownButton("Tags Exostos", btnSize, hvacBack, hvacFore, menuExostos, hvacBorder);
            toolTip.SetToolTip(btnExostos, "Etiqueta ductos y tuberías de exostos indicando diámetro y materialidad (ej: 10\"ø - Acero galvanizado).");

            flowHvac.Controls.Add(btnExostos);
            pnlHvac.Controls.Add(flowHvac);

            // =========================================================================
            // 5. SECCIÓN GENERALES (REDES SECAS Y HÚMEDAS)
            // =========================================================================
            Panel pnlGenerales = new Panel
            {
                Location = new Point(12, 400),
                Size = new Size(780, 75),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblGenerales = new Label
            {
                Text = "GENERALES (REDES SECAS Y HÚMEDAS)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(102, 51, 153),
                Location = new Point(10, 8),
                AutoSize = true
            };
            pnlGenerales.Controls.Add(lblGenerales);

            FlowLayoutPanel flowGenerales = new FlowLayoutPanel
            {
                Location = new Point(6, 28),
                Size = new Size(765, 45),
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.White
            };

            Color generalesFore = Color.FromArgb(102, 51, 153);
            Color generalesBack = Color.FromArgb(246, 243, 252);
            Color generalesBorder = Color.FromArgb(190, 170, 225);
            Color generalesHover = Color.FromArgb(238, 230, 250);

            // --- BOTÓN 1: Sube / Baja (Desplegable unificado) ---
            ContextMenuStrip menuSubeBaja = new ContextMenuStrip();

            ToolStripMenuItem itemSubeSel = new ToolStripMenuItem("Tag SUBE (Selección Múltiple)");
            itemSubeSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagSubePorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemBajaSel = new ToolStripMenuItem("Tag BAJA (Selección Múltiple)");
            itemBajaSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagBajaPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemSubeAuto = new ToolStripMenuItem("Tag SUBE (Automático en Vista)");
            itemSubeAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagSubeAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemBajaAuto = new ToolStripMenuItem("Tag BAJA (Automático en Vista)");
            itemBajaAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagBajaAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuSubeBaja.Items.Add(itemSubeSel);
            menuSubeBaja.Items.Add(itemBajaSel);
            menuSubeBaja.Items.Add(new ToolStripSeparator());
            menuSubeBaja.Items.Add(itemSubeAuto);
            menuSubeBaja.Items.Add(itemBajaAuto);
            ConfigureModernMenu(menuSubeBaja, generalesFore, generalesHover);

            Button btnSubeBaja = CreateDropdownButton("Sube / Baja", btnSize, generalesBack, generalesFore, menuSubeBaja, generalesBorder);
            toolTip.SetToolTip(btnSubeBaja, "Coloca etiquetas 'SUBE' o 'BAJA' en accesorios y tuberías.");

            // --- BOTÓN 2: Tag Room (Desplegable) ---
            ContextMenuStrip menuTagRoom = new ContextMenuStrip();
            ToolStripMenuItem itemTagRoomSel = new ToolStripMenuItem("Por Selección Múltiple");
            itemTagRoomSel.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagRoomPorSeleccion;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            ToolStripMenuItem itemTagRoomAuto = new ToolStripMenuItem("Tag Automático");
            itemTagRoomAuto.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.TagRoomAuto;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            menuTagRoom.Items.Add(itemTagRoomSel);
            menuTagRoom.Items.Add(itemTagRoomAuto);
            ConfigureModernMenu(menuTagRoom, generalesFore, generalesHover);

            Button btnTagRoom = CreateDropdownButton("Tag Room", btnSize, generalesBack, generalesFore, menuTagRoom, generalesBorder);
            toolTip.SetToolTip(btnTagRoom, "Etiqueta habitaciones y espacios (buitrones técnicos HID, ELE, ETO, HVAC, RCI y cuartos técnicos) usando Room Tags.");

            flowGenerales.Controls.Add(btnSubeBaja);
            flowGenerales.Controls.Add(btnTagRoom);
            pnlGenerales.Controls.Add(flowGenerales);

            // Agregar secciones de contenido a pnlMain
            pnlMain.Controls.Add(pnlSecas);
            pnlMain.Controls.Add(pnlHumedas);
            pnlMain.Controls.Add(pnlHvac);
            pnlMain.Controls.Add(pnlGenerales);

            // 5. Botón Inferior Cerrar en pnlFooter
            Button btnCerrar = new Button
            {
                Text = "Cerrar",
                DialogResult = DialogResult.Cancel,
                Size = new Size(110, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(70, 70, 70),
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            btnCerrar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnCerrar, 6, Color.LightGray);
            btnCerrar.Location = new Point(pnlFooter.ClientSize.Width - btnCerrar.Width - 18, 9);
            btnCerrar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCerrar.Click += (s, e) =>
            {
                SelectedAction = TagsCoorAction.Cancelar;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            pnlFooter.Controls.Add(btnCerrar);

            this.Controls.Add(pnlMain);
            this.Controls.Add(pnlFooter);
            this.Controls.Add(pnlHeader);
            this.CancelButton = btnCerrar;

            // Recalcular posiciones y envolver botones dinámicamente al cambiar el tamaño de la ventana
            Action relayoutSections = () =>
            {
                int scrollW = pnlMain.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                int targetW = Math.Max(320, pnlMain.ClientSize.Width - scrollW - 28);

                pnlSecas.Location = new Point(12, 10);
                pnlSecas.Width = targetW;
                flowSecas.Width = Math.Max(200, pnlSecas.ClientSize.Width - 20);
                flowSecas.Height = flowSecas.GetPreferredSize(new Size(flowSecas.Width, 0)).Height;
                pnlSecas.Height = flowSecas.Bottom + 12;

                pnlHumedas.Location = new Point(12, pnlSecas.Bottom + 14);
                pnlHumedas.Width = targetW;
                flowHumedas.Width = Math.Max(200, pnlHumedas.ClientSize.Width - 20);
                flowHumedas.Height = flowHumedas.GetPreferredSize(new Size(flowHumedas.Width, 0)).Height;
                pnlHumedas.Height = flowHumedas.Bottom + 12;

                pnlHvac.Location = new Point(12, pnlHumedas.Bottom + 14);
                pnlHvac.Width = targetW;
                flowHvac.Width = Math.Max(200, pnlHvac.ClientSize.Width - 20);
                flowHvac.Height = flowHvac.GetPreferredSize(new Size(flowHvac.Width, 0)).Height;
                pnlHvac.Height = flowHvac.Bottom + 12;

                pnlGenerales.Location = new Point(12, pnlHvac.Bottom + 14);
                pnlGenerales.Width = targetW;
                flowGenerales.Width = Math.Max(200, pnlGenerales.ClientSize.Width - 20);
                flowGenerales.Height = flowGenerales.GetPreferredSize(new Size(flowGenerales.Width, 0)).Height;
                pnlGenerales.Height = flowGenerales.Bottom + 12;
            };

            pnlMain.Resize += (s, e) => relayoutSections();
            this.Shown += (s, e) => relayoutSections();
        }

        private static void ConfigureModernMenu(ContextMenuStrip menu, Color activeTextColor, Color hoverBg)
        {
            ApplyModernMenuStyle(menu, activeTextColor, hoverBg);
        }

        private static void ApplyModernMenuStyle(ToolStripDropDown dropDown, Color activeTextColor, Color hoverBg)
        {
            if (dropDown is ToolStripDropDownMenu dropMenu)
            {
                dropMenu.ShowImageMargin = false;
                dropMenu.ShowCheckMargin = false;
            }
            dropDown.BackColor = Color.White;
            dropDown.Renderer = new ModernMenuRenderer(hoverBg, activeTextColor);
            dropDown.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            dropDown.Padding = new Padding(4, 4, 4, 4);
            dropDown.AutoSize = true;
            dropDown.DropShadowEnabled = true;

            dropDown.Opened += (s, e) =>
            {
                if (dropDown.Width > 0 && dropDown.Height > 0)
                {
                    Rectangle rect = new Rectangle(0, 0, dropDown.Width, dropDown.Height);
                    using (var path = GetRoundedRectanglePath(rect, 8))
                    {
                        dropDown.Region = new Region(path);
                    }
                }
            };

            foreach (ToolStripItem item in dropDown.Items)
            {
                item.Padding = new Padding(12, 7, 12, 7);
                item.Margin = new Padding(2, 2, 2, 2);
                item.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
                item.ForeColor = Color.FromArgb(51, 65, 85);

                if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                {
                    ApplyModernMenuStyle(menuItem.DropDown, activeTextColor, hoverBg);
                }
            }
        }

        private static Button CreateModernButton(string text, Size size, Color backColor, Color foreColor, Color? borderColor = null)
        {
            Button btn = new Button
            {
                Text = text,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 3, 4, 3)
            };
            btn.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btn, 8, borderColor);
            return btn;
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
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 3, 4, 3)
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
