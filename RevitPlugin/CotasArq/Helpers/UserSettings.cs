namespace PluginCotasExteriores.Helpers
{
    using System;
    using System.IO;
    using System.Xml.Linq;

    /// <summary>
    /// Simple user settings persistence via XML file.
    /// Almacenamiento de configuraciones de usuario en XML.
    /// Settings are stored in %AppData%\PluginCotasExteriores\UserSettings.xml
    /// </summary>
    public static class UserSettings
    {
        private static readonly string SettingsDirectory;
        private static readonly string SettingsFilePath;
        private static XDocument _doc;

        static UserSettings()
        {
            SettingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PluginCotasExteriores");
            SettingsFilePath = Path.Combine(SettingsDirectory, "UserSettings.xml");
            Load();
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    _doc = XDocument.Load(SettingsFilePath);
                }
                else
                {
                    _doc = new XDocument(new XElement("Settings"));
                }
            }
            catch
            {
                _doc = new XDocument(new XElement("Settings"));
            }
        }

        /// <summary>Get a setting value by key. Returns empty string if not found.</summary>
        public static string GetValue(string key)
        {
            var root = _doc.Root;
            if (root == null) return string.Empty;
            var element = root.Element(key);
            return element?.Value ?? string.Empty;
        }

        /// <summary>Set a setting value by key.</summary>
        public static void SetValue(string key, string value)
        {
            var root = _doc.Root;
            if (root == null) return;
            var element = root.Element(key);
            if (element != null)
            {
                element.Value = value;
            }
            else
            {
                root.Add(new XElement(key, value));
            }
        }

        /// <summary>Save all settings to disk.</summary>
        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                    Directory.CreateDirectory(SettingsDirectory);
                _doc.Save(SettingsFilePath);
            }
            catch
            {
                // Silently fail - user settings are not critical
            }
        }
    }
}
