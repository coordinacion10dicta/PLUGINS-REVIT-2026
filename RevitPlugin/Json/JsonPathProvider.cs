using System.IO;
using System.Reflection;

namespace MiNamespace.Json
{
    public static class JsonPathProvider
    {
        private const string PluginFolderName = "RevitPlugin";

        public static string GetBasePath()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        }

        public static string GetTemplatesPath()
        {
            string templatesPath = Path.Combine(GetBasePath(), "Json", "Templates");

            if (!Directory.Exists(templatesPath))
                Directory.CreateDirectory(templatesPath);

            return templatesPath;
        }

        public static string GetRuntimePath()
        {
            string jsonPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                PluginFolderName,
                "Json");

            if (!Directory.Exists(jsonPath))
                Directory.CreateDirectory(jsonPath);

            return jsonPath;
        }

        public static string GetRuntimeFile(string fileName)
        {
            return Path.Combine(GetRuntimePath(), fileName);
        }

        public static string GetTemplateFile(string fileName)
        {
            return Path.Combine(GetTemplatesPath(), fileName);
        }
    }
}
