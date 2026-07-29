using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiNamespace
{
    public class OrgEjesWindow : Form
    {
        public string StartHorizontal { get; private set; }
        public string SequenceHorizontal { get; private set; }
        public bool IsHorizontalAscending { get; private set; }

        public string StartVertical { get; private set; }
        public string SequenceVertical { get; private set; }
        public bool IsVerticalAscending { get; private set; }

        public bool UseSpecificSelection { get; private set; }

        private TextBox txtStartHorizontal;
        private TextBox txtSequenceHorizontal;
        private ComboBox cmbDirHorizontal;

        private TextBox txtStartVertical;
        private TextBox txtSequenceVertical;
        private ComboBox cmbDirVertical;
        private CheckBox chkSpecificSelection;

        // Cancel Button

        private Button btnOk;
        private Button btnCancel;

        public bool HasPreSelectedGrids { get; set; }

        public OrgEjesWindow(bool hasPreSelectedGrids = false)
        {
            HasPreSelectedGrids = hasPreSelectedGrids;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Organizar Ejes";
            this.Size = new Size(480, 280);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));

            Label lblTitle = new Label();
            lblTitle.Text = "Configuración de Ejes:";
            lblTitle.Location = new Point(20, 15);
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(51, 51, 51);
            this.Controls.Add(lblTitle);

            Label lblInfo = new Label();
            lblInfo.Text = HasPreSelectedGrids 
                ? "Modo: Se organizarán SOLO los ejes seleccionados."
                : "Modo: Se organizarán TODOS los ejes de la vista actual.";
            lblInfo.Location = new Point(20, 35);
            lblInfo.AutoSize = true;
            lblInfo.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            lblInfo.ForeColor = HasPreSelectedGrids ? Color.Green : Color.DimGray;
            this.Controls.Add(lblInfo);

            // Cabecera
            int startY = 60;
            this.Controls.Add(new Label { Text = "Prefijo", Location = new Point(100, startY), AutoSize = true, ForeColor = Color.Gray });
            this.Controls.Add(new Label { Text = "Secuencia", Location = new Point(170, startY), AutoSize = true, ForeColor = Color.Gray });
            this.Controls.Add(new Label { Text = "Dirección", Location = new Point(260, startY), AutoSize = true, ForeColor = Color.Gray });

            Label lblHint = new Label();
            lblHint.Text = "Deja la secuencia vacía para omitir un eje.";
            lblHint.Location = new Point(100, startY + 18);
            lblHint.Size = new Size(300, 16);
            lblHint.TextAlign = ContentAlignment.MiddleCenter;
            lblHint.Font = new Font("Segoe UI", 7.5F, FontStyle.Italic);
            lblHint.ForeColor = Color.DimGray;

            this.Controls.Add(lblHint);

            // Horizontal
            int horizY = 95;
            Label lblHoriz = new Label { Text = "Horizontal:", Location = new Point(20, horizY + 3), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            this.Controls.Add(lblHoriz);

            txtStartHorizontal = new TextBox { Location = new Point(100, horizY), Width = 60 };
            this.Controls.Add(txtStartHorizontal);

            txtSequenceHorizontal = new TextBox { Text = "1", Location = new Point(170, horizY), Width = 60 };
            this.Controls.Add(txtSequenceHorizontal);

            cmbDirHorizontal = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(260, horizY), Width = 170 };
            cmbDirHorizontal.Items.AddRange(new string[] { "Arriba a Abajo ↓", "Abajo a Arriba ↑" });
            cmbDirHorizontal.SelectedIndex = 0;
            this.Controls.Add(cmbDirHorizontal);

            // Vertical
            int vertY = 135;
            Label lblVert = new Label { Text = "Vertical:", Location = new Point(20, vertY + 3), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            this.Controls.Add(lblVert);

            txtStartVertical = new TextBox { Location = new Point(100, vertY), Width = 60 };
            this.Controls.Add(txtStartVertical);

            txtSequenceVertical = new TextBox { Text = "A", Location = new Point(170, vertY), Width = 60 };
            this.Controls.Add(txtSequenceVertical);

            cmbDirVertical = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(260, vertY), Width = 170 };
            cmbDirVertical.Items.AddRange(new string[] { "Izquierda a Derecha →", "Derecha a Izquierda ←" });
            cmbDirVertical.SelectedIndex = 0;
            this.Controls.Add(cmbDirVertical);

            // NUEVO - Checkbox selección específica
            chkSpecificSelection = new CheckBox();
            chkSpecificSelection.Text = "Seleccionar ejes específicos en el modelo";
            chkSpecificSelection.Location = new Point(20, 165);
            chkSpecificSelection.AutoSize = true;
            this.Controls.Add(chkSpecificSelection);

            // Cancel Button
            btnCancel = new Button();
            btnCancel.Text = "Cancelar";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(260, 195);
            btnCancel.Size = new Size(90, 32);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.BackColor = Color.White;
            btnCancel.FlatAppearance.BorderColor = Color.LightGray;
            btnCancel.Cursor = Cursors.Hand;
            this.Controls.Add(btnCancel);

            // OK Button
            btnOk = new Button();
            btnOk.Text = "Organizar";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Location = new Point(360, 195);
            btnOk.Size = new Size(90, 32);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.BackColor = Color.FromArgb(0, 120, 215);
            btnOk.ForeColor = Color.White;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnOk.Cursor = Cursors.Hand;
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            StartHorizontal = txtStartHorizontal.Text.Trim();
            SequenceHorizontal = txtSequenceHorizontal.Text.Trim();
            // index 0 = "Arriba a Abajo" -> descendente en Y
            // index 1 = "Abajo a Arriba" -> ascendente en Y
            IsHorizontalAscending = cmbDirHorizontal.SelectedIndex == 1;

            StartVertical = txtStartVertical.Text.Trim();
            SequenceVertical = txtSequenceVertical.Text.Trim();
            // index 0 = "Izquierda a Derecha" -> ascendente en X
            // index 1 = "Derecha a Izquierda" -> descendente en X
            IsVerticalAscending = cmbDirVertical.SelectedIndex == 0;

            UseSpecificSelection = chkSpecificSelection.Checked;

            if (string.IsNullOrEmpty(SequenceHorizontal) && string.IsNullOrEmpty(SequenceVertical))
            {
                MessageBox.Show("Debe indicar al menos una secuencia (horizontal o vertical).", "Advertencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
            }
        }
    }
}
