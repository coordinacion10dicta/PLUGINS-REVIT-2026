using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;

using RevitView = Autodesk.Revit.DB.View;
using WinForms = System.Windows.Forms;

namespace MiNamespace
{
    [Transaction(TransactionMode.Manual)]
    public class ARQ_Cotas : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                RevitView view = uidoc.ActiveView;

                if (!(view is ViewPlan))
                {
                    TaskDialog.Show("ARQ Cotas", "Solo funciona en vistas de planta.");
                    return Result.Cancelled;
                }

                // ── UI Principal ──────────────────────────────────
                TaskDialog td = new TaskDialog("DICTA - Cotas");
                td.MainInstruction = "¿Qué deseas acotar?";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Muros");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Espacios");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Ambos");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                td.DefaultButton = TaskDialogResult.Cancel;

                TaskDialogResult res = td.Show();
                if (res == TaskDialogResult.Cancel) return Result.Cancelled;

                bool acotarMuros = res == TaskDialogResult.CommandLink1 || res == TaskDialogResult.CommandLink3;
                bool acotarEspacios = res == TaskDialogResult.CommandLink2 || res == TaskDialogResult.CommandLink3;

                // ── Selección de DimensionType ────────────────────
                var tiposLineales = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(dt => dt.StyleType == DimensionStyleType.Linear)
                    .OrderBy(dt => dt.Name)
                    .ToList();

                if (!tiposLineales.Any())
                {
                    TaskDialog.Show("ARQ Cotas", "No se encontraron tipos de cota lineales en el proyecto.");
                    return Result.Cancelled;
                }

                DimensionType dimTypeSeleccionado = MostrarSelectorDimensionType(tiposLineales);
                if (dimTypeSeleccionado == null) return Result.Cancelled;

                // ── Config Muros ──────────────────────────────────
                if (acotarMuros)
                {
                    TaskDialog td2 = new TaskDialog("Muros - Configuración");
                    td2.MainInstruction = "Configuración de cotas de muros";
                    td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Acotar muros");
                    td2.CommonButtons = TaskDialogCommonButtons.Cancel;
                    td2.DefaultButton = TaskDialogResult.Cancel;

                    TaskDialogResult res2 = td2.Show();
                    if (res2 == TaskDialogResult.Cancel) return Result.Cancelled;
                }

                // ── Config Espacios ───────────────────────────────
                if (acotarEspacios)
                {
                    TaskDialog td3 = new TaskDialog("Espacios - Configuración");
                    td3.MainInstruction = "Configuración de cotas de espacios";
                    td3.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Acotar espacios");
                    td3.CommonButtons = TaskDialogCommonButtons.Cancel;
                    td3.DefaultButton = TaskDialogResult.Cancel;

                    TaskDialogResult res3 = td3.Show();
                    if (res3 == TaskDialogResult.Cancel) return Result.Cancelled;
                }

                // ── Transacción ───────────────────────────────────
                using (Transaction tx = new Transaction(doc, "ARQ Cotas PRO"))
                {
                    tx.Start();

                    if (acotarMuros) AcotarMuros(doc, view, dimTypeSeleccionado);
                    if (acotarEspacios) AcotarEspacios(doc, view, dimTypeSeleccionado);

                    tx.Commit();
                }

                TaskDialog.Show("Éxito", "Cotas generadas correctamente.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ═════════════════════════════════════════════════════════
        // VENTANA INGRESO DE ESPESOR
        // ═════════════════════════════════════════════════════════
        private double MostrarInputEspesor(string etiqueta = "Espesor máximo de acabado (cm):")
        {
            double resultado = -1;

            WinForms.Form form = new WinForms.Form();
            form.Text = "Espesor máximo de acabado";
            form.Width = 320;
            form.Height = 175;
            form.StartPosition = WinForms.FormStartPosition.CenterScreen;
            form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;

            WinForms.Label lbl = new WinForms.Label();
            lbl.Text = etiqueta;
            lbl.Left = 12;
            lbl.Top = 16;
            lbl.Width = 280;
            lbl.Height = 20;
            form.Controls.Add(lbl);

            WinForms.TextBox txtEspesor = new WinForms.TextBox();
            txtEspesor.Left = 12;
            txtEspesor.Top = 42;
            txtEspesor.Width = 280;
            txtEspesor.Height = 24;
            txtEspesor.Font = new System.Drawing.Font("Segoe UI", 10f);
            txtEspesor.MaxLength = 6;
            form.Controls.Add(txtEspesor);

            WinForms.Label lblError = new WinForms.Label();
            lblError.Text = "⚠ Solo se permiten números (ej: 5 o 2.5)";
            lblError.Left = 12;
            lblError.Top = 70;
            lblError.Width = 280;
            lblError.Height = 18;
            lblError.ForeColor = System.Drawing.Color.Red;
            lblError.Font = new System.Drawing.Font("Segoe UI", 8.5f);
            lblError.Visible = false;
            form.Controls.Add(lblError);

            WinForms.Button btnOk = new WinForms.Button();
            btnOk.Text = "Aceptar";
            btnOk.Left = 100;
            btnOk.Top = 95;
            btnOk.Width = 90;
            btnOk.Height = 30;
            form.Controls.Add(btnOk);

            WinForms.Button btnCancel = new WinForms.Button();
            btnCancel.Text = "Cancelar";
            btnCancel.Left = 200;
            btnCancel.Top = 95;
            btnCancel.Width = 90;
            btnCancel.Height = 30;
            btnCancel.DialogResult = WinForms.DialogResult.Cancel;
            form.Controls.Add(btnCancel);

            form.CancelButton = btnCancel;

            txtEspesor.KeyPress += (s, e) =>
            {
                bool esDigito = char.IsDigit(e.KeyChar);
                bool esBackspace = e.KeyChar == (char)Keys.Back;
                bool esPunto = (e.KeyChar == '.' || e.KeyChar == ',')
                                   && !txtEspesor.Text.Contains('.')
                                   && !txtEspesor.Text.Contains(',');

                if (!esDigito && !esBackspace && !esPunto)
                    e.Handled = true;
            };

            txtEspesor.TextChanged += (s, e) =>
            {
                string texto = txtEspesor.Text.Replace(',', '.');
                bool valido = string.IsNullOrEmpty(texto) ||
                                double.TryParse(texto,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out _);

                lblError.Visible = !valido;
                btnOk.Enabled = valido;
            };

            btnOk.Click += (s, e) =>
            {
                string texto = txtEspesor.Text.Trim().Replace(',', '.');

                if (string.IsNullOrEmpty(texto))
                {
                    lblError.Text = "⚠ El campo no puede estar vacío.";
                    lblError.Visible = true;
                    return;
                }

                if (!double.TryParse(texto,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double valor) || valor <= 0)
                {
                    lblError.Text = "⚠ Ingresa un número mayor que 0 (ej: 5 o 2.5)";
                    lblError.Visible = true;
                    return;
                }

                resultado = valor;
                form.DialogResult = WinForms.DialogResult.OK;
                form.Close();
            };

            form.AcceptButton = btnOk;
            form.ShowDialog();

            return resultado;
        }

        // ═════════════════════════════════════════════════════════
        // SELECTOR DE DIMENSIONTYPE
        // ═════════════════════════════════════════════════════════
        private DimensionType MostrarSelectorDimensionType(List<DimensionType> tipos)
        {
            DimensionType seleccionado = null;

            WinForms.Form form = new WinForms.Form();
            form.Text = "Tipo de Cota";
            form.Width = 360;
            form.Height = 460;
            form.StartPosition = WinForms.FormStartPosition.CenterScreen;
            form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;

            WinForms.Label lblFiltro = new WinForms.Label();
            lblFiltro.Text = "Filtrar:";
            lblFiltro.Left = 12;
            lblFiltro.Top = 12;
            lblFiltro.Width = 50;
            lblFiltro.Height = 22;
            lblFiltro.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            form.Controls.Add(lblFiltro);

            WinForms.TextBox txtFiltro = new WinForms.TextBox();
            txtFiltro.Left = 65;
            txtFiltro.Top = 12;
            txtFiltro.Width = 267;
            txtFiltro.Height = 22;
            txtFiltro.Font = new System.Drawing.Font("Segoe UI", 9.5f);
            txtFiltro.ForeColor = System.Drawing.Color.Gray;
            txtFiltro.Text = "Escribe para filtrar...";
            form.Controls.Add(txtFiltro);

            txtFiltro.GotFocus += (s, e) =>
            {
                if (txtFiltro.Text == "Escribe para filtrar...")
                {
                    txtFiltro.Text = "";
                    txtFiltro.ForeColor = System.Drawing.Color.Black;
                }
            };

            txtFiltro.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtFiltro.Text))
                {
                    txtFiltro.Text = "Escribe para filtrar...";
                    txtFiltro.ForeColor = System.Drawing.Color.Gray;
                }
            };

            WinForms.Label lbl = new WinForms.Label();
            lbl.Text = "Selecciona el tipo de cota a usar:";
            lbl.Left = 12;
            lbl.Top = 44;
            lbl.Width = 320;
            lbl.Height = 20;
            form.Controls.Add(lbl);

            WinForms.ListBox listBox = new WinForms.ListBox();
            listBox.Left = 12;
            listBox.Top = 68;
            listBox.Width = 320;
            listBox.Height = 290;
            listBox.Font = new System.Drawing.Font("Segoe UI", 9.5f);
            form.Controls.Add(listBox);

            var tiposFiltrados = new List<DimensionType>(tipos);

            Action ActualizarLista = () =>
            {
                string placeholder = "escribe para filtrar...";
                string filtro = txtFiltro.Text.Trim().ToLower();
                bool sinFiltro = string.IsNullOrEmpty(filtro) || filtro == placeholder;

                tiposFiltrados = sinFiltro
                    ? new List<DimensionType>(tipos)
                    : tipos.Where(dt => dt.Name.ToLower().Contains(filtro)).ToList();

                listBox.BeginUpdate();
                listBox.Items.Clear();
                foreach (DimensionType dt in tiposFiltrados)
                    listBox.Items.Add(dt.Name);
                if (listBox.Items.Count > 0)
                    listBox.SelectedIndex = 0;
                listBox.EndUpdate();
            };

            ActualizarLista();
            txtFiltro.TextChanged += (s, e) => ActualizarLista();

            WinForms.Button btnOk = new WinForms.Button();
            btnOk.Text = "Aceptar";
            btnOk.Left = 148;
            btnOk.Top = 375;
            btnOk.Width = 90;
            btnOk.Height = 30;
            btnOk.DialogResult = WinForms.DialogResult.OK;
            form.Controls.Add(btnOk);

            WinForms.Button btnCancel = new WinForms.Button();
            btnCancel.Text = "Cancelar";
            btnCancel.Left = 244;
            btnCancel.Top = 375;
            btnCancel.Width = 90;
            btnCancel.Height = 30;
            btnCancel.DialogResult = WinForms.DialogResult.Cancel;
            form.Controls.Add(btnCancel);

            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            listBox.DoubleClick += (s, e) =>
            {
                form.DialogResult = WinForms.DialogResult.OK;
                form.Close();
            };

            if (form.ShowDialog() == WinForms.DialogResult.OK && listBox.SelectedIndex >= 0)
                seleccionado = tiposFiltrados[listBox.SelectedIndex];

            return seleccionado;
        }

        private void AcotarMuros(Document doc, RevitView view, DimensionType dimType)
        {
            var muros = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w.Location is LocationCurve lc && lc.Curve is Line)
                .ToList();

            if (!muros.Any()) return;

            double offsetInicial = 1.0 / 30.48;
            double separacion = 0.8 / 30.48;
            double margenBorde = 50.0 / 30.48;
            double tolPegado = 1.0 / 30.48;
            double espacioMinimo = 50.0 / 30.48;

            // ✅ CONTROL DE DUPLICADOS POR EJE
            var longitudesCreadasY = new List<double>();
            var longitudesCreadasX = new List<double>();
            const double tolIgual = 0.5 / 30.48;

            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (Wall w in muros)
            {
                XYZ p0 = ((LocationCurve)w.Location).Curve.GetEndPoint(0);
                XYZ p1 = ((LocationCurve)w.Location).Curve.GetEndPoint(1);
                xMin = Math.Min(xMin, Math.Min(p0.X, p1.X));
                xMax = Math.Max(xMax, Math.Max(p0.X, p1.X));
                yMin = Math.Min(yMin, Math.Min(p0.Y, p1.Y));
                yMax = Math.Max(yMax, Math.Max(p0.Y, p1.Y));
            }

            var grupos = new List<List<Wall>>();
            var asignado = new HashSet<ElementId>();

            foreach (Wall w in muros)
            {
                if (asignado.Contains(w.Id)) continue;

                var grupo = new List<Wall> { w };
                asignado.Add(w.Id);

                Line lineaA = ((LocationCurve)w.Location).Curve as Line;
                XYZ dirA = NormalizarDir(lineaA);

                foreach (Wall candidato in muros)
                {
                    if (asignado.Contains(candidato.Id)) continue;

                    Line lineaB = ((LocationCurve)candidato.Location).Curve as Line;
                    XYZ dirB = NormalizarDir(lineaB);

                    bool mismaDir = dirA.IsAlmostEqualTo(dirB, 0.01) ||
                                    dirA.IsAlmostEqualTo(dirB.Negate(), 0.01);
                    if (!mismaDir) continue;

                    if (MurosPegados(w, candidato, view, tolPegado))
                    {
                        grupo.Add(candidato);
                        asignado.Add(candidato.Id);
                    }
                }

                grupos.Add(grupo);
            }

            var gruposEjeY = new List<List<Wall>>();
            var gruposEjeX = new List<List<Wall>>();

            foreach (var grupo in grupos)
            {
                Line l = ((LocationCurve)grupo[0].Location).Curve as Line;
                XYZ dir = (l.GetEndPoint(1) - l.GetEndPoint(0)).Normalize();
                if (Math.Abs(dir.DotProduct(XYZ.BasisX)) > 0.7)
                    gruposEjeX.Add(grupo);
                else
                    gruposEjeY.Add(grupo);
            }

            // ════════════════════════════════════════
            // 🔹 BARRIDO EJE Y
            // ════════════════════════════════════════
            var gruposYOrdenados = gruposEjeY
                .OrderByDescending(g =>
                {
                    Line l = ((LocationCurve)g[0].Location).Curve as Line;
                    return (l.GetEndPoint(0).X + l.GetEndPoint(1).X) / 2.0;
                })
                .ToList();

            var offsetsIzquierda = new List<double>();
            var offsetsDerecha = new List<double>();

            foreach (var grupo in gruposYOrdenados)
            {
                var (refInicio, refFin, p0G, p1G) =
                    ObtenerCarasExterioresGrupo(doc, grupo, view);

                if (refInicio == null || refFin == null) continue;
                if (p0G.DistanceTo(p1G) < 0.01) continue;

                double longitud = p0G.DistanceTo(p1G);

                double xMedio = grupo
                    .Select(w => (((LocationCurve)w.Location).Curve.GetEndPoint(0).X +
                                  ((LocationCurve)w.Location).Curve.GetEndPoint(1).X) / 2.0)
                    .Average();

                bool usarDerecha1 = (xMedio - xMin) < margenBorde;
                XYZ normal1 = usarDerecha1 ? new XYZ(1, 0, 0) : new XYZ(-1, 0, 0);
                var offsetsUsados1 = usarDerecha1 ? offsetsDerecha : offsetsIzquierda;

                double offset1 = offsetInicial;
                while (offsetsUsados1.Any(o => Math.Abs(o - offset1) < separacion * 0.8))
                    offset1 += separacion;
                offsetsUsados1.Add(offset1);

                GenerarCota(doc, view, dimType, refInicio, refFin, p0G, p1G, normal1, offset1);

                longitudesCreadasY.Add(longitud);

                double espacioDisponible = usarDerecha1
                    ? (xMax - xMedio)
                    : (xMedio - xMin);

                if (espacioDisponible >= espacioMinimo)
                {
                    bool yaExiste = longitudesCreadasY
                        .Any(l => Math.Abs(l - longitud) < tolIgual);

                    if (!yaExiste)
                    {
                        XYZ normal2 = usarDerecha1 ? new XYZ(-1, 0, 0) : new XYZ(1, 0, 0);
                        var offsets2 = usarDerecha1 ? offsetsIzquierda : offsetsDerecha;

                        double offset2 = offsetInicial;
                        while (offsets2.Any(o => Math.Abs(o - offset2) < separacion * 0.8))
                            offset2 += separacion;
                        offsets2.Add(offset2);

                        GenerarCota(doc, view, dimType, refInicio, refFin, p0G, p1G, normal2, offset2);

                        longitudesCreadasY.Add(longitud);
                    }
                }
            }

            // ════════════════════════════════════════
            // 🔹 BARRIDO EJE X
            // ════════════════════════════════════════
            var gruposXOrdenados = gruposEjeX
                .OrderByDescending(g =>
                {
                    Line l = ((LocationCurve)g[0].Location).Curve as Line;
                    return (l.GetEndPoint(0).Y + l.GetEndPoint(1).Y) / 2.0;
                })
                .ToList();

            var offsetsAbajo = new List<double>();
            var offsetsArriba = new List<double>();

            foreach (var grupo in gruposXOrdenados)
            {
                var (refInicio, refFin, p0G, p1G) =
                    ObtenerCarasExterioresGrupo(doc, grupo, view);

                if (refInicio == null || refFin == null) continue;
                if (p0G.DistanceTo(p1G) < 0.01) continue;

                double longitud = p0G.DistanceTo(p1G);

                double yMedio = grupo
                    .Select(w => (((LocationCurve)w.Location).Curve.GetEndPoint(0).Y +
                                  ((LocationCurve)w.Location).Curve.GetEndPoint(1).Y) / 2.0)
                    .Average();

                bool usarAbajo1 = (yMax - yMedio) < margenBorde;
                XYZ normal1 = usarAbajo1 ? new XYZ(0, -1, 0) : new XYZ(0, 1, 0);
                var offsetsUsados1 = usarAbajo1 ? offsetsAbajo : offsetsArriba;

                double offset1 = offsetInicial;
                while (offsetsUsados1.Any(o => Math.Abs(o - offset1) < separacion * 0.8))
                    offset1 += separacion;
                offsetsUsados1.Add(offset1);

                GenerarCota(doc, view, dimType, refInicio, refFin, p0G, p1G, normal1, offset1);

                longitudesCreadasX.Add(longitud);

                double espacioDisponible = usarAbajo1
                    ? (yMedio - yMin)
                    : (yMax - yMedio);

                if (espacioDisponible >= espacioMinimo)
                {
                    bool yaExiste = longitudesCreadasX
                        .Any(l => Math.Abs(l - longitud) < tolIgual);

                    if (!yaExiste)
                    {
                        XYZ normal2 = usarAbajo1 ? new XYZ(0, 1, 0) : new XYZ(0, -1, 0);
                        var offsets2 = usarAbajo1 ? offsetsArriba : offsetsAbajo;

                        double offset2 = offsetInicial;
                        while (offsets2.Any(o => Math.Abs(o - offset2) < separacion * 0.8))
                            offset2 += separacion;
                        offsets2.Add(offset2);

                        GenerarCota(doc, view, dimType, refInicio, refFin, p0G, p1G, normal2, offset2);

                        longitudesCreadasX.Add(longitud);
                    }
                }
            }
        }


        // ═════════════════════════════════════════════════════════
        // GENERAR COTA — con filtro de distancia mínima
        // Si la distancia entre referencias < umbral → omitir
        // ═════════════════════════════════════════════════════════
        private void GenerarCota(
            Document doc, RevitView view, DimensionType dimType,
            Reference refInicio, Reference refFin,
            XYZ p0, XYZ p1, XYZ normal, double offset)
        {
            // ── Filtro Dimension Crowding ─────────────────────────
            // Si la distancia entre p0 y p1 es menor a 60cm → omitir
            const double umbralMinimo = 60.0 / 30.48; // 60 cm en pies

            double distanciaReal = p0.DistanceTo(p1);
            if (distanciaReal < umbralMinimo) return;

            XYZ inicioLinea = p0 + normal * offset;
            XYZ finLinea = p1 + normal * offset;

            if (inicioLinea.DistanceTo(finLinea) < 0.01) return;

            Line dimLine;
            try { dimLine = Line.CreateBound(inicioLinea, finLinea); }
            catch { return; }

            ReferenceArray refArray = new ReferenceArray();
            refArray.Append(refInicio);
            refArray.Append(refFin);

            try
            {
                if (dimType != null)
                    doc.Create.NewDimension(view, dimLine, refArray, dimType);
                else
                    doc.Create.NewDimension(view, dimLine, refArray);
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════
        // DETECTAR MUROS PEGADOS
        // ═════════════════════════════════════════════════════════
        private bool MurosPegados(Wall a, Wall b, RevitView view, double tolerancia)
        {
            Options opt = new Options { ComputeReferences = true, View = view };
            var carasA = ObtenerCarasLaterales(a, opt);
            var carasB = ObtenerCarasLaterales(b, opt);

            foreach (var ptA in carasA)
                foreach (var ptB in carasB)
                    if (new XYZ(ptA.X - ptB.X, ptA.Y - ptB.Y, 0).GetLength() < tolerancia)
                        return true;

            return false;
        }

        // ═════════════════════════════════════════════════════════
        // CARAS LATERALES DEL MURO
        // ═════════════════════════════════════════════════════════
        private List<XYZ> ObtenerCarasLaterales(Wall wall, Options opt)
        {
            var puntos = new List<XYZ>();

            Line line = ((LocationCurve)wall.Location).Curve as Line;
            if (line == null) return puntos;

            XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            XYZ perp = new XYZ(-dir.Y, dir.X, 0);

            GeometryElement geo = wall.get_Geometry(opt);
            if (geo == null) return puntos;

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;
                if (solid == null) continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null) continue;

                    XYZ fn = new XYZ(pf.FaceNormal.X, pf.FaceNormal.Y, 0);
                    if (fn.GetLength() < 0.01) continue;

                    if (Math.Abs(fn.Normalize().DotProduct(perp)) > 0.9)
                        puntos.Add(pf.Evaluate(new UV(0.5, 0.5)));
                }
            }

            return puntos;
        }
        // ═════════════════════════════════════════════════════════
        // CARAS EXTERIORES DEL GRUPO — versión corregida
        // Usa caras de EXTREMO (tapas) del muro más largo del grupo
        // garantizando una sola cota por grupo muro+acabado
        // ═════════════════════════════════════════════════════════
        private (Reference refInicio, Reference refFin, XYZ p0, XYZ p1)
            ObtenerCarasExterioresGrupo(Document doc, List<Wall> grupo, RevitView view)
        {
            Line lineaRef = ((LocationCurve)grupo[0].Location).Curve as Line;
            XYZ dir = (lineaRef.GetEndPoint(1) - lineaRef.GetEndPoint(0)).Normalize();
            XYZ origen = lineaRef.GetEndPoint(0);

            Reference refInicio = null, refFin = null;
            double posMinima = double.MaxValue, posMaxima = double.MinValue;
            XYZ p0Global = origen;
            XYZ p1Global = lineaRef.GetEndPoint(1);

            // Usar solo el muro más largo del grupo para las referencias
            // Los acabados pegados no aportan referencias — solo extienden
            // visualmente el conjunto pero la cota se ancla al muro principal
            Wall muroBase = grupo
                .OrderByDescending(w =>
                {
                    Line l = ((LocationCurve)w.Location).Curve as Line;
                    return l.Length;
                })
                .First();

            // Extremos longitudinales de TODO el grupo (incluyendo acabados)
            foreach (Wall w in grupo)
            {
                Line l = ((LocationCurve)w.Location).Curve as Line;
                XYZ p0 = l.GetEndPoint(0);
                XYZ p1 = l.GetEndPoint(1);

                double t0 = (p0 - origen).DotProduct(dir);
                double t1 = (p1 - origen).DotProduct(dir);

                if (t0 < posMinima) { posMinima = t0; p0Global = p0; }
                if (t1 < posMinima) { posMinima = t1; p0Global = p1; }
                if (t0 > posMaxima) { posMaxima = t0; p1Global = p0; }
                if (t1 > posMaxima) { posMaxima = t1; p1Global = p1; }
            }

            // Referencias de caras de extremo SOLO del muro más largo
            Options opt = new Options { ComputeReferences = true, View = view };
            GeometryElement geo = muroBase.get_Geometry(opt);
            if (geo == null) return (null, null, p0Global, p1Global);

            double posRefMin = double.MaxValue, posRefMax = double.MinValue;

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;
                if (solid == null) continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf?.Reference == null) continue;

                    bool esExtremo =
                        pf.FaceNormal.IsAlmostEqualTo(dir) ||
                        pf.FaceNormal.IsAlmostEqualTo(-dir);

                    if (!esExtremo) continue;

                    XYZ pt = pf.Origin;
                    double pos = (pt - origen).DotProduct(dir);

                    if (pos < posRefMin) { posRefMin = pos; refInicio = pf.Reference; }
                    if (pos > posRefMax) { posRefMax = pos; refFin = pf.Reference; }
                }
            }

            return (refInicio, refFin, p0Global, p1Global);
        }

        // ═════════════════════════════════════════════════════════
        // NORMALIZAR DIRECCIÓN
        // ═════════════════════════════════════════════════════════
        private XYZ NormalizarDir(Line line)
        {
            XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            if (dir.X < -0.0175 || (Math.Abs(dir.X) < 0.0175 && dir.Y < 0))
                dir = dir.Negate();
            return dir;
        }
        // ═════════════════════════════════════════════════════════
        // ESPACIOS
        // ═════════════════════════════════════════════════════════
        private void AcotarEspacios(Document doc, RevitView view, DimensionType dimType)
        {
            UIDocument uidoc = new UIDocument(doc);

            // 🔹 Seleccionar punto
            XYZ punto;
            try
            {
                punto = uidoc.Selection.PickPoint("Selecciona el punto donde ubicar la cota");
            }
            catch
            {
                return;
            }

            // 🔹 Elegir orientación
            TaskDialog td = new TaskDialog("Dirección de cota");
            td.MainInstruction = "Selecciona la dirección de la cota";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Horizontal");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Vertical");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var res = td.Show();
            if (res == TaskDialogResult.Cancel) return;

            bool horizontal = res == TaskDialogResult.CommandLink1;

            // 🔹 Obtener muros visibles
            var muros = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (!muros.Any()) return;

            Options opt = new Options
            {
                ComputeReferences = true,
                View = view
            };

            List<(Reference referencia, XYZ punto)> referencias = new List<(Reference, XYZ)>();

            foreach (var wall in muros)
            {
                GeometryElement geo = wall.get_Geometry(opt);
                if (geo == null) continue;

                foreach (GeometryObject obj in geo)
                {
                    Solid solid = obj as Solid;
                    if (solid == null) continue;

                    foreach (Face face in solid.Faces)
                    {
                        PlanarFace pf = face as PlanarFace;
                        if (pf == null || pf.Reference == null) continue;

                        XYZ fn = new XYZ(pf.FaceNormal.X, pf.FaceNormal.Y, 0);
                        if (fn.GetLength() < 0.01) continue;

                        fn = fn.Normalize();

                        // 🔹 Filtrar caras según dirección
                        if (horizontal && Math.Abs(fn.X) < 0.99) continue;
                        if (!horizontal && Math.Abs(fn.Y) < 0.99) continue;

                        XYZ pt = pf.Evaluate(new UV(0.5, 0.5));

                        // 🔥 AJUSTE: tolerancia aumentada
                        double tolerancia = 5.0; // ~1.5 m

                        if (horizontal)
                        {
                            if (Math.Abs(pt.Y - punto.Y) > tolerancia) continue;
                        }
                        else
                        {
                            if (Math.Abs(pt.X - punto.X) > tolerancia) continue;
                        }

                        referencias.Add((pf.Reference, pt));
                    }
                }
            }

            if (referencias.Count < 2)
            {
                TaskDialog.Show("ARQ Cotas", "No se encontraron suficientes muros para acotar.");
                return;
            }

            // 🔹 Ordenar referencias
            if (horizontal)
                referencias = referencias.OrderBy(r => r.punto.X).ToList();
            else
                referencias = referencias.OrderBy(r => r.punto.Y).ToList();

            ReferenceArray refArray = new ReferenceArray();
            refArray.Append(referencias.First().referencia);
            refArray.Append(referencias.Last().referencia);

            // 🔹 Crear línea de cota
            Line dimLine;

            if (horizontal)
            {
                dimLine = Line.CreateBound(
                    new XYZ(referencias.First().punto.X, punto.Y, 0),
                    new XYZ(referencias.Last().punto.X, punto.Y, 0)
                );
            }
            else
            {
                dimLine = Line.CreateBound(
                    new XYZ(punto.X, referencias.First().punto.Y, 0),
                    new XYZ(punto.X, referencias.Last().punto.Y, 0)
                );
            }

            try
            {
                if (dimType != null)
                    doc.Create.NewDimension(view, dimLine, refArray, dimType);
                else
                    doc.Create.NewDimension(view, dimLine, refArray);
            }
            catch
            {
                TaskDialog.Show("ARQ Cotas", "Error al crear la cota.");
            }
        }




        // ═════════════════════════════════════════════════════════
        // MÉTODO FUERA (IMPORTANTE)
        // ═════════════════════════════════════════════════════════
        private Reference ObtenerReferenciaMuro(Wall wall, Options opt, bool ejeX)
        {
            GeometryElement geo = wall.get_Geometry(opt);
            if (geo == null) return null;

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;
                if (solid == null) continue;

                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.Reference == null) continue;

                    XYZ fn = new XYZ(pf.FaceNormal.X, pf.FaceNormal.Y, 0);
                    if (fn.GetLength() < 0.01) continue;

                    fn = fn.Normalize();

                    // 🔹 Caras eje X
                    if (ejeX && Math.Abs(fn.X) > 0.99)
                        return pf.Reference;

                    // 🔹 Caras eje Y
                    if (!ejeX && Math.Abs(fn.Y) > 0.99)
                        return pf.Reference;
                }
            }

            return null;
        }

    }
}