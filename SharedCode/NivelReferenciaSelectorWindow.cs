using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace MiNamespace
{
    public enum ModoNivelReferencia
    {
        Superior,   // Techo / Losa inmediatamente superior (Cotas N. -X.XX)
        Inferior    // Piso / Base de la vista activa (Cotas N. +X.XX)
    }

    public class OpcionNivelReferencia
    {
        public ModoNivelReferencia Modo { get; set; } = ModoNivelReferencia.Superior;
        public string Descripcion
        {
            get
            {
                switch (Modo)
                {
                    case ModoNivelReferencia.Superior:
                        return "Nivel Superior - Techo/Losa";
                    case ModoNivelReferencia.Inferior:
                        return "Nivel Inferior - Piso/Base";
                    default:
                        return "Nivel Superior";
                }
            }
        }
    }

    /// <summary>
    /// Modal moderno y limpio para seleccionar si la cota de nivel se genera
    /// respecto al Nivel Superior (techo/losa) o al Nivel Inferior (piso de la vista activa).
    /// </summary>
    public class NivelReferenciaSelectorWindow : System.Windows.Forms.Form
    {
        public OpcionNivelReferencia OpcionSeleccionada { get; private set; }

        private System.Windows.Forms.RadioButton rbSuperior;
        private System.Windows.Forms.RadioButton rbInferior;

        public NivelReferenciaSelectorWindow(Autodesk.Revit.DB.View activeView)
        {
            OpcionSeleccionada = new OpcionNivelReferencia();
            InitializeCustomComponents(activeView);
        }

        public static OpcionNivelReferencia PedirNivel(Document doc, Autodesk.Revit.DB.View view)
        {
            using (var form = new NivelReferenciaSelectorWindow(view))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    return form.OpcionSeleccionada;
                }
            }
            return null;
        }

        private void InitializeCustomComponents(Autodesk.Revit.DB.View activeView)
        {
            string nombreVista = activeView != null ? activeView.Name : "Vista Activa";
            string nombreNivelBase = activeView?.GenLevel != null ? activeView.GenLevel.Name : "Nivel de la Vista";

            this.Text = "DICTA - Referencia para Cotas de Nivel";
            this.Size = new System.Drawing.Size(510, 340);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = System.Drawing.Color.FromArgb(248, 249, 250);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. Header Banner
            System.Windows.Forms.Panel pnlHeader = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = System.Drawing.Color.FromArgb(24, 43, 73)
            };

            System.Windows.Forms.Label lblTitle = new System.Windows.Forms.Label
            {
                Text = "COTAS DE NIVEL - REFERENCIA GENERAL",
                Font = new System.Drawing.Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                Location = new System.Drawing.Point(16, 9),
                AutoSize = true
            };

            System.Windows.Forms.Label lblSub = new System.Windows.Forms.Label
            {
                Text = string.Format("Vista activa: {0} ({1})", nombreVista, nombreNivelBase),
                Font = new System.Drawing.Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = System.Drawing.Color.FromArgb(200, 220, 245),
                Location = new System.Drawing.Point(17, 34),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);
            this.Controls.Add(pnlHeader);

            // 2. Contenedor Central de Opciones
            System.Windows.Forms.Panel pnlBody = new System.Windows.Forms.Panel
            {
                Location = new System.Drawing.Point(16, 76),
                Size = new System.Drawing.Size(462, 160),
                BackColor = System.Drawing.Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Opción 1: Nivel Superior (Techo / Losa)
            rbSuperior = new System.Windows.Forms.RadioButton
            {
                Text = "Nivel Superior (Techo / Losa Inmediata)",
                Font = new System.Drawing.Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(24, 43, 73),
                Location = new System.Drawing.Point(18, 16),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };

            System.Windows.Forms.Label lblDescSuperior = new System.Windows.Forms.Label
            {
                Text = "Referencia la cota al nivel inmediatamente superior en Z.\nGenera valores negativos relativos al techo (ej: N. -1.25 m).",
                Font = new System.Drawing.Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = System.Drawing.Color.FromArgb(100, 110, 125),
                Location = new System.Drawing.Point(38, 40),
                Size = new System.Drawing.Size(405, 30)
            };

            // Opción 2: Nivel Inferior (Piso / Base de la Vista)
            rbInferior = new System.Windows.Forms.RadioButton
            {
                Text = "Nivel Inferior (Piso / Base de la Vista)",
                Font = new System.Drawing.Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(24, 43, 73),
                Location = new System.Drawing.Point(18, 82),
                AutoSize = true,
                Cursor = Cursors.Hand
            };

            System.Windows.Forms.Label lblDescInferior = new System.Windows.Forms.Label
            {
                Text = "Referencia la cota al nivel inferior / piso donde estás parado.\nGenera valores positivos sobre el piso terminado (ej: N. +2.60 m).",
                Font = new System.Drawing.Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = System.Drawing.Color.FromArgb(100, 110, 125),
                Location = new System.Drawing.Point(38, 106),
                Size = new System.Drawing.Size(405, 30)
            };

            pnlBody.Controls.Add(rbSuperior);
            pnlBody.Controls.Add(lblDescSuperior);
            pnlBody.Controls.Add(rbInferior);
            pnlBody.Controls.Add(lblDescInferior);
            this.Controls.Add(pnlBody);

            // 3. Botones Inferiores (Aceptar / Cancelar)
            System.Windows.Forms.Button btnAceptar = new System.Windows.Forms.Button
            {
                Text = "Continuar y Acotar",
                Size = new System.Drawing.Size(160, 36),
                Location = new System.Drawing.Point(165, 248),
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(0, 102, 204),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAceptar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnAceptar, 8, System.Drawing.Color.FromArgb(0, 80, 170));

            System.Windows.Forms.Button btnCancelar = new System.Windows.Forms.Button
            {
                Text = "Cancelar",
                Size = new System.Drawing.Size(100, 36),
                Location = new System.Drawing.Point(335, 248),
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(235, 238, 242),
                ForeColor = System.Drawing.Color.FromArgb(70, 80, 95),
                Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            btnCancelar.FlatAppearance.BorderSize = 0;
            ApplyRoundedStyle(btnCancelar, 8, System.Drawing.Color.FromArgb(200, 205, 215));

            btnAceptar.Click += (s, e) =>
            {
                if (rbSuperior.Checked)
                {
                    OpcionSeleccionada.Modo = ModoNivelReferencia.Superior;
                }
                else if (rbInferior.Checked)
                {
                    OpcionSeleccionada.Modo = ModoNivelReferencia.Inferior;
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancelar.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(btnAceptar);
            this.Controls.Add(btnCancelar);
        }

        private static void ApplyRoundedStyle(System.Windows.Forms.Button btn, int radius, System.Drawing.Color borderColor)
        {
            btn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                using (GraphicsPath path = CreateRoundedRectanglePath(rect, radius))
                {
                    btn.Region = new Region(path);
                    using (System.Drawing.Pen pen = new System.Drawing.Pen(borderColor, 1))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };
        }

        private static GraphicsPath CreateRoundedRectanglePath(System.Drawing.Rectangle rect, int radius)
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
