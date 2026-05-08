#if REVIT2020
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2021
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2022
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2023
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2024
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif
#if REVIT2025
#define REVIT
using Autodesk.Revit.ApplicationServices;
#endif

using System;
using System.Globalization;
using System.Linq;

// Alias para evitar choques con Revit UI
using WF = System.Windows.Forms;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class MyAraña : IExternalCommand
    {
        private const double SlopePercent = 0.01; // 1%

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ---- Niveles ----
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                if (!levels.Any()) { message = "No hay niveles en el proyecto."; return Result.Failed; }

                string[] levelDisplay = levels.Select(l => $"{l.Name} ({FeetToMeters(l.Elevation):0.###} m)").ToArray();

                // ---- Bucle de menú principal (permite volver) ----
                while (true)
                {
                    using (var menu = new MainMenuForm())
                    {
                        var r = menu.ShowDialog();
                        if (r != WF.DialogResult.OK) return Result.Cancelled;

                        switch (menu.Selection)
                        {
                            case MainMenuForm.MainChoice.Pipe:
                                {
                                    using (var dlg = new PipeParamsForm(levelDisplay))
                                    {
                                        var pr = dlg.ShowDialog();
                                        if (pr == WF.DialogResult.Retry) continue;           // Volver al menú
                                        if (pr != WF.DialogResult.OK) return Result.Cancelled;

                                        // ----- Conversión a unidades internas -----
                                        double height_ft = MetersToFeet(dlg.AlturaMetros);
                                        double length_ft = MetersToFeet(dlg.LongitudMetros);
                                        double diameter_ft = InchesToFeet(dlg.DiametroPulgadas);
                                        double offset_ft = MetersToFeet(dlg.DesfaseMetros);
                                        XYZ dirXY = DirToVector(dlg.DireccionElegida);
                                        Level level = levels[Math.Max(0, Math.Min(dlg.SelectedLevelIndex, levels.Count - 1))];
                                        var tipo = dlg.TipoElegido;

                                        // ----- Pick point (xy); z = nivel + desfase -----
                                        XYZ pick;
                                        try
                                        {
                                            pick = uidoc.Selection.PickPoint(
                                                ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest,
                                                "Elige el punto en planta del codo/sifón (Z = Nivel + Desfase).");
                                        }
                                        catch { return Result.Cancelled; }

                                        double elbowZ = level.Elevation + offset_ft;
                                        XYZ elbowPoint = new XYZ(pick.X, pick.Y, elbowZ);

                                        // Tipos
                                        var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
                                        if (pipeType == null) { message = "No se encontró un PipeType."; return Result.Failed; }

                                        var systemType = new FilteredElementCollector(doc)
                                            .OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>()
                                            .FirstOrDefault(st => st.Name.ToLower().Contains("sanit"))
                                            ?? new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault();
                                        if (systemType == null) { message = "No se encontró un PipingSystemType."; return Result.Failed; }

                                        using (var t = new Transaction(doc, "Araña: recta / sifón (nivel + desfase + dirección)"))
                                        {
                                            t.Start();

                                            // Vertical base
                                            XYZ vStart = elbowPoint;
                                            XYZ vEnd = elbowPoint + new XYZ(0, 0, height_ft);
                                            Pipe vertical = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, vEnd, vStart);
                                            SetDiameter(vertical, diameter_ft);

                                            if (tipo == PipeParamsForm.TipoConexion.Recta)
                                            {
                                                double dropFt = length_ft * SlopePercent;
                                                XYZ mainEnd = elbowPoint + dirXY.Multiply(length_ft) + new XYZ(0, 0, -dropFt);

                                                Pipe horizontal = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, elbowPoint, mainEnd);
                                                SetDiameter(horizontal, diameter_ft);
                                                TrySetSlopeParameter(horizontal, SlopePercent);

                                                var vc = GetEndAt(vertical, elbowPoint);
                                                var hc = GetEndAt(horizontal, elbowPoint);
                                                if (vc == null || hc == null) throw new Exception("No se hallaron conectores para el codo.");
                                                doc.Create.NewElbowFitting(vc, hc);
                                            }
                                            else // ---------- SIFÓN ----------
                                            {
                                                FamilySymbol trapSym = FindTrapSymbol(doc, new[]
                                                { "Trap P - PVC - Sch 40 - DWV", "Trap P", "P-Trap", "Trap" });
                                                if (trapSym == null)
                                                    throw new Exception("No encontré la familia del sifón (Trap P...). Cárgala e inténtalo de nuevo.");
                                                if (!trapSym.IsActive) { trapSym.Activate(); doc.Regenerate(); }

                                                // Colocar
                                                FamilyInstance trap = doc.Create.NewFamilyInstance(elbowPoint, trapSym, level, StructuralType.NonStructural);

                                                // 1) Poner "de pie" con eje horizontal ⟂ a dir
                                                EnsureTrapVertical(doc, trap, elbowPoint, dirXY);
                                                doc.Regenerate();

                                                // 2) Centrar el trap: mover SU CONECTOR VERTICAL al elbowPoint
                                                if (!CenterTrapOnVConnector(doc, trap, elbowPoint, dirXY, out Connector trapH, out Connector trapV))
                                                    throw new Exception("No pude localizar el conector vertical del sifón.");
                                                doc.Regenerate();

                                                // 3) Elegir automáticamente la mejor de 4 poses (0/90/180/270) en planta
                                                if (!OrientTrapBestQuadrant(doc, trap, elbowPoint, dirXY, out trapH, out trapV))
                                                    throw new Exception("No pude orientar el sifón correctamente.");
                                                doc.Regenerate();

                                                // 4) Reconstruir vertical pura y conectar
                                                RebuildVerticalAt(vertical, trapV.Origin, height_ft);
                                                SetDiameter(vertical, diameter_ft);
                                                GetEndAt(vertical, trapV.Origin)?.ConnectTo(trapV);

                                                // 5) Tramo principal 1%
                                                double dropFt = length_ft * SlopePercent;
                                                XYZ mainEnd = trapH.Origin + dirXY.Multiply(length_ft) + new XYZ(0, 0, -dropFt);

                                                Pipe main = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, trapH.Origin, mainEnd);
                                                SetDiameter(main, diameter_ft);
                                                TrySetSlopeParameter(main, SlopePercent);
                                                GetEndAt(main, trapH.Origin)?.ConnectTo(trapH);
                                            }

                                            t.Commit();
                                        }

                                        return Result.Succeeded;
                                    }
                                }

                            case MainMenuForm.MainChoice.Future:
                                {
                                    using (var fut = new FutureFeatureForm())
                                    {
                                        var fr = fut.ShowDialog();
                                        if (fr == WF.DialogResult.Retry) continue; // volver al menú
                                        return Result.Cancelled;                   // cancelar plugin
                                    }
                                }

                            case MainMenuForm.MainChoice.Cancel:
                            default:
                                return Result.Cancelled;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ----------------- Utilidades generales -----------------
        private static Connector GetEndAt(MEPCurve curve, XYZ nearPoint)
        {
            Connector best = null; double min = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(nearPoint);
                if (d < min) { min = d; best = c; }
            }
            return best;
        }

        private static void TrySetSlopeParameter(Pipe pipe, double slopeAsPercent)
        {
            var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
            if (p != null && !p.IsReadOnly) p.Set(slopeAsPercent);
        }

        private static void SetDiameter(Pipe pipe, double diameterFeet)
        {
            var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (p != null && !p.IsReadOnly) p.Set(diameterFeet);
        }

        private static double MetersToFeet(double m) => m * 3.280839895;
        private static double FeetToMeters(double f) => f / 3.280839895;
        private static double InchesToFeet(double inch) => inch / 12.0;

        private static XYZ DirToVector(PipeParamsForm.DirOption opt)
        {
            switch (opt)
            {
                case PipeParamsForm.DirOption.NorteSur: return new XYZ(0, -1, 0);
                case PipeParamsForm.DirOption.SurNorte: return new XYZ(0, 1, 0);
                case PipeParamsForm.DirOption.OccidenteOriente: return new XYZ(1, 0, 0);
                case PipeParamsForm.DirOption.OrienteOccidente: return new XYZ(-1, 0, 0);
                default: return new XYZ(1, 0, 0);
            }
        }

        // Busca el símbolo de la familia del sifón (Trap P)
        private static FamilySymbol FindTrapSymbol(Document doc, string[] nameHints)
        {
            var allSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();

            bool IsPreferred(FamilySymbol fs) =>
                fs.Category != null && (
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting ||
                    fs.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PlumbingFixtures);

            var preferred = allSymbols.Where(IsPreferred).ToList();

            foreach (string hint in nameHints)
            {
                var s = preferred.FirstOrDefault(fs =>
                    fs.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fs.FamilyName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (s != null) return s;
            }

            foreach (string hint in nameHints)
            {
                var s = allSymbols.FirstOrDefault(fs =>
                    fs.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fs.FamilyName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (s != null) return s;
            }

            var byWordPreferred = preferred.FirstOrDefault(fs =>
                fs.Name.ToLower().Contains("trap") || fs.FamilyName.ToLower().Contains("trap"));
            if (byWordPreferred != null) return byWordPreferred;

            return allSymbols.FirstOrDefault(fs =>
                fs.Name.ToLower().Contains("trap") || fs.FamilyName.ToLower().Contains("trap"));
        }

        // ----------------- SIFÓN: orientación robusta -----------------
        private static bool CenterTrapOnVConnector(Document doc, FamilyInstance trap, XYZ target,
                                                   XYZ dirXY, out Connector trapH, out Connector trapV)
        {
            trapH = null; trapV = null;
            if (!TryGetTrapConnectors(trap, dirXY, out trapH, out trapV)) return false;

            XYZ delta = target - trapV.Origin;
            if (delta.GetLength() > 1e-6)
            {
                ElementTransformUtils.MoveElement(doc, trap.Id, delta);
                doc.Regenerate();
                TryGetTrapConnectors(trap, dirXY, out trapH, out trapV);
            }
            return true;
        }

        private static bool IsTrapNowVertical(FamilyInstance trap, double threshold = 0.7)
        {
            var cm = trap.MEPModel?.ConnectorManager?.Connectors;
            if (cm == null) return false;

            double maxZ = 0.0;
            foreach (Connector c in cm)
            {
                var best = GetBestAxisForZ(c.CoordinateSystem);
                maxZ = Math.Max(maxZ, Math.Abs(best.Z));
            }
            return maxZ >= threshold;
        }

        private static void EnsureTrapVertical(Document doc, FamilyInstance trap, XYZ origin, XYZ dirXY)
        {
            XYZ axis = dirXY.CrossProduct(XYZ.BasisZ);
            if (IsZero(axis)) axis = XYZ.BasisX;
            axis = axis.Normalize();

            Line axLine = Line.CreateBound(origin, origin + axis);

            ElementTransformUtils.RotateElement(doc, trap.Id, axLine, Math.PI / 2.0);
            doc.Regenerate();

            if (!IsTrapNowVertical(trap))
            {
                ElementTransformUtils.RotateElement(doc, trap.Id, axLine, Math.PI);
                doc.Regenerate();
            }
        }

        private static bool OrientTrapBestQuadrant(
            Document doc, FamilyInstance trap, XYZ origin, XYZ dirXY,
            out Connector bestH, out Connector bestV)
        {
            bestH = null; bestV = null;
            Line axisZ = Line.CreateBound(origin, origin + XYZ.BasisZ);

            int bestIdx = -1; double bestScore = double.NegativeInfinity;

            for (int i = 0; i < 4; i++)
            {
                if (TryGetTrapConnectors(trap, dirXY, out Connector h, out Connector v))
                {
                    double s = ScorePose(trap, h, v, dirXY);
                    if (s > bestScore) { bestScore = s; bestIdx = i; bestH = h; bestV = v; }
                }
                ElementTransformUtils.RotateElement(doc, trap.Id, axisZ, Math.PI / 2.0);
                doc.Regenerate();
            }

            if (bestIdx < 0) return false;

            double angleToBest = bestIdx * (Math.PI / 2.0);
            if (Math.Abs(angleToBest) > 1e-6)
            {
                ElementTransformUtils.RotateElement(doc, trap.Id, axisZ, angleToBest);
                doc.Regenerate();
                TryGetTrapConnectors(trap, dirXY, out bestH, out bestV);
            }

            EnsureTrapHangsDown(doc, trap, origin, dirXY, ref bestH, ref bestV);
            EnsureTrapFacesDirection(doc, trap, origin, dirXY, ref bestH, ref bestV);

            return bestH != null && bestV != null;
        }

        private static double ScorePose(FamilyInstance trap, Connector h, Connector v, XYZ dirXY)
        {
            XYZ d = Normalize2D(dirXY);
            XYZ hA = Normalize2D(GetBestAxisForXY(h.CoordinateSystem));
            double s1 = 0.5 * (1.0 + hA.DotProduct(d));

            XYZ vh = Normalize2D(h.Origin - v.Origin);
            double s2 = 0.5 * (1.0 + vh.DotProduct(d));

            double s3 = 0.0;
            BoundingBoxXYZ bb = trap.get_BoundingBox(null);
            if (bb != null)
            {
                double minConnZ = Math.Min(h.Origin.Z, v.Origin.Z);
                if (bb.Min.Z < minConnZ - 1e-3) s3 = 1.0;
            }

            double s4 = Math.Abs(GetBestAxisForZ(v.CoordinateSystem).Z);
            return 2.0 * s1 + 2.0 * s2 + 1.0 * s3 + 0.2 * s4;
        }

        private static void EnsureTrapFacesDirection(Document doc, FamilyInstance trap, XYZ origin, XYZ dirXY,
                                                     ref Connector trapH, ref Connector trapV)
        {
            XYZ current = Normalize2D(GetBestAxisForXY(trapH.CoordinateSystem));
            XYZ desired = Normalize2D(dirXY);

            if (current.DotProduct(desired) < 0.0)
            {
                Line axisZ = Line.CreateBound(origin, origin + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, trap.Id, axisZ, Math.PI);
                doc.Regenerate();
                TryGetTrapConnectors(trap, dirXY, out trapH, out trapV);
            }
        }

        private static void EnsureTrapHangsDown(Document doc, FamilyInstance trap, XYZ origin, XYZ dirXY,
                                                ref Connector trapH, ref Connector trapV)
        {
            BoundingBoxXYZ bb = trap.get_BoundingBox(null);
            if (bb == null) return;

            double minConnZ = Math.Min(trapH.Origin.Z, trapV.Origin.Z);
            if (bb.Min.Z > minConnZ - 1e-3)
            {
                Line axis = Line.CreateBound(origin, origin + Normalize2D(dirXY));
                ElementTransformUtils.RotateElement(doc, trap.Id, axis, Math.PI);
                doc.Regenerate();
                TryGetTrapConnectors(trap, dirXY, out trapH, out trapV);
            }
        }

        private static bool TryGetTrapConnectors(FamilyInstance trap, XYZ dirXY, out Connector horiz, out Connector vert)
        {
            horiz = null; vert = null;

            var cm = trap.MEPModel?.ConnectorManager?.Connectors;
            if (cm == null) return false;

            var list = cm.Cast<Connector>()
                         .Select(c =>
                         {
                             var zAxis = GetBestAxisForZ(c.CoordinateSystem);
                             var xyAxis = GetBestAxisForXY(c.CoordinateSystem);
                             double zAbs = Math.Abs(zAxis.Z);
                             XYZ xy = Normalize2D(xyAxis);
                             double align = Math.Abs(xy.DotProduct(Normalize2D(dirXY)));
                             return new { Conn = c, ZAbs = zAbs, Align = align };
                         })
                         .ToList();

            if (list.Count < 2) return false;

            var v = list.OrderByDescending(x => x.ZAbs).First();
            var h = list.Where(x => !ReferenceEquals(x, v)).OrderByDescending(x => x.Align).First();

            if (v.ZAbs < 0.45 && list.All(x => x.ZAbs < 0.45)) return false;

            vert = v.Conn;
            horiz = h.Conn;
            if (ReferenceEquals(vert, horiz)) return false;

            return true;
        }

        private static XYZ GetBestAxisForZ(Transform cs)
        {
            XYZ[] axes = { cs.BasisX, cs.BasisY, cs.BasisZ };
            return axes.OrderByDescending(a => Math.Abs(a.Z)).First();
        }

        private static XYZ GetBestAxisForXY(Transform cs)
        {
            XYZ[] axes = { cs.BasisX, cs.BasisY, cs.BasisZ };
            return axes.OrderByDescending(a => (a.X * a.X + a.Y * a.Y)).First();
        }

        private static XYZ Normalize2D(XYZ v)
        {
            var p = new XYZ(v.X, v.Y, 0);
            double n = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            return n < 1e-9 ? new XYZ(1, 0, 0) : new XYZ(p.X / n, p.Y / n, 0);
        }

        private static void RebuildVerticalAt(Pipe vertical, XYZ basePoint, double heightFt)
        {
            var lc = vertical.Location as LocationCurve;
            if (lc == null) return;

            XYZ top = basePoint + new XYZ(0, 0, heightFt);
            lc.Curve = Line.CreateBound(top, basePoint);
        }

        private static bool IsZero(XYZ v, double eps = 1e-9) =>
            Math.Abs(v.X) < eps && Math.Abs(v.Y) < eps && Math.Abs(v.Z) < eps;
    }

    // ----------------- UI: MENÚ PRINCIPAL -----------------
    internal class MainMenuForm : WF.Form
    {
        public enum MainChoice { Pipe, Future, Cancel }
        public MainChoice Selection { get; private set; } = MainChoice.Cancel;

        public MainMenuForm()
        {
            Text = "Arañas HID — Menú";
            FormBorderStyle = WF.FormBorderStyle.FixedDialog;
            StartPosition = WF.FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            Width = 420; Height = 230;

            var lbl = new WF.Label
            {
                Left = 20,
                Top = 20,
                Width = 360,
                Text = "¿Qué deseas hacer?"
            };
            var btnPipe = new WF.Button { Left = 20, Top = 60, Width = 360, Height = 32, Text = "1) Tubería (Recta / Sifón)" };
            var btnFuture = new WF.Button { Left = 20, Top = 100, Width = 360, Height = 32, Text = "2) (Futuro) Próxima herramienta" };
            var btnCancel = new WF.Button { Left = 20, Top = 140, Width = 360, Height = 32, Text = "3) Cancelar" };

            btnPipe.Click += (s, e) => { Selection = MainChoice.Pipe; DialogResult = WF.DialogResult.OK; Close(); };
            btnFuture.Click += (s, e) => { Selection = MainChoice.Future; DialogResult = WF.DialogResult.OK; Close(); };
            btnCancel.Click += (s, e) => { Selection = MainChoice.Cancel; DialogResult = WF.DialogResult.Cancel; Close(); };

            Controls.Add(lbl); Controls.Add(btnPipe); Controls.Add(btnFuture); Controls.Add(btnCancel);
        }
    }

    // ----------------- UI: PARÁMETROS DE TUBERÍA -----------------
    internal class PipeParamsForm : WF.Form
    {
        public enum DirOption { NorteSur, SurNorte, OccidenteOriente, OrienteOccidente }
        public enum TipoConexion { Recta = 0, Sifon = 1 }

        private WF.TextBox txtAlturaM, txtLongitudM, txtDiametroIn, txtDesfaseM;
        private WF.ComboBox cmbDireccion, cmbNivel, cmbTipo;
        private WF.Button btnOK, btnCancel, btnBack;

        public double AlturaMetros { get; private set; } = 1.80;
        public double LongitudMetros { get; private set; } = 3.00;
        public double DiametroPulgadas { get; private set; } = 2.0;
        public double DesfaseMetros { get; private set; } = 0.0;
        public DirOption DireccionElegida { get; private set; } = DirOption.OccidenteOriente;
        public int SelectedLevelIndex { get; private set; } = 0;
        public TipoConexion TipoElegido { get; private set; } = TipoConexion.Recta;

        public PipeParamsForm(string[] levelDisplay)
        {
            Text = "Parámetros de tubería";
            FormBorderStyle = WF.FormBorderStyle.FixedDialog;
            StartPosition = WF.FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            Width = 560; Height = 420;

            int x1 = 20, x2 = 280, w = 220;
            int y = 20, dy = 35;

            AddLabel(x1, y, "Altura vertical (m):"); txtAlturaM = AddText(x2, y, "1.80"); y += dy;
            AddLabel(x1, y, "Longitud horizontal (m):"); txtLongitudM = AddText(x2, y, "3.00"); y += dy;
            AddLabel(x1, y, "Diámetro (pulgadas):"); txtDiametroIn = AddText(x2, y, "2"); y += dy;
            AddLabel(x1, y, "Dirección (horizontal):");
            cmbDireccion = new WF.ComboBox { Left = x2, Top = y - 3, Width = w, DropDownStyle = WF.ComboBoxStyle.DropDownList };
            cmbDireccion.Items.AddRange(new object[] { "Norte → Sur", "Sur → Norte", "Occidente → Oriente", "Oriente → Occidente" });
            cmbDireccion.SelectedIndex = 2; Controls.Add(cmbDireccion); y += dy;

            AddLabel(x1, y, "Nivel de referencia:");
            cmbNivel = new WF.ComboBox { Left = x2, Top = y - 3, Width = w, DropDownStyle = WF.ComboBoxStyle.DropDownList };
            cmbNivel.Items.AddRange(levelDisplay);
            cmbNivel.SelectedIndex = 0; Controls.Add(cmbNivel); y += dy;

            AddLabel(x1, y, "Desfase respecto al nivel (m):"); txtDesfaseM = AddText(x2, y, "0.00"); y += dy;

            AddLabel(x1, y, "Tipo de conexión:");
            cmbTipo = new WF.ComboBox { Left = x2, Top = y - 3, Width = w, DropDownStyle = WF.ComboBoxStyle.DropDownList };
            cmbTipo.Items.AddRange(new object[] { "Recta", "Sifón" });
            cmbTipo.SelectedIndex = 0; Controls.Add(cmbTipo); y += dy + 10;

            btnBack = new WF.Button { Text = "← Volver al menú", Left = x2 - 160, Width = 140, Top = y };
            btnOK = new WF.Button { Text = "OK", Left = x2 + 0, Width = 80, Top = y };
            btnCancel = new WF.Button { Text = "Cancelar", Left = x2 + 90, Width = 90, Top = y };

            btnBack.Click += (s, e) => { DialogResult = WF.DialogResult.Retry; Close(); };
            btnCancel.Click += (s, e) => { DialogResult = WF.DialogResult.Cancel; Close(); };
            btnOK.Click += (s, e) =>
            {
                try
                {
                    AlturaMetros = Parse(txtAlturaM.Text, 1.80);
                    LongitudMetros = Parse(txtLongitudM.Text, 3.00);
                    DiametroPulgadas = Parse(txtDiametroIn.Text, 2.0);
                    DesfaseMetros = Parse(txtDesfaseM.Text, 0.0);

                    if (AlturaMetros <= 0 || LongitudMetros <= 0 || DiametroPulgadas <= 0)
                        throw new Exception("Altura, longitud y diámetro deben ser mayores que cero.");

                    DireccionElegida = (DirOption)cmbDireccion.SelectedIndex;
                    SelectedLevelIndex = Math.Max(0, cmbNivel.SelectedIndex);
                    TipoElegido = (TipoConexion)cmbTipo.SelectedIndex;

                    DialogResult = WF.DialogResult.OK;
                    Close();
                }
                catch (Exception ex)
                {
                    WF.MessageBox.Show(ex.Message, "Datos inválidos", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                }
            };

            Controls.Add(btnBack); Controls.Add(btnOK); Controls.Add(btnCancel);

            // Helpers
            void AddLabel(int x, int yy, string text) => Controls.Add(new WF.Label { Left = x, Top = yy, Width = 240, Text = text });
            WF.TextBox AddText(int x, int yy, string text)
            {
                var tb = new WF.TextBox { Left = x, Top = yy - 3, Width = w, Text = text };
                Controls.Add(tb); return tb;
            }
        }

        private static double Parse(string s, double fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }
    }

    // ----------------- UI: FUTURO / PLACEHOLDER -----------------
    internal class FutureFeatureForm : WF.Form
    {
        public FutureFeatureForm()
        {
            Text = "Próxima herramienta";
            FormBorderStyle = WF.FormBorderStyle.FixedDialog;
            StartPosition = WF.FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            Width = 420; Height = 220;

            var lbl = new WF.Label
            {
                Left = 20,
                Top = 20,
                Width = 360,
                Text = "Esta funcionalidad estará disponible en una próxima versión."
            };

            var btnBack = new WF.Button { Left = 20, Top = 80, Width = 360, Height = 32, Text = "← Volver al menú" };
            var btnCancel = new WF.Button { Left = 20, Top = 120, Width = 360, Height = 32, Text = "Cancelar" };

            btnBack.Click += (s, e) => { DialogResult = WF.DialogResult.Retry; Close(); };
            btnCancel.Click += (s, e) => { DialogResult = WF.DialogResult.Cancel; Close(); };

            Controls.Add(lbl); Controls.Add(btnBack); Controls.Add(btnCancel);
        }
    }
}
