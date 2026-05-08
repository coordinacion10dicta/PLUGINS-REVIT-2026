using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.Attributes;

namespace MiNamespace
{
    // Comando externo de Revit que se ejecuta manualmente
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_HVAC2 : IExternalCommand
    {
        // Nombre del parámetro compartido en el EQUIPO que guarda el flujo de aire
        private const string EQUIP_SHARED_PARAM_NAME = "DC - Flujo de aire";
        // Tolerancia de comparación de flujo (ahora mismo cero → se compara casi exacto)
        private const double TOL_FLOW = 0.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Documento de usuario y documento de Revit
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ========================================================
                // 0) Preguntar qué tipo de sistema quiere auditar el usuario
                //    Suministro (Supply) o Extracción (Exhaust)
                // ========================================================
                DuctSystemType systemType;
                string systemLabelEs;

                TaskDialog td = new TaskDialog("Auditoría HVAC - Tipo de sistema");
                td.MainInstruction = "Selecciona el tipo de sistema a auditar";
                td.MainContent = "Puedes auditar sistemas de Suministro o de Extracción conectados al equipo.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Suministro (Supply Air)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Extracción (Exhaust Air)");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                td.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult choice = td.Show();
                if (choice == TaskDialogResult.CommandLink1)
                {
                    systemType = DuctSystemType.SupplyAir;
                    systemLabelEs = "Suministro";
                }
                else if (choice == TaskDialogResult.CommandLink2)
                {
                    systemType = DuctSystemType.ExhaustAir;
                    systemLabelEs = "Extracción";
                }
                else
                {
                    // Si cancela el diálogo, se cancela el comando
                    return Result.Cancelled;
                }

                // ========================================================
                // 1) El usuario selecciona el Mechanical Equipment a auditar
                // ========================================================
                Reference pickRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new MechanicalEquipmentSelectionFilter(), // filtro para solo equipos mecánicos
                    "Selecciona el Mechanical Equipment (equipo) a auditar…"
                );
                if (pickRef == null) return Result.Cancelled;

                // Se asegura que el elemento seleccionado es un FamilyInstance de MechanicalEquipment
                FamilyInstance equipment = doc.GetElement(pickRef) as FamilyInstance;
                if (equipment == null || equipment.Category == null ||
                    equipment.Category.Id.IntegerValue != (int)BuiltInCategory.OST_MechanicalEquipment)
                {
                    TaskDialog.Show("Auditoría HVAC", "El elemento seleccionado no es un Mechanical Equipment.");
                    return Result.Cancelled;
                }

                View activeView = doc.ActiveView;

                // ========================================================
                // 2) Obtener sistemas mecánicos conectados al equipo
                //    filtrados por el tipo (Supply o Exhaust) elegido
                // ========================================================
                var systems = GetConnectedMechanicalSystems(equipment, systemType);
                if (systems.Count == 0)
                {
                    TaskDialog.Show(
                        "Auditoría HVAC",
                        $"El equipo no tiene un sistema de {systemLabelEs} ({systemType}) conectado."
                    );
                    return Result.Succeeded;
                }

                // ========================================================
                // 3) Recolectar Air Terminals conectados a esos sistemas
                //    - Los MechanicalSystem.Elements pueden venir como ElementId o Element
                // ========================================================
                var airTerminals = new List<FamilyInstance>();
                foreach (var sys in systems)
                {
                    if (sys?.Elements == null) continue;

                    foreach (var obj in sys.Elements)
                    {
                        Element el = null;
                        if (obj is ElementId eid) el = doc.GetElement(eid);
                        else if (obj is Element e) el = e;

                        // Nos quedamos solo con FamilyInstance de categoría DuctTerminal (rejillas/difusores)
                        if (el is FamilyInstance fi &&
                            fi.Category != null &&
                            fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_DuctTerminal)
                        {
                            airTerminals.Add(fi);
                        }
                    }
                }

                // Eliminar duplicados por Id (por si viene de varios sistemas o rutas)
                airTerminals = airTerminals.GroupBy(x => x.Id.IntegerValue).Select(g => g.First()).ToList();

                // ========================================================
                // 4) Filtrar solo rejillas visibles en la vista activa
                //    (no interesa lo que esté en otra vista)
                // ========================================================
                var visibleTerminalIds = new HashSet<ElementId>(
                    new FilteredElementCollector(doc, activeView.Id)
                        .OfCategory(BuiltInCategory.OST_DuctTerminal)
                        .WhereElementIsNotElementType()
                        .ToElementIds(),
                    new ElementIdComparer()
                );
                airTerminals = airTerminals.Where(t => visibleTerminalIds.Contains(t.Id)).ToList();

                // ========================================================
                // 5) Obtener flujo del EQUIPO
                //    Primero intenta el parámetro compartido "DC - Flujo de aire"
                //    Si no existe o no tiene valor, hace fallback al flujo estándar
                // ========================================================
                double equipFlowVal = GetSharedParamValueDouble(equipment, EQUIP_SHARED_PARAM_NAME);
                string equipFlowDisp = GetSharedParamDisplay(equipment, EQUIP_SHARED_PARAM_NAME);
                bool usedShared = true;

                if (double.IsNaN(equipFlowVal))
                {
                    // No se encontró o no tiene valor → usamos parámetro estándar
                    usedShared = false;
                    equipFlowVal = GetFlowValue(equipment);
                    equipFlowDisp = GetFlowDisplay(equipment);
                }

                // ========================================================
                // 6) Sumar flujos de todas las rejillas conectadas y visibles
                //    y generar un listado detalle
                // ========================================================
                double sumTerminalsFlow = 0.0;
                var lines = new List<string>();
                foreach (var at in airTerminals)
                {
                    double val = GetFlowValue(at);
                    sumTerminalsFlow += val;
                    string disp = GetFlowDisplay(at);
                    lines.Add($"- {at.Name} (Id {at.Id.IntegerValue}): {disp}");
                }

                // ========================================================
                // 7) Comparar sumatoria de rejillas contra flujo del equipo
                // ========================================================
                double diff = sumTerminalsFlow - equipFlowVal;
                // Se considera “cumple” si la diferencia es prácticamente cero
                bool cumple = Math.Abs(diff) < 0.001;

                // ========================================================
                // 8) Resumen de sistemas conectados (por Id, sin duplicados)
                // ========================================================
                var sysLines = new List<string>();
                foreach (var s in systems.GroupBy(x => x.Id.IntegerValue).Select(g => g.First()))
                {
                    string sname = string.IsNullOrWhiteSpace(s.Name) ? "(Sin nombre)" : s.Name;
                    sysLines.Add($"• {sname}  [{s.SystemType}]  (Id {s.Id.IntegerValue})");
                }

                // ========================================================
                // 9) Construir el mensaje de salida con todos los datos
                // ========================================================
                var sb = new StringBuilder();
                sb.AppendLine($"Vista: {activeView?.Name}");
                sb.AppendLine($"Equipo: {equipment.Name} (Id {equipment.Id.IntegerValue})");
                sb.AppendLine($"Sistemas de {systemLabelEs} conectados:");
                foreach (var l in sysLines) sb.AppendLine(l);
                sb.AppendLine();

                if (!usedShared)
                    sb.AppendLine($"⚠ No se encontró el parámetro compartido \"{EQUIP_SHARED_PARAM_NAME}\" en el equipo. " +
                                  $"Se usa el parámetro estándar de flujo como respaldo.");

                sb.AppendLine($"Flujo del equipo ({systemLabelEs}): {equipFlowDisp}");
                sb.AppendLine($"Suma flujo de rejillas conectadas y visibles ({airTerminals.Count}): {FormatNumber(sumTerminalsFlow)}");
                sb.AppendLine($"Diferencia (Rejillas - Equipo): {FormatNumber(diff)}");
                sb.AppendLine();
                sb.AppendLine(cumple
                    ? "✅ CUMPLE: La sumatoria de rejillas coincide con el flujo del equipo."
                    : "❌ NO CUMPLE: La sumatoria de rejillas NO coincide con el flujo del equipo.");

                sb.AppendLine();
                if (airTerminals.Count > 0)
                {
                    sb.AppendLine("Detalle de rejillas (solo conectadas al sistema y visibles en la vista):");
                    foreach (var l in lines) sb.AppendLine(l);
                }
                else
                {
                    sb.AppendLine("No se encontraron rejillas (Air Terminals) conectadas y visibles en la vista.");
                }

                // Mostrar todo en un TaskDialog
                TaskDialog.Show("Auditoría HVAC", sb.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Captura cuando el usuario presiona ESC en una selección
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Cualquier otro error → mostrar mensaje y marcar como fallo
                message = $"Error en auditoría HVAC: {ex.Message}";
                return Result.Failed;
            }
        }

        // ========================================================
        // =============== MÉTODOS AUXILIARES ======================
        // ========================================================

        // Devuelve todos los MechanicalSystem conectados al equipo filtrados por DuctSystemType
        private List<MechanicalSystem> GetConnectedMechanicalSystems(
            FamilyInstance equipment,
            DuctSystemType targetType)
        {
            var systems = new List<MechanicalSystem>();

            MEPModel mepModel = equipment.MEPModel;
            if (mepModel == null) return systems;

            ConnectorManager cm = mepModel.ConnectorManager;
            if (cm == null) return systems;

            // Recorre los conectores HVAC del equipo
            foreach (Connector c in cm.Connectors)
            {
                if (c.Domain != Domain.DomainHvac) continue;

                // Si el conector pertenece a un MechanicalSystem del tipo deseado, se agrega
                if (c.MEPSystem is MechanicalSystem ms)
                {
                    if (ms.SystemType == targetType)
                        systems.Add(ms);
                }
            }

            // Eliminar sistemas duplicados por Id
            return systems.GroupBy(x => x.Id.IntegerValue).Select(g => g.First()).ToList();
        }

        // --- Parámetro compartido del EQUIPO: valor numérico (double) ---
        private double GetSharedParamValueDouble(FamilyInstance fi, string paramName)
        {
            if (fi == null || string.IsNullOrWhiteSpace(paramName)) return double.NaN;

            // Busca el parámetro por nombre exacto (ignorando mayúsculas/minúsculas y espacios)
            Parameter p = fi.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(x => x.Definition != null &&
                                     string.Equals(x.Definition.Name?.Trim(), paramName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (p == null || !p.HasValue) return double.NaN;

            // Dependiendo del tipo de almacenamiento del parámetro, se convierte a double
            switch (p.StorageType)
            {
                case StorageType.Double:
                    return p.AsDouble();

                case StorageType.Integer:
                    return p.AsInteger();

                case StorageType.String:
                    {
                        var s = p.AsString();
                        if (string.IsNullOrWhiteSpace(s)) return double.NaN;

                        // Limpieza de texto por si viene con "CFM" o comas
                        string upper = s.ToUpperInvariant();
                        upper = upper.Replace("CFM", "");
                        upper = upper.Replace(",", "");
                        upper = upper.Trim();

                        // Se intenta parsear primero con cultura invariante y luego con la actual
                        double vInv;
                        if (double.TryParse(upper, NumberStyles.Any, CultureInfo.InvariantCulture, out vInv))
                            return vInv;

                        double v;
                        if (double.TryParse(upper, out v))
                            return v;

                        return double.NaN;
                    }

                default:
                    return double.NaN;
            }
        }

        // --- Parámetro compartido del EQUIPO: display (texto formateado) ---
        private string GetSharedParamDisplay(FamilyInstance fi, string paramName)
        {
            if (fi == null || string.IsNullOrWhiteSpace(paramName)) return null;

            Parameter p = fi.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(x => x.Definition != null &&
                                     string.Equals(x.Definition.Name?.Trim(), paramName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (p != null && p.HasValue)
            {
                // Primero se intenta usar AsValueString() (respeta unidades de proyecto)
                string s = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s;

                // Si no, se arma texto simple según tipo de dato
                if (p.StorageType == StorageType.Double) return p.AsDouble().ToString("0.##");
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
                if (p.StorageType == StorageType.String) return p.AsString();
            }
            return null;
        }

        // --- Flujo estándar (para rejillas y fallback del equipo) ---
        private double GetFlowValue(FamilyInstance fi)
        {
            // Intenta primero el parámetro nativo de flujo de ductos
            Parameter p = fi.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
            if (p != null && p.HasValue) return p.AsDouble();

            // Si no existe, busca parámetros llamados "Flow" o "Flujo"
            Parameter cand = fi.Parameters.Cast<Parameter>().FirstOrDefault(x =>
                x.Definition != null &&
                (x.Definition.Name.Equals("Flow", StringComparison.OrdinalIgnoreCase) ||
                 x.Definition.Name.Equals("Flujo", StringComparison.OrdinalIgnoreCase)));

            return (cand != null && cand.HasValue) ? cand.AsDouble() : 0.0;
        }

        private string GetFlowDisplay(FamilyInstance fi)
        {
            // Display del parámetro nativo de flujo
            Parameter p = fi.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
            if (p != null && p.HasValue)
            {
                string s = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
                return FormatNumber(p.AsDouble());
            }

            // Si no, display del parámetro "Flow"/"Flujo"
            Parameter cand = fi.Parameters.Cast<Parameter>().FirstOrDefault(x =>
                x.Definition != null &&
                (x.Definition.Name.Equals("Flow", StringComparison.OrdinalIgnoreCase) ||
                 x.Definition.Name.Equals("Flujo", StringComparison.OrdinalIgnoreCase)));

            if (cand != null && cand.HasValue)
            {
                string s = cand.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
                return FormatNumber(cand.AsDouble());
            }

            return "0";
        }

        // Formatea un número con hasta 2 decimales
        private string FormatNumber(double d) => d.ToString("0.##");

        // Comparador de ElementId para poder usar HashSet<ElementId> sin problemas
        private class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y) => x?.IntegerValue == y?.IntegerValue;
            public int GetHashCode(ElementId obj) => obj?.IntegerValue.GetHashCode() ?? 0;
        }

        // Filtro de selección: solo permite escoger Mechanical Equipment
        private class MechanicalEquipmentSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem?.Category != null &&
                       elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipment;
            }

            // Se permiten todas las referencias sobre ese elemento
            public bool AllowReference(Reference reference, XYZ position) => true;
        }
    }
}
