using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace MiNamespace.ValidadorParametros
{
    public static class DisciplinasRepository
    {
        private static string _jsonPath;
        private static DisciplinasRoot _data;

        public static void Initialize(string jsonPath)
        {
            _jsonPath = jsonPath;
            _data = Load(jsonPath);
        }

        public static DisciplinasRoot GetData() => _data;

        public static List<DisciplinaConfig> GetDisciplinas() => _data.Disciplinas;

        public static List<string> GetNombresDisciplinas()
            => _data.Disciplinas.Select(d => d.Nombre).OrderBy(n => n).ToList();

        public static void Save()
        {
            var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            File.WriteAllText(_jsonPath, json, Encoding.UTF8);
        }

        // Convierte la estructura jerárquica a la lista plana que usa ParameterValidator
        public static List<DisciplineParameter> ToDisciplineParameters()
        {
            var result = new List<DisciplineParameter>();
            foreach (var disc in _data.Disciplinas)
                foreach (var cat in disc.Categorias)
                    foreach (var param in cat.Parametros)
                        result.Add(new DisciplineParameter
                        {
                            Disciplina      = disc.Nombre,
                            Categoria       = cat.Nombre,
                            NombreParametro = param.Nombre,
                            Alcance         = string.IsNullOrWhiteSpace(param.Alcance) ? "Instancia" : param.Alcance,
                            Obligatorio     = param.Obligatorio,
                            Descripcion     = param.Descripcion ?? ""
                        });
            return result;
        }

        // ─── CRUD Disciplinas ────────────────────────────────────────────────────

        public static DisciplinaConfig AddDisciplina(string nombre)
        {
            var disc = new DisciplinaConfig { Nombre = nombre, Categorias = new List<CategoriaConfig>() };
            _data.Disciplinas.Add(disc);
            Save();
            return disc;
        }

        public static void RemoveDisciplina(string nombre)
        {
            _data.Disciplinas.RemoveAll(d => d.Nombre == nombre);
            Save();
        }

        public static void RenameDisciplina(string nombreActual, string nombreNuevo)
        {
            var disc = _data.Disciplinas.FirstOrDefault(d => d.Nombre == nombreActual);
            if (disc != null) { disc.Nombre = nombreNuevo; Save(); }
        }

        // ─── CRUD Categorías ─────────────────────────────────────────────────────

        public static CategoriaConfig AddCategoria(string disciplina, string nombre)
        {
            var disc = _data.Disciplinas.FirstOrDefault(d => d.Nombre == disciplina);
            if (disc == null) return null;
            var cat = new CategoriaConfig { Nombre = nombre, Parametros = new List<ParametroConfig>() };
            disc.Categorias.Add(cat);
            Save();
            return cat;
        }

        public static void RemoveCategoria(string disciplina, string categoria)
        {
            var disc = _data.Disciplinas.FirstOrDefault(d => d.Nombre == disciplina);
            disc?.Categorias.RemoveAll(c => c.Nombre == categoria);
            Save();
        }

        public static void RenameCategoria(string disciplina, string nombreActual, string nombreNuevo)
        {
            var cat = _data.Disciplinas.FirstOrDefault(d => d.Nombre == disciplina)
                          ?.Categorias.FirstOrDefault(c => c.Nombre == nombreActual);
            if (cat != null) { cat.Nombre = nombreNuevo; Save(); }
        }

        // ─── CRUD Parámetros ─────────────────────────────────────────────────────

        public static ParametroConfig AddParametro(string disciplina, string categoria, ParametroConfig param)
        {
            var cat = _data.Disciplinas.FirstOrDefault(d => d.Nombre == disciplina)
                          ?.Categorias.FirstOrDefault(c => c.Nombre == categoria);
            if (cat == null) return null;
            cat.Parametros.Add(param);
            Save();
            return param;
        }

        public static void RemoveParametro(string disciplina, string categoria, string nombreParam)
        {
            var cat = _data.Disciplinas.FirstOrDefault(d => d.Nombre == disciplina)
                          ?.Categorias.FirstOrDefault(c => c.Nombre == categoria);
            cat?.Parametros.RemoveAll(p => p.Nombre == nombreParam);
            Save();
        }

        // ─── Carga inicial ───────────────────────────────────────────────────────

        private static DisciplinasRoot Load(string path)
        {
            DisciplinasRoot root = null;

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    root = JsonConvert.DeserializeObject<DisciplinasRoot>(json);
                }
                catch { }
            }

            if (root == null)
                root = new DisciplinasRoot { Disciplinas = new List<DisciplinaConfig>() };

            // Asegura que las disciplinas del catálogo existen; agrega las faltantes con sus categorías por defecto.
            foreach (string nombre in DisciplinaCatalogo.Disciplinas)
            {
                if (!root.Disciplinas.Any(d => d.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
                    root.Disciplinas.Add(BuildDefault(nombre));
            }

            var json2 = JsonConvert.SerializeObject(root, Formatting.Indented);
            File.WriteAllText(path, json2, Encoding.UTF8);
            return root;
        }

        private static DisciplinaConfig BuildDefault(string nombre)
        {
            var cats = DisciplinaCatalogo.GetCategorias(nombre)
                .Select(c => new CategoriaConfig { Nombre = c, Parametros = new List<ParametroConfig>() })
                .ToList();
            return new DisciplinaConfig { Nombre = nombre, Categorias = cats };
        }

        private static List<DisciplinaConfig> BuildDefaults()
        {
            return DisciplinaCatalogo.Disciplinas.Select(BuildDefault).ToList();
        }
    }
}
