using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MiNamespace.ValidadorParametros
{
    /// <summary>
    /// Agrega un parámetro shared a un proyecto Revit en TRES niveles:
    ///   1. Definición en archivo de parámetros compartidos (.txt)
    ///   2. Family Parameter en cada loadable family de la categoría (EditFamily + AddParameter + LoadFamily)
    ///   3. Project Parameter Binding sobre la categoría (cubre system families y elementos huérfanos)
    /// Soporta Revit 2020-2025 (net47, net48, net8.0-windows).
    /// </summary>
    public class AgregarParametroEventHandler : IExternalEventHandler
    {
        // ─── Request ─────────────────────────────────────────────────────────────
        public string Nombre    { get; set; }
        public string Categoria { get; set; }
        public string Alcance   { get; set; }   // "Tipo" | "Instancia"
        public string DllDir    { get; set; }

        // ─── Callbacks ───────────────────────────────────────────────────────────
        public Action<string> OnCompleted { get; set; }
        public Action<string> OnError     { get; set; }

        public void Execute(UIApplication uiApp)
        {
            try
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null) { InvokeUi(() => OnError?.Invoke("No hay documento activo.")); return; }

                string resultado = CrearParametroProyecto(doc, uiApp.Application);
                InvokeUi(() => OnCompleted?.Invoke(resultado));
            }
            catch (Exception ex)
            {
                InvokeUi(() => OnError?.Invoke($"Error al agregar parámetro:\n{ex.Message}"));
            }
        }

        public string GetName() => "ValidadorParametros_AgregarParametro";

        // ─── Lógica principal ────────────────────────────────────────────────────

        private string CrearParametroProyecto(Document doc, Application app)
        {
            if (string.IsNullOrWhiteSpace(Nombre))
                return "El nombre del parámetro está vacío.";

            BuiltInCategory bic = MapToBuiltInCategory(Categoria);
            if (bic == BuiltInCategory.INVALID)
                return $"Categoría '{Categoria}' no tiene un mapeo conocido.";

            Category cat = null;
            try { cat = doc.Settings.Categories.get_Item(bic); }
            catch { }
            if (cat == null)
                return $"No se encontró la categoría '{Categoria}' en el proyecto.";

            bool esTipo = Alcance?.Equals("Tipo", StringComparison.OrdinalIgnoreCase) == true;

            string sharedParamPath = Path.Combine(DllDir ?? "", "DICTA_SharedParams.txt");
            string originalSharedParam = app.SharedParametersFilename;

            try
            {
                // 1) Definición en shared param file
                ExternalDefinition def = ObtenerOCrearDefinicion(app, sharedParamPath);
                if (def == null)
                    return $"No se pudo crear la definición de '{Nombre}' en el archivo de parámetros compartidos.";

                // 2) Modificar todas las loadable families de la categoría
                var familias = BuscarFamiliasDeCategoria(doc, bic);

                int modificadas = 0;
                int yaTenian    = 0;
                int fallidas    = 0;
                var errores     = new List<string>();

                foreach (var family in familias)
                {
                    try
                    {
                        var res = AgregarParametroAFamilia(doc, family, def, esTipo);
                        if      (res == ResultadoFamilia.Modificada) modificadas++;
                        else if (res == ResultadoFamilia.YaTenia)    yaTenian++;
                        else                                          fallidas++;
                    }
                    catch (Exception ex)
                    {
                        fallidas++;
                        errores.Add($"  • {family.Name}: {Recortar(ex.Message)}");
                    }
                }

                // 3) Project Parameter Binding sobre la categoría
                string msgBinding = HacerProjectParameterBinding(doc, app, def, cat, esTipo);

                // 4) Guardar el documento
                string msgGuardado = GuardarDocumento(doc);

                // 5) Mensaje final
                return ComponerMensaje(familias.Count, modificadas, yaTenian, fallidas, errores, msgBinding, msgGuardado);
            }
            finally
            {
                app.SharedParametersFilename = originalSharedParam ?? "";
            }
        }

        // ─── Paso 1: shared parameter file ───────────────────────────────────────

        private ExternalDefinition ObtenerOCrearDefinicion(Application app, string sharedParamPath)
        {
            if (!File.Exists(sharedParamPath))
                File.WriteAllText(sharedParamPath,
                    "# This is a Revit shared parameter file.\n" +
                    "# Do not edit manually!\n" +
                    "*META\tVERSION\tMINVERSION\n" +
                    "META\t2\t1\n" +
                    "*GROUP\tID\tNAME\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\n");

            app.SharedParametersFilename = sharedParamPath;
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null) return null;

            DefinitionGroup grupo = defFile.Groups.get_Item("DICTA_Params")
                                 ?? defFile.Groups.Create("DICTA_Params");

            Definition def = grupo.Definitions.get_Item(Nombre);
            if (def == null)
            {
#if REVIT2021_OR_EARLIER
                var opts = new ExternalDefinitionCreationOptions(Nombre, ParameterType.Text) { Visible = true };
#else
                var opts = new ExternalDefinitionCreationOptions(Nombre, SpecTypeId.String.Text) { Visible = true };
#endif
                def = grupo.Definitions.Create(opts);
            }
            return def as ExternalDefinition;
        }

        // ─── Paso 2: editar familias loadable ────────────────────────────────────

        private enum ResultadoFamilia { Modificada, YaTenia, Fallida }

        private static List<Family> BuscarFamiliasDeCategoria(Document doc, BuiltInCategory bic)
        {
            var resultado = new List<Family>();
            try
            {
                var todas = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>();

                foreach (var f in todas)
                {
                    try
                    {
                        if (f?.FamilyCategory == null) continue;
                        if (!f.IsEditable) continue;

#if REVIT_LEGACY_ELEMENTID
                        long catRaw = f.FamilyCategory.Id.IntegerValue;
#else
                        long catRaw = f.FamilyCategory.Id.Value;
#endif
                        if (catRaw == (long)bic)
                            resultado.Add(f);
                    }
                    catch { }
                }
            }
            catch { }
            return resultado;
        }

        private static ResultadoFamilia AgregarParametroAFamilia(
            Document projectDoc, Family family, ExternalDefinition def, bool esTipo)
        {
            Document famDoc = null;
            try
            {
                famDoc = projectDoc.EditFamily(family);
                if (famDoc == null) return ResultadoFamilia.Fallida;

                FamilyManager fm = famDoc.FamilyManager;

                // Si ya existe parámetro con ese nombre — no duplicar
                FamilyParameter existente = null;
                try { existente = fm.get_Parameter(def.Name); } catch { }
                if (existente != null)
                {
                    try { famDoc.Close(false); } catch { }
                    famDoc = null;
                    return ResultadoFamilia.YaTenia;
                }

                using (var t = new Transaction(famDoc, $"DICTA_AddParam_{def.Name}"))
                {
                    t.Start();
#if REVIT2021_OR_EARLIER
                    fm.AddParameter(def, BuiltInParameterGroup.PG_DATA, !esTipo);
#else
                    fm.AddParameter(def, GroupTypeId.Data, !esTipo);
#endif
                    t.Commit();
                }

                // Cargar de vuelta al proyecto (sobrescribe la familia existente)
                famDoc.LoadFamily(projectDoc, new DictaFamilyLoadOptions());

                try { famDoc.Close(false); } catch { }
                famDoc = null;
                return ResultadoFamilia.Modificada;
            }
            finally
            {
                if (famDoc != null)
                {
                    try { famDoc.Close(false); } catch { }
                }
            }
        }

        // ─── Paso 3: Project Parameter Binding ───────────────────────────────────

        private static string HacerProjectParameterBinding(
            Document doc, Application app, ExternalDefinition def, Category cat, bool esTipo)
        {
            try
            {
                BindingMap map = doc.ParameterBindings;
                if (map.Contains(def))
                    return "binding ya existía";

                var catSet = new CategorySet();
                catSet.Insert(cat);

                Binding binding = esTipo
                    ? (Binding)app.Create.NewTypeBinding(catSet)
                    : app.Create.NewInstanceBinding(catSet);

                using (var t = new Transaction(doc, $"DICTA_Bind_{def.Name}"))
                {
                    t.Start();
#if REVIT2021_OR_EARLIER
                    bool ok = map.Insert(def, binding, BuiltInParameterGroup.PG_DATA);
#else
                    bool ok = map.Insert(def, binding, GroupTypeId.Data);
#endif
                    if (ok)
                    {
                        t.Commit();
                        return "binding agregado";
                    }
                    else
                    {
                        t.RollBack();
                        return "binding rechazado por Revit";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"error en binding: {Recortar(ex.Message)}";
            }
        }

        // ─── Paso 4: guardar ─────────────────────────────────────────────────────

        private static string GuardarDocumento(Document doc)
        {
            try
            {
                doc.Save(new SaveOptions { Compact = false });
                return doc.IsWorkshared
                    ? "Copia local guardada. Sincroniza con Central para publicar."
                    : "Proyecto guardado.";
            }
            catch (Exception ex)
            {
                return $"⚠ No se pudo guardar automáticamente ({Recortar(ex.Message)}). Usa Ctrl+S.";
            }
        }

        // ─── Mensaje final ───────────────────────────────────────────────────────

        private string ComponerMensaje(
            int totalFamilias, int modificadas, int yaTenian, int fallidas,
            List<string> errores, string msgBinding, string msgGuardado)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"✓ Parámetro '{Nombre}' procesado como {Alcance} en '{Categoria}'.");
            sb.AppendLine();

            if (totalFamilias > 0)
            {
                sb.AppendLine($"Loadable families de la categoría: {totalFamilias}");
                sb.AppendLine($"  • Modificadas:    {modificadas}");
                sb.AppendLine($"  • Ya lo tenían:   {yaTenian}");
                if (fallidas > 0)
                {
                    sb.AppendLine($"  • Fallidas:       {fallidas}");
                    foreach (var err in errores.Take(5))
                        sb.AppendLine(err);
                    if (errores.Count > 5)
                        sb.AppendLine($"  • … y {errores.Count - 5} más.");
                }
            }
            else
            {
                sb.AppendLine($"'{Categoria}' no tiene loadable families (system family).");
            }

            sb.AppendLine();
            sb.AppendLine($"Project Parameter Binding: {msgBinding}.");
            sb.AppendLine(msgGuardado);

            return sb.ToString().TrimEnd();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Recortar(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > 100 ? s.Substring(0, 100) + "…" : s);

        private static void InvokeUi(Action action)
        {
            try { action?.Invoke(); }
            catch { }
        }

        // ─── IFamilyLoadOptions: política al sobrescribir familia existente ──────

        private class DictaFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;   // conservar valores en instancias existentes
                return true;                         // sí, sobrescribir la definición de familia
            }

            public bool OnSharedFamilyFound(
                Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }

        // ─── Mapping ─────────────────────────────────────────────────────────────

        private static BuiltInCategory MapToBuiltInCategory(string n)
        {
            switch (n)
            {
                case "Pipes":                 return BuiltInCategory.OST_PipeCurves;
                case "Pipe Fittings":         return BuiltInCategory.OST_PipeFitting;
                case "Pipe Accessories":      return BuiltInCategory.OST_PipeAccessory;
                case "Pipe Insulations":      return BuiltInCategory.OST_PipeInsulations;
                case "Pipe Systems":          return BuiltInCategory.OST_PipingSystem;
                case "Plumbing Fixtures":     return BuiltInCategory.OST_PlumbingFixtures;
                case "Flex Pipes":            return BuiltInCategory.OST_FlexPipeCurves;
                case "Sprinklers":            return BuiltInCategory.OST_Sprinklers;
                case "Ducts":                 return BuiltInCategory.OST_DuctCurves;
                case "Duct Fittings":         return BuiltInCategory.OST_DuctFitting;
                case "Duct Accessories":      return BuiltInCategory.OST_DuctAccessory;
                case "Duct Insulations":      return BuiltInCategory.OST_DuctInsulations;
                case "Duct Systems":          return BuiltInCategory.OST_DuctSystem;
                case "Flex Ducts":            return BuiltInCategory.OST_FlexDuctCurves;
                case "Air Terminals":         return BuiltInCategory.OST_DuctTerminal;
                case "Mechanical Equipment":  return BuiltInCategory.OST_MechanicalEquipment;
                case "Electrical Equipment":  return BuiltInCategory.OST_ElectricalEquipment;
                case "Electrical Fixtures":   return BuiltInCategory.OST_ElectricalFixtures;
                case "Conduits":              return BuiltInCategory.OST_Conduit;
                case "Conduit Fittings":      return BuiltInCategory.OST_ConduitFitting;
                case "Cable Trays":           return BuiltInCategory.OST_CableTray;
                case "Cable Tray Fittings":   return BuiltInCategory.OST_CableTrayFitting;
                case "Electrical Circuits":   return BuiltInCategory.OST_ElectricalCircuit;
                case "Lighting Fixtures":     return BuiltInCategory.OST_LightingFixtures;
                case "Lighting Devices":      return BuiltInCategory.OST_LightingDevices;
                case "Communication Devices": return BuiltInCategory.OST_CommunicationDevices;
                case "Data Devices":          return BuiltInCategory.OST_DataDevices;
                case "Nurse Call Devices":    return BuiltInCategory.OST_NurseCallDevices;
                case "Telephone Devices":     return BuiltInCategory.OST_TelephoneDevices;
                case "Security Devices":      return BuiltInCategory.OST_SecurityDevices;
                case "Fire Alarm Devices":    return BuiltInCategory.OST_FireAlarmDevices;
                default:                      return BuiltInCategory.INVALID;
            }
        }
    }
}
