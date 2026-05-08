using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;



public class TagSelectionWindow : Form
{
    private ComboBox comboBoxTags;
    private Button btnOk;
    private TextBox txtBuscar;
    private List<string> _originalTagNames;

    public string SelectedTagType { get; private set; }

    public TagSelectionWindow(List<string> tagTypes)
    {
        _originalTagNames = tagTypes;

        // Configuración ventana
        this.Text = "Seleccionar Tipo de Tag";
        this.Size = new Size(320, 160);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        // TextBox de búsqueda
        txtBuscar = new TextBox
        {
            Left = 20,
            Top = 15,
            Width = 260,
            
        };
        txtBuscar.TextChanged += TxtBuscar_TextChanged;

        // ComboBox
        comboBoxTags = new ComboBox
        {
            Left = 20,
            Top = 45,
            Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        comboBoxTags.Items.AddRange(_originalTagNames.ToArray());
        if (_originalTagNames.Any())
            comboBoxTags.SelectedIndex = 0;

        // Botón OK
        btnOk = new Button
        {
            Text = "Aceptar",
            Left = 110,
            Top = 80,
            Width = 80
        };
        btnOk.Click += (sender, e) =>
        {
            if (comboBoxTags.SelectedItem != null)
            {
                SelectedTagType = comboBoxTags.SelectedItem.ToString();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        };

        // Agregar controles
        this.Controls.Add(txtBuscar);
        this.Controls.Add(comboBoxTags);
        this.Controls.Add(btnOk);
    }

    private void TxtBuscar_TextChanged(object sender, EventArgs e)
    {
        string filtro = txtBuscar.Text.Trim().ToLower();

        var filtrados = _originalTagNames
            .Where(name => name.ToLower().Contains(filtro))
            .ToArray();

        comboBoxTags.Items.Clear();
        comboBoxTags.Items.AddRange(filtrados);

        if (filtrados.Length > 0)
            comboBoxTags.SelectedIndex = 0;
    }
}
