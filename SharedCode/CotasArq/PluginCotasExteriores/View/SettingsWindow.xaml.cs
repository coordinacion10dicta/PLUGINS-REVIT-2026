namespace PluginCotasExteriores.View
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using Body;
    using Configurations;
    using Helpers;

    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private ObservableCollection<ExteriorConfiguration> _exteriorConfigurations;

        private void SettingsWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            GetExteriorConfigurations();

            // load settings
            var minWidthStr = UserSettings.GetValue("ExteriorFaceMinWidthBetween");
            var minWidthSetting = int.TryParse(minWidthStr, out int m) ? m : 100;
            TbExteriorFaceMinWidthBetween.Text = minWidthSetting.ToString();

            var removeStr = UserSettings.GetValue("ExteriorMinWidthFaceRemove");
            CbExteriorMinWidthFaceRemove.SelectedIndex = int.TryParse(removeStr, out m) ? m : 0;

            var minWallStr = UserSettings.GetValue("MinWallWidth");
            var minWallWidth = int.TryParse(minWallStr, out m) ? m : 50;
            TbMinWallWidth.Text = minWallWidth.ToString();
        }

        private void GetExteriorConfigurations()
        {
            try
            {
                var defConfigStr = UserSettings.GetValue("DefaultExteriorConfiguration");
                var defConfig = Guid.TryParse(defConfigStr, out Guid g) ? g : Guid.Empty;
                if (defConfig == Guid.Empty)
                {
                    BtDeleteExteriorConfiguration.IsEnabled = false;
                    BtEditExteriorConfiguration.IsEnabled = false;
                    _exteriorConfigurations = new ObservableCollection<ExteriorConfiguration>();
                    return;
                }

                _exteriorConfigurations = SettingsFile.LoadExteriorConfigurations();
                CbExteriorConfigurations.ItemsSource = _exteriorConfigurations;
                var index = 0;
                for (var i = 0; i < _exteriorConfigurations.Count; i++)
                {
                    if (_exteriorConfigurations[i].Id.Equals(defConfig))
                    {
                        index = i;
                        break;
                    }
                }

                CbExteriorConfigurations.SelectedIndex = index;
                if (!_exteriorConfigurations.Any())
                {
                    BtDeleteExteriorConfiguration.IsEnabled = false;
                    BtEditExteriorConfiguration.IsEnabled = false;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CbExteriorConfigurations_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
                return;
            UserSettings.SetValue("DefaultExteriorConfiguration",
                ((ExteriorConfiguration)e.AddedItems[0]).Id.ToString());
            UserSettings.Save();
        }

        // Add new Exterior Configuration
        private void BtAddNewExteriorConfiguration_OnClick(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                ExteriorConfigurationWin win = new ExteriorConfigurationWin { Title = "Nueva Configuración" };
                var result = win.ShowDialog();
                if (result == true)
                {
                    _exteriorConfigurations.Add(win.CurrentExteriorConfiguration);
                    CbExteriorConfigurations.ItemsSource = _exteriorConfigurations;
                    CbExteriorConfigurations.SelectedIndex = CbExteriorConfigurations.Items.Count - 1;
                    SettingsFile.SaveExteriorConfigurations(_exteriorConfigurations);
                    BtDeleteExteriorConfiguration.IsEnabled = true;
                    BtEditExteriorConfiguration.IsEnabled = true;
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowDialog();
            }
        }

        // Edit Exterior Configuration
        private void BtEditExteriorConfiguration_OnClick(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                if (CbExteriorConfigurations.SelectedIndex == -1)
                    return;
                var selected = (ExteriorConfiguration)CbExteriorConfigurations.SelectedItem;
                var selectedIndex = CbExteriorConfigurations.SelectedIndex;

                ExteriorConfigurationWin win = new ExteriorConfigurationWin(selected) { Title = "Editar Configuración" };
                var result = win.ShowDialog();
                if (result == true)
                {
                    _exteriorConfigurations.RemoveAt(selectedIndex);
                    _exteriorConfigurations.Insert(selectedIndex, win.CurrentExteriorConfiguration);
                    CbExteriorConfigurations.ItemsSource = _exteriorConfigurations;
                    CbExteriorConfigurations.SelectedIndex = selectedIndex;
                    SettingsFile.SaveExteriorConfigurations(_exteriorConfigurations);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowDialog();
            }
        }

        // delete configuration
        private void BtDeleteExteriorConfiguration_OnClick(object sender, RoutedEventArgs e)
        {
            if (CbExteriorConfigurations.SelectedIndex == -1)
                return;
            var selected = (ExteriorConfiguration)CbExteriorConfigurations.SelectedItem;
            var selectedIndex = CbExteriorConfigurations.SelectedIndex;
            if (MessageBox.Show(
                    "¿Eliminar la configuración \"" + selected.Name + "\"?\n\nEsta acción no se puede deshacer.",
                    "Atención",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _exteriorConfigurations.RemoveAt(selectedIndex);
                CbExteriorConfigurations.ItemsSource = _exteriorConfigurations;
                if (selectedIndex >= 1)
                    CbExteriorConfigurations.SelectedIndex = selectedIndex - 1;
                SettingsFile.SaveExteriorConfigurations(_exteriorConfigurations);
                if (!_exteriorConfigurations.Any())
                {
                    BtDeleteExteriorConfiguration.IsEnabled = false;
                    BtEditExteriorConfiguration.IsEnabled = false;
                }
            }
        }
        
        // only integers
        private void TextBoxBase_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;

            var newText = tb?.Text;
            if (string.IsNullOrEmpty(newText))
                return;

            if (!int.TryParse(newText, out int _))
                newText = newText.Remove(newText.Length - 1);

            tb.Text = newText;
            tb.CaretIndex = newText.Length;
        }

        private void SettingsWindow_OnClosed(object sender, EventArgs e)
        {
            // save settings
            UserSettings.SetValue("ExteriorFaceMinWidthBetween", TbExteriorFaceMinWidthBetween.Text);
            UserSettings.SetValue("MinWallWidth", TbMinWallWidth.Text);
            UserSettings.SetValue("ExteriorMinWidthFaceRemove", CbExteriorMinWidthFaceRemove.SelectedIndex.ToString());
            UserSettings.Save();
        }
    }
}
