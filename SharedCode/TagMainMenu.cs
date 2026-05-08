using System;
using System.Windows.Forms;

public class TagMainMenu : Form
{
    public enum TagOption { Alimentadores, Cajas, Codos, Cancelar }
    public TagOption SelectedOption { get; private set; } = TagOption.Cancelar;

    public TagMainMenu()
    {
        Text = "Menú de Tagueo";
        Size = new System.Drawing.Size(280, 220);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Button btnAlimentadores = new Button { Text = "Taguear Alimentadores", Width = 200, Top = 20, Left = 30 };
        Button btnCajas = new Button { Text = "Taguear Cajas de Derivación", Width = 200, Top = 60, Left = 30 };
        Button btnCodos = new Button { Text = "Taguear Codos", Width = 200, Top = 100, Left = 30 };
        Button btnCancelar = new Button { Text = "Cancelar", Width = 200, Top = 140, Left = 30 };

        btnAlimentadores.Click += (s, e) => { SelectedOption = TagOption.Alimentadores; DialogResult = DialogResult.OK; Close(); };
        btnCajas.Click += (s, e) => { SelectedOption = TagOption.Cajas; DialogResult = DialogResult.OK; Close(); };
        btnCodos.Click += (s, e) => { SelectedOption = TagOption.Codos; DialogResult = DialogResult.OK; Close(); };
        btnCancelar.Click += (s, e) => { SelectedOption = TagOption.Cancelar; DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { btnAlimentadores, btnCajas, btnCodos, btnCancelar });
    }
}
