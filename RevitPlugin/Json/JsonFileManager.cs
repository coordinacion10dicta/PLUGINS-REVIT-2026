using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiNamespace.Json
{
    public static class JsonFileManager
    {
        private static readonly string[] ManagedFiles =
        {
            "hvac_config.json",
            "hidraulico_config.json",
            "rci_config.json",
            "mapping.json",
            "pending_mappings.json",
            "unknowns.json"
        };

        public static void EnsureRuntimeEnvironment()
        {
            CleanupOldRuntime();
            EnsureJsonFiles();
        }

        public static void EnsureJsonFiles()
        {
            foreach (string fileName in ManagedFiles)
            {
                EnsureRuntimeFile(fileName);
            }
        }

        public static void CleanupOldRuntime()
        {
            try
            {
                string oldPath = JsonPathProvider.GetRuntimePath();

                if (Directory.Exists(oldPath))
                {
                    // Solo borramos los config para forzar su regeneración desde las nuevas plantillas
                    foreach (var file in Directory.GetFiles(oldPath, "*config.json"))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Evita crash si el directorio viejo está en uso o bloqueado.
            }
        }

        public static T ReadJson<T>(string fileName)
        {
            try
            {
                string path = JsonPathProvider.GetRuntimeFile(fileName);

                if (!File.Exists(path))
                    return default(T);

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return default(T);

                JToken token = JToken.Parse(json, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore
                });

                return token.ToObject<T>();
            }
            catch
            {
                return default(T);
            }
        }

        public static void WriteJson<T>(string fileName, T data)
        {
            try
            {
                EnsureJsonFiles();

                string path = JsonPathProvider.GetRuntimeFile(fileName);
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Evita crash del plugin por problemas de persistencia.
            }
        }

        private static string GetDefaultHvacConfigContent()
        {
            var data = new Dictionary<string, object>
            {
                {
                    "tuberia", new Dictionary<string, object>
                    {
                        {
                            "template",
                            "Tubo de {MATERIAL} {CLASS} {DIAMETER_MM} {DIAMETER_IN} {FIG}, marca {BRAND}, Incluye: {INCLUDES}"
                        },
                        { "default_insulation", "aislamiento de espuma de poliuretano de media caña" },
                        { "default_system", "HVAC" },
                        {
                            "material_map", new Dictionary<string, string>
                            {
                                { "PVC", "PVC" },
                                { "COBRE", "cobre" }
                            }
                        },
                        {
                            "fallbacks", new Dictionary<string, string>
                            {
                                { "MATERIAL", "(MATERIAL)" },
                                { "SYSTEM", "(SISTEMA)" },
                                { "CLASS", "" },
                                { "DIAMETER_MM", "(MM)" },
                                { "DIAMETER_IN", "(IN)" },
                                { "FIG", "" },
                                { "BRAND", "(MARCA)" },
                                { "INCLUDES", "suministro e instalación" }
                            }
                        }
                    }
                },
                {
                    "ducto", new Dictionary<string, object>
                    {
                        {
                            "template",
                            "{FAMILY} a base de {MATERIAL}, para {SYSTEM}, de {SIZE}, incluye: materiales, acarreos, cortes, dobleces, desperdicios, mano de obra, instalación, equipo y herramienta."
                        },
                        { "default_system", "Ventilación Mecánica" },
                        {
                            "material_map", new Dictionary<string, string>
                            {
                                { "GALV", "lámina de acero galvanizado" },
                                { "ALUM", "lámina de aluminio" }
                            }
                        },
                        {
                            "family_map", new Dictionary<string, string>
                            {
                                { "Round Duct", "Ducto redondo" },
                                { "Rectangular Duct", "Ducto rectangular" },
                                { "Oval Duct", "Ducto ovalado" }
                            }
                        },
                        {
                            "fallbacks", new Dictionary<string, string>
                            {
                                { "FAMILY", "(NO FAMILIA)" },
                                { "SYSTEM", "(NO SISTEMA)" },
                                { "MATERIAL", "(NO MATERIAL)" },
                                { "SIZE", "(NO DIMENSIÓN)" }
                            }
                        }
                    }
                },
                {
                    "accesorio", new Dictionary<string, object>
                    {
                        { "template", "{COMPONENT} {MATERIAL} {BRAND} {MODEL} de {DIAMETER_MM} {DIAMETER_IN} {CONNECTION}, incluye: {INCLUDES}" },
                        {
                            "fallbacks", new Dictionary<string, string>
                            {
                                { "COMPONENT", "(COMPONENTE)" },
                                { "MATERIAL", "" },
                                { "BRAND", "" },
                                { "MODEL", "" },
                                { "DIAMETER_MM", "" },
                                { "DIAMETER_IN", "" },
                                { "CONNECTION", "" },
                                { "INCLUDES", "materiales, acarreos, instalación y mano de obra" }
                            }
                        }
                    }
                },
                {
                    "mechanical", new Dictionary<string, object>
                    {
                        { "template", "{DESCRIPTION} marca {BRAND}, modelo {MODEL}. Incluye: {INCLUDES}" },
                        {
                            "fallbacks", new Dictionary<string, string>
                            {
                                { "DESCRIPTION", "(NO DESCRIPCIÓN)" },
                                { "BRAND", "(NO MARCA)" },
                                { "MODEL", "(NO MODELO)" },
                                { "INCLUDES", "suministro, instalación, mano de obra" }
                            }
                        }
                    }
                },
                {
                    "conexion", new Dictionary<string, object>
                    {
                        { "template", "{COMPONENT} de {MATERIAL} {CLASS} {ANGLE} {DIAMETERS} {FIG}, marca {BRAND}, Incluye: {INCLUDES}" },
                        {
                            "fallbacks", new Dictionary<string, string>
                            {
                                { "COMPONENT", "(CONEXIÓN)" },
                                { "MATERIAL", "" },
                                { "CLASS", "" },
                                { "ANGLE", "" },
                                { "DIAMETERS", "(DIÁMETRO)" },
                                { "FIG", "" },
                                { "BRAND", "(MARCA)" },
                                { "INCLUDES", "suministro e instalación, mano de obra, pruebas, etiquetas y todo lo necesario para su ejecución" }
                            }
                        }
                    }
                }
            };

            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        private static void EnsureRuntimeFile(string fileName)
        {
            try
            {
                string runtimePath = JsonPathProvider.GetRuntimeFile(fileName);
                bool isConfig = fileName.EndsWith("config.json", StringComparison.OrdinalIgnoreCase);
                if (File.Exists(runtimePath) && !isConfig)
                    return;

                string legacyRuntimePath = Path.Combine(JsonPathProvider.GetBasePath(), "Json", fileName);
                if (File.Exists(legacyRuntimePath))
                {
                    File.Copy(legacyRuntimePath, runtimePath, true);
                    return;
                }

                string templatePath = JsonPathProvider.GetTemplateFile(fileName);
                if (File.Exists(templatePath))
                {
                    File.Copy(templatePath, runtimePath, true);
                    return;
                }

                File.WriteAllText(runtimePath, GetFallbackContent(fileName));
            }
            catch
            {
                // Evita que el plugin falle al inicializar archivos runtime.
            }
        }

        private static string GetFallbackContent(string fileName)
        {
            if (string.Equals(fileName, "hvac_config.json", StringComparison.OrdinalIgnoreCase))
                return GetDefaultHvacConfigContent();

            if (string.Equals(fileName, "mapping.json", StringComparison.OrdinalIgnoreCase))
                return "{\n  \"mappings\": []\n}";

            if (string.Equals(fileName, "pending_mappings.json", StringComparison.OrdinalIgnoreCase))
                return "{\n  \"pending\": []\n}";

            if (string.Equals(fileName, "unknowns.json", StringComparison.OrdinalIgnoreCase))
                return "{\n  \"unknowns\": []\n}";

            if (fileName.EndsWith("config.json", StringComparison.OrdinalIgnoreCase))
                return GetDefaultHvacConfigContent();

            return "{}";
        }
    }
}
