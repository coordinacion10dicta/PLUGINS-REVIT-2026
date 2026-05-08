using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace RevitPlugin.UI
{
    public partial class TagSelectorCielos : Window
    {
        public FamilySymbol SelectedTag { get; private set; }
        public string SelectedAction { get; private set; }

        public TagSelectorCielos(List<FamilySymbol> tags)
        {
            InitializeComponent();

            // Acciones
            ActionComboBox.Items.Add("Taggear");
            ActionComboBox.Items.Add("Acotar");
            ActionComboBox.SelectedIndex = 0;

            // Tags
            TagComboBox.ItemsSource = tags;
            TagComboBox.DisplayMemberPath = "Name";
        }

        private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string action = ActionComboBox.SelectedItem as string;

            if (action == "Acotar")
            {
                TagComboBox.Visibility = System.Windows.Visibility.Collapsed;
                TagLabel.Visibility = System.Windows.Visibility.Collapsed;

                // Limpia selección para evitar errores
                TagComboBox.SelectedItem = null;
            }
            else
            {
                TagComboBox.Visibility = System.Windows.Visibility.Visible;
                TagLabel.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void Aceptar_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ActionComboBox.SelectedItem as string;
            SelectedTag = TagComboBox.SelectedItem as FamilySymbol;

            if (SelectedAction == null)
            {
                MessageBox.Show("Seleccione una acción.");
                return;
            }

            if (SelectedAction == "Taggear" && SelectedTag == null)
            {
                MessageBox.Show("Seleccione un tag.");
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}