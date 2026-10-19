using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Font = System.Drawing.Font;
using Control = System.Windows.Forms.Control;
using Panel = System.Windows.Forms.Panel;
using View = Autodesk.Revit.DB.View;
using Level = Autodesk.Revit.DB.Level;

namespace MiNamespace
{
    public enum ModoNivelReferencia
    {
        Superior,   // Techo / Losa inmediatamente superior (Cotas N. -X.XX)
        Inferior,   // Piso / Base de la vista activa (Cotas N. +X.XX)
        Manual      // Nivel del proyecto seleccionado manualmente
    }

    public enum DisplayElevationType
    {
        ActualElevation = 0,        // Actual (Selected) Elevation
        TopElevation = 1,           // Top Elevation
        BottomElevation = 2,        // Bottom Elevation
        TopAndBottomElevation = 3   // Top & Bottom Elevations
    }

    public class DisplayElevationItem
    {
        public DisplayElevationType Type { get; set; }
        public string DisplayText { get; set; }

        public DisplayElevationItem(DisplayElevationType type, string displayText)
        {
            Type = type;
            DisplayText = displayText;
        }

        public override string ToString() => DisplayText;
    }

    public class NivelComboItem
    {
        public Level Level { get; }
        public string DisplayText { get; }

        public NivelComboItem(Level level)
        {
            Level = level;
            DisplayText = level != null ? level.Name : "(Sin nivel)";
        }

        public override string ToString() => DisplayText;
    }

    public class OpcionNivelReferencia
    {
        public ModoNivelReferencia Modo { get; set; } = ModoNivelReferencia.Superior;
        public Level NivelManual { get; set; } = null;
        public string NombreNivelManual { get; set; } = "";
        public DisplayElevationType DisplayElevation { get; set; } = DisplayElevationType.BottomElevation;
        public int DisplayElevationsValue => (int)DisplayElevation;

        public string Descripcion
        {
            get
            {
                switch (Modo)
                {
                    case ModoNivelReferencia.Superior:
                        return $"Nivel Superior ({GetDisplayDesc()})";
                    case ModoNivelReferencia.Inferior:
                        return $"Nivel Inferior ({GetDisplayDesc()})";
                    case ModoNivelReferencia.Manual:
                        string nombre = NivelManual != null ? NivelManual.Name : (!string.IsNullOrWhiteSpace(NombreNivelManual) ? NombreNivelManual : "Personalizado");
                        return $"Nivel Manual: {nombre} ({GetDisplayDesc()})";
                    default:
                        return "Nivel Superior";
                }
            }
        }

        private string GetDisplayDesc()
        {
            switch (DisplayElevation)
            {
                case DisplayElevationType.ActualElevation:
                    return "Actual (Selected) Elevation";
                case DisplayElevationType.TopElevation:
                    return "Top Elevation";
                case DisplayElevationType.BottomElevation:
                    return "Bottom Elevation";
                case DisplayElevationType.TopAndBottomElevation:
                    return "Top & Bottom Elevations";
                default:
                    return "Bottom Elevation";
            }
        }
    }

    /// <summary>
    /// Modal moderno, responsivo y redimensionable manualmente para seleccionar si la cota de nivel se genera
    /// respecto al Nivel Superior, Nivel Inferior o Seleccionar Manualmente (con lista de niveles del proyecto sin duplicados y Display Elevations).
    /// </summary>
    public class NivelReferenciaSelectorWindow : System.Windows.Forms.Form
    {
        public OpcionNivelReferencia OpcionSeleccionada { get; private set; }

        private System.Windows.Forms.RadioButton rbSuperior;
        private System.Windows.Forms.RadioButton rbInferior;
        private System.Windows.Forms.RadioButton rbManual;
        private System.Windows.Forms.Panel pnlManualContent;
        private System.Windows.Forms.ComboBox cmbNiveles;
        private System.Windows.Forms.ComboBox cmbDisplayElevations;
        private System.Windows.Forms.Panel pnlCard;

        public NivelReferenciaSelectorWindow(Document doc, View activeView)
        {
            OpcionSeleccionada = new OpcionNivelReferencia();
            InitializeCustomComponents(doc, activeView);
        }

        public NivelReferenciaSelectorWindow(View activeView)
            : this(activeView?.Document, activeView)
        {
        }

        public static OpcionNivelReferencia PedirNivel(Document doc, View view)
        {
            using (var form = new NivelReferenciaSelectorWindow(doc, view))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    return form.OpcionSeleccionada;
                }
            }
            return null;
        }

        private void InitializeCustomComponents(Document doc, View activeView)
        {
            string nombreVista = activeView != null ? activeView.Name : "Vista Activa";
            string nombreNivelBase = activeView?.GenLevel != null ? activeView.GenLevel.Name : "Nivel de la Vista";

            this.Text = "DICTA - Referencia para Cotas de Nivel";
            this.Size = new Size(640, 580);
            this.MinimumSize = new Size(540, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. Header Banner
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(24, 43, 73)
            };

            Label lblTitle = new Label
            {
                Text = "COTAS DE NIVEL - REFERENCIA GENERAL",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(16, 10),
                AutoSize = true
            };

            Label lblSub = new Label
            {
                Text = string.Format("Vista activa: {0} ({1})", nombreVista, nombreNivelBase),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 220, 245),
                Location = new Point(17, 35),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);

            // 2. Footer Panel con botones de acción
            Panel pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = Color.FromArgb(242, 244, 247)
            };

            Button btnAceptar = new Button
            {
                Text = "Continuar y Acotar",
                Size = new Size(160, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 102, 204),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnAceptar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnAceptar, 8, Color.FromArgb(0, 80, 170));

            Button btnCancelar = new Button
            {
                Text = "Cancelar",
                Size = new Size(100, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(235, 238, 242),
                ForeColor = Color.FromArgb(70, 80, 95),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnCancelar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnCancelar, 8, Color.FromArgb(200, 205, 215));

            pnlFooter.Controls.Add(btnAceptar);
            pnlFooter.Controls.Add(btnCancelar);

            // Ajustar posición inicial de los botones en el footer
            void RelocateFooterButtons()
            {
                btnCancelar.Location = new Point(pnlFooter.ClientSize.Width - 116, 11);
                btnAceptar.Location = new Point(pnlFooter.ClientSize.Width - 286, 11);
            }
            pnlFooter.Resize += (s, e) => RelocateFooterButtons();
            RelocateFooterButtons();

            // 3. Main Panel con scroll responsivo
            Panel pnlMain = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(16, 16, 16, 16),
                AutoScrollMargin = new Size(0, 18)
            };

            // 4. Tarjeta blanca contenedora de opciones
            pnlCard = new Panel
            {
                Location = new Point(16, 18),
                Size = new Size(Math.Max(460, pnlMain.ClientSize.Width - 32), 215),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Opción 1: Nivel Superior (Techo / Losa)
            rbSuperior = new RadioButton
            {
                Text = "Nivel Superior (Techo / Losa Inmediata)",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 43, 73),
                Location = new Point(18, 18),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };

            Label lblDescSuperior = new Label
            {
                Text = "Referencia la cota al nivel inmediatamente superior en Z.\nGenera valores negativos relativos al techo (ej: N. -1.25 m).",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 110, 125),
                Location = new Point(38, 42),
                Size = new Size(pnlCard.ClientSize.Width - 56, 32)
            };

            // Opción 2: Nivel Inferior (Piso / Base de la Vista)
            rbInferior = new RadioButton
            {
                Text = "Nivel Inferior (Piso / Base de la Vista)",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 43, 73),
                Location = new Point(18, 86),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            Label lblDescInferior = new Label
            {
                Text = "Referencia la cota al nivel inferior / piso donde estás parado.\nGenera valores positivos sobre el piso terminado (ej: N. +2.60 m).",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 110, 125),
                Location = new Point(38, 110),
                Size = new Size(pnlCard.ClientSize.Width - 56, 32)
            };

            // Opción 3: Seleccionar manualmente
            rbManual = new RadioButton
            {
                Text = "Seleccionar manualmente (Nivel del Proyecto)",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 43, 73),
                Location = new Point(18, 154),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            Label lblDescManual = new Label
            {
                Text = "Elige manualmente el nivel de referencia del proyecto y el tipo de elevación a acotar.",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 110, 125),
                Location = new Point(38, 178),
                Size = new Size(pnlCard.ClientSize.Width - 56, 18)
            };

            // Panel desplegable de controles manuales
            pnlManualContent = new Panel
            {
                Location = new Point(38, 204),
                Size = new Size(pnlCard.ClientSize.Width - 56, 148),
                BackColor = Color.FromArgb(246, 249, 254),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            void AjustarLayout()
            {
                int scrollW = pnlMain.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                int cardW = Math.Max(460, pnlMain.ClientSize.Width - scrollW - 32);
                pnlCard.Width = cardW;
                int innerW = Math.Max(400, pnlCard.ClientSize.Width - 56);
                lblDescSuperior.Width = innerW;
                lblDescInferior.Width = innerW;
                lblDescManual.Width = innerW;
                pnlManualContent.Width = innerW;
            }

            pnlMain.Resize += (s, e) => AjustarLayout();

            Label lblNiveles = new Label
            {
                Text = "Nivel de Referencia del Proyecto (sin duplicados):",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 43, 73),
                Location = new Point(12, 10),
                AutoSize = true
            };

            cmbNiveles = new ComboBox
            {
                Location = new Point(12, 30),
                Size = new Size(pnlManualContent.ClientSize.Width - 24, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Cargar niveles del proyecto ordenados por elevación y sin duplicados
            if (doc != null)
            {
                try
                {
                    var nivelesUnicos = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Name))
                        .OrderBy(l => l.Elevation)
                        .GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();

                    foreach (var lvl in nivelesUnicos)
                    {
                        cmbNiveles.Items.Add(new NivelComboItem(lvl));
                    }

                    if (activeView?.GenLevel != null)
                    {
                        for (int i = 0; i < cmbNiveles.Items.Count; i++)
                        {
                            if (cmbNiveles.Items[i] is NivelComboItem item &&
                                (item.Level.Id == activeView.GenLevel.Id ||
                                 string.Equals(item.Level.Name, activeView.GenLevel.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                cmbNiveles.SelectedIndex = i;
                                break;
                            }
                        }
                    }

                    if (cmbNiveles.SelectedIndex < 0 && cmbNiveles.Items.Count > 0)
                    {
                        cmbNiveles.SelectedIndex = 0;
                    }
                }
                catch { }
            }

            Label lblDisplay = new Label
            {
                Text = "Display Elevations (Tipo de Elevación):",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 43, 73),
                Location = new Point(12, 68),
                AutoSize = true
            };

            cmbDisplayElevations = new ComboBox
            {
                Location = new Point(12, 88),
                Size = new Size(pnlManualContent.ClientSize.Width - 24, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Elementos idénticos a los del Options Bar de Revit (img2)
            cmbDisplayElevations.Items.Add(new DisplayElevationItem(DisplayElevationType.BottomElevation, "Bottom Elevation"));
            cmbDisplayElevations.Items.Add(new DisplayElevationItem(DisplayElevationType.TopElevation, "Top Elevation"));
            cmbDisplayElevations.Items.Add(new DisplayElevationItem(DisplayElevationType.ActualElevation, "Actual (Selected) Elevation"));
            cmbDisplayElevations.Items.Add(new DisplayElevationItem(DisplayElevationType.TopAndBottomElevation, "Top & Bottom Elevations"));
            cmbDisplayElevations.SelectedIndex = 0; // Default: Bottom Elevation

            pnlManualContent.Controls.Add(lblNiveles);
            pnlManualContent.Controls.Add(cmbNiveles);
            pnlManualContent.Controls.Add(lblDisplay);
            pnlManualContent.Controls.Add(cmbDisplayElevations);

            pnlCard.Controls.Add(rbSuperior);
            pnlCard.Controls.Add(lblDescSuperior);
            pnlCard.Controls.Add(rbInferior);
            pnlCard.Controls.Add(lblDescInferior);
            pnlCard.Controls.Add(rbManual);
            pnlCard.Controls.Add(lblDescManual);
            pnlCard.Controls.Add(pnlManualContent);

            pnlMain.Controls.Add(pnlCard);

            // Dinamismo del panel manual
            void ActualizarEstadoManual()
            {
                bool manual = rbManual.Checked;
                pnlManualContent.Visible = manual;
                pnlManualContent.Enabled = manual;
                pnlCard.Height = manual ? 372 : 215;
                AjustarLayout();
            }

            rbSuperior.CheckedChanged += (s, e) => { if (rbSuperior.Checked) ActualizarEstadoManual(); };
            rbInferior.CheckedChanged += (s, e) => { if (rbInferior.Checked) ActualizarEstadoManual(); };
            rbManual.CheckedChanged += (s, e) => ActualizarEstadoManual();
            ActualizarEstadoManual();

            // Handlers de botones
            btnAceptar.Click += (s, e) =>
            {
                if (rbSuperior.Checked)
                {
                    OpcionSeleccionada.Modo = ModoNivelReferencia.Superior;
                    OpcionSeleccionada.DisplayElevation = DisplayElevationType.BottomElevation;
                }
                else if (rbInferior.Checked)
                {
                    OpcionSeleccionada.Modo = ModoNivelReferencia.Inferior;
                    OpcionSeleccionada.DisplayElevation = DisplayElevationType.BottomElevation;
                }
                else if (rbManual.Checked)
                {
                    OpcionSeleccionada.Modo = ModoNivelReferencia.Manual;
                    if (cmbNiveles.SelectedItem is NivelComboItem nItem && nItem.Level != null)
                    {
                        OpcionSeleccionada.NivelManual = nItem.Level;
                        OpcionSeleccionada.NombreNivelManual = nItem.Level.Name;
                    }
                    if (cmbDisplayElevations.SelectedItem is DisplayElevationItem dItem)
                    {
                        OpcionSeleccionada.DisplayElevation = dItem.Type;
                    }
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancelar.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            // Agregar paneles al formulario en orden de acoplamiento correcto
            this.Controls.Add(pnlMain);
            this.Controls.Add(pnlFooter);
            this.Controls.Add(pnlHeader);

            pnlHeader.SendToBack();
            pnlFooter.SendToBack();
            pnlMain.BringToFront();

            this.Load += (s, e) =>
            {
                pnlHeader.SendToBack();
                pnlFooter.SendToBack();
                pnlMain.BringToFront();
                pnlMain.AutoScrollPosition = new Point(0, 0);
                this.ActiveControl = null;
                AjustarLayout();
            };
        }

        private static void ApplyRoundedStyle(Button btn, int radius, Color borderColor)
        {
            btn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                using (GraphicsPath path = CreateRoundedRectanglePath(rect, radius))
                {
                    btn.Region = new System.Drawing.Region(path);
                    using (Pen pen = new Pen(borderColor, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
