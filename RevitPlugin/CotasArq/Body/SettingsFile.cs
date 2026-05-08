namespace PluginCotasExteriores.Body
{
    using System;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using Configurations;

    public static class SettingsFile
    {
        private static string _settingsFile;

        /// <summary>Initialize settings file, creating it if needed</summary>
        public static void InitSettingsFile()
        {
            var configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PluginCotasExteriores",
                "DimConfigurations");
            if (!Directory.Exists(configDirectory))
                Directory.CreateDirectory(configDirectory);
            var file = Path.Combine(configDirectory, "PluginCotasExteriores.xml");
            if (!File.Exists(file))
            {
                var xElement = new XElement(Constants.XElementName_Root);
                xElement.Save(file);
            }

            _settingsFile = file;
        }

        #region Configurations

        /// <summary>Load exterior configurations from file</summary>
        public static ObservableCollection<ExteriorConfiguration> LoadExteriorConfigurations()
        {
            var configurations = new ObservableCollection<ExteriorConfiguration>();
            if (string.IsNullOrEmpty(_settingsFile))
                InitSettingsFile();
            var settingsFile = XElement.Load(_settingsFile);
            var configurationsXElement = settingsFile.Element(Constants.XElementName_ExteriorConfigurations);
            if (configurationsXElement != null)
            {
                if (configurationsXElement.Elements(Constants.XElementName_ExteriorConfiguration).Any())
                {
                    foreach (var xElement in configurationsXElement.Elements(Constants.XElementName_ExteriorConfiguration))
                        configurations.Add(ExteriorConfiguration.GetExteriorConfigurationFromXElement(xElement));
                }
            }

            return configurations;
        }

        /// <summary>Save exterior configurations to file (full overwrite)</summary>
        public static void SaveExteriorConfigurations(ObservableCollection<ExteriorConfiguration> configurations)
        {
            if (string.IsNullOrEmpty(_settingsFile))
                InitSettingsFile();
            var settingsFile = XElement.Load(_settingsFile);

            var exteriorConfigurationsXElement =
                settingsFile.Element(Constants.XElementName_ExteriorConfigurations);
            exteriorConfigurationsXElement?.Remove();
            exteriorConfigurationsXElement = new XElement(Constants.XElementName_ExteriorConfigurations);

            foreach (var configuration in configurations)
                exteriorConfigurationsXElement.Add(configuration.GetXElementFromExteriorConfigurationInstance());

            settingsFile.Add(exteriorConfigurationsXElement);

            settingsFile.Save(_settingsFile);
        }

        #endregion
    }
}
