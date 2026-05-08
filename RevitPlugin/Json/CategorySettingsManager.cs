using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace MiNamespace.Json
{
    public static class CategorySettingsManager
    {
        private const string FileName = "category_settings.json";

        public static void SaveCategories(string disciplina, List<BuiltInCategory> categories)
        {
            try
            {
                string path = JsonPathProvider.GetRuntimeFile(FileName);
                Dictionary<string, List<BuiltInCategory>> allSettings;

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    allSettings = JsonConvert.DeserializeObject<Dictionary<string, List<BuiltInCategory>>>(json)
                                 ?? new Dictionary<string, List<BuiltInCategory>>();
                }
                else
                {
                    allSettings = new Dictionary<string, List<BuiltInCategory>>();
                }

                allSettings[disciplina] = categories;
                File.WriteAllText(path, JsonConvert.SerializeObject(allSettings, Formatting.Indented));
            }
            catch { /* Silencioso para no interrumpir flujo */ }
        }

        public static List<BuiltInCategory> LoadCategories(string disciplina)
        {
            try
            {
                string path = JsonPathProvider.GetRuntimeFile(FileName);
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                var allSettings = JsonConvert.DeserializeObject<Dictionary<string, List<BuiltInCategory>>>(json);

                if (allSettings != null && allSettings.ContainsKey(disciplina))
                {
                    return allSettings[disciplina];
                }
            }
            catch { }

            return null;
        }
    }
}
