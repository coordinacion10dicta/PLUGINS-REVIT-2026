namespace PluginCotasExteriores.View
{
    using System.Windows;
    using System.Windows.Input;
    using Configurations;

    public partial class ExteriorConfigurationWin : Window
    {
        public ExteriorConfiguration CurrentExteriorConfiguration;

        public ExteriorConfigurationWin(ExteriorConfiguration exteriorConfiguration = null)
        {
            InitializeComponent();
            CurrentExteriorConfiguration = exteriorConfiguration ?? new ExteriorConfiguration();
        }
        
        private void ExteriorConfiguration_OnLoaded(object sender, RoutedEventArgs e)
        {
            SizeToContent = SizeToContent.Manual;
            DataContext = CurrentExteriorConfiguration;
        }

        private void BtAccept_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentExteriorConfiguration.Name))
            {
                MessageBox.Show("Ingrese un nombre para la configuración.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!CurrentExteriorConfiguration.BottomDimensions &&
                !CurrentExteriorConfiguration.LeftDimensions &&
                !CurrentExteriorConfiguration.RightDimensions &&
                !CurrentExteriorConfiguration.TopDimensions)
            {
                MessageBox.Show("Seleccione al menos una dirección de acotación.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
        }

        private void BtAddRow_OnClick(object sender, RoutedEventArgs e)
        {
            CurrentExteriorConfiguration.Chains.Add(new ExteriorDimensionChain());
        }

        private void BtDeleteSelectedChain_OnClick(object sender, RoutedEventArgs e)
        {
            var selectedChainIndex = DgChains.SelectedIndex;
            if (selectedChainIndex != -1)
                CurrentExteriorConfiguration.Chains.RemoveAt(selectedChainIndex);
        }

        private void ExteriorConfigurationWin_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
