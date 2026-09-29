using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.SGH.Models;
using AutoCAD.SGH.UI;

namespace AutoCAD.SGH.Services
{
    public static class PolylineSyncService
    {
        private static readonly HashSet<ObjectId> _modifiedPolylineIds = new HashSet<ObjectId>();
        private static readonly object _lockObj = new object();
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            try
            {
                Application.DocumentManager.DocumentCreated += (s, e) => RegisterDocumentEvents(e.Document);
                Application.DocumentManager.DocumentActivated += (s, e) => RegisterDocumentEvents(e.Document);

                foreach (Document doc in Application.DocumentManager)
                {
                    RegisterDocumentEvents(doc);
                }

                Application.Idle += Application_Idle;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PolylineSyncService Init Error]: {ex.Message}");
            }
        }

        private static void RegisterDocumentEvents(Document doc)
        {
            if (doc == null || doc.Database == null) return;
            try
            {
                doc.Database.ObjectModified -= Database_ObjectModified;
                doc.Database.ObjectModified += Database_ObjectModified;
            }
            catch { }
        }

        private static void Database_ObjectModified(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is Curve curve)
            {
                lock (_lockObj)
                {
                    _modifiedPolylineIds.Add(curve.ObjectId);
                }
            }
        }

        private static void Application_Idle(object sender, EventArgs e)
        {
            List<ObjectId> idsToProcess;
            lock (_lockObj)
            {
                if (_modifiedPolylineIds.Count == 0) return;
                idsToProcess = _modifiedPolylineIds.ToList();
                _modifiedPolylineIds.Clear();
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    bool updatedAny = false;

                    foreach (var polyId in idsToProcess)
                    {
                        if (polyId.IsErased || !polyId.IsValid) continue;
                        var curve = tr.GetObject(polyId, OpenMode.ForRead) as Curve;
                        if (curve == null) continue;

                        // 1. Intentar obtener la manija del bloque asociado desde la polilínea
                        string blkHandleStr = SpaceService.ReadBlockHandleFromPolyline(curve);
                        BlockReference targetBlkRef = null;

                        if (!string.IsNullOrWhiteSpace(blkHandleStr))
                        {
                            try
                            {
                                long hVal = long.Parse(blkHandleStr, NumberStyles.HexNumber);
                                var handle = new Handle(hVal);
                                if (db.TryGetObjectId(handle, out ObjectId blkId) && !blkId.IsErased)
                                {
                                    targetBlkRef = tr.GetObject(blkId, OpenMode.ForWrite) as BlockReference;
                                }
                            }
                            catch { }
                        }

                        // 2. Si no se encontró por manija directa de la polilínea, buscar en los bloques del dibujo
                        if (targetBlkRef == null)
                        {
                            string polyHandleStr = curve.Handle.ToString();
                            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            string[] sghBlockNames = new string[] { "DICTA_SGH_TAG", "DICTA_SGH_TAG_V2" };

                            foreach (string bName in sghBlockNames)
                            {
                                if (bt.Has(bName))
                                {
                                    var btr = (BlockTableRecord)tr.GetObject(bt[bName], OpenMode.ForRead);
                                    foreach (ObjectId refId in btr.GetBlockReferenceIds(true, true))
                                    {
                                        if (refId.IsErased) continue;
                                        var blkRefCandidate = tr.GetObject(refId, OpenMode.ForRead) as BlockReference;
                                        if (blkRefCandidate == null) continue;

                                        var candSpace = SpaceService.ReadSpaceFromBlock(tr, blkRefCandidate);
                                        if (!string.IsNullOrWhiteSpace(candSpace.PolylineHandle) &&
                                            candSpace.PolylineHandle.Equals(polyHandleStr, StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetBlkRef = tr.GetObject(refId, OpenMode.ForWrite) as BlockReference;
                                            break;
                                        }
                                    }
                                }
                                if (targetBlkRef != null) break;
                            }
                        }

                        // 3. Si se encontró el bloque asociado, recalcular área y CO
                        if (targetBlkRef != null)
                        {
                            var space = SpaceService.ReadSpaceFromBlock(tr, targetBlkRef);
                            var calc = AreaService.CalculateArea(curve);
                            double newAreaM2 = calc.AreaM2;

                            if (Math.Abs(space.Area - newAreaM2) > 0.001)
                            {
                                space.Area = newAreaM2;
                                space.PolylineHandle = curve.Handle.ToString();
                                space.PolylineId = curve.ObjectId;

                                // Vincular bidireccionalmente por XData
                                SpaceService.WriteXDataToPolyline(tr, db, curve, targetBlkRef.Handle.ToString());

                                // Recalcular CO para NSR-10 si es numérico
                                string rawNsr = !string.IsNullOrWhiteSpace(space.GrupoOcupacionNsr) ? space.GrupoOcupacionNsr : space.GrupoOcupacion;
                                var gNsr = OccupancyService.NormsNSR10.Groups.FirstOrDefault(g =>
                                    g.DisplayText.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                    g.Code.Equals(rawNsr, StringComparison.OrdinalIgnoreCase) ||
                                    g.Name.Equals(rawNsr, StringComparison.OrdinalIgnoreCase))
                                    ?? OccupancyService.MatchGroup_NSR10(rawNsr);

                                if (gNsr != null && gNsr.FactorM2PerPerson.HasValue && gNsr.FactorM2PerPerson.Value > 0)
                                {
                                    int newCoNsr = Math.Max(1, (int)Math.Ceiling(newAreaM2 / gNsr.FactorM2PerPerson.Value));
                                    space.CargaOcupacionNsr = newCoNsr.ToString();
                                }

                                // Recalcular CO para NFPA si es numérico
                                string rawNfpa = !string.IsNullOrWhiteSpace(space.GrupoOcupacionNfpa) ? space.GrupoOcupacionNfpa : rawNsr;
                                var gNfpa = OccupancyService.NormsNFPA.Groups.FirstOrDefault(g =>
                                    g.DisplayText.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                    g.Code.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase) ||
                                    g.Name.Equals(rawNfpa, StringComparison.OrdinalIgnoreCase))
                                    ?? OccupancyService.MatchGroup_NFPA(rawNfpa);

                                if (gNfpa != null && gNfpa.FactorM2PerPerson.HasValue && gNfpa.FactorM2PerPerson.Value > 0)
                                {
                                    int newCoNfpa = Math.Max(1, (int)Math.Ceiling(newAreaM2 / gNfpa.FactorM2PerPerson.Value));
                                    space.CargaOcupacionNfpa = newCoNfpa.ToString();
                                }

                                // Definir la Carga de Ocupación activa según si la norma principal activa es NFPA o NSR
                                bool isNfpaActive = (gNfpa != null && space.GrupoOcupacion.Equals(gNfpa.Code, StringComparison.OrdinalIgnoreCase)) ||
                                                    (OccupancyService.NormsNFPA.Groups.Any(g => g.Code.Equals(space.GrupoOcupacion, StringComparison.OrdinalIgnoreCase)));

                                space.CargaOcupacion = isNfpaActive ? space.CargaOcupacionNfpa : space.CargaOcupacionNsr;

                                // Recalcular anchos de evacuación si aplican
                                var (anchoPas, anchoEsc) = OccupancyService.CalculateEgressWidths(
                                    int.TryParse(space.CargaOcupacion, out int coVal) ? coVal : 1,
                                    space.GrupoOcupacion);

                                if (anchoPas.HasValue) space.AnchoPasillosMm = anchoPas.Value;
                                if (anchoEsc.HasValue) space.AnchoEscalerasMm = anchoEsc.Value;

                                // Actualizar bloque en dibujo y XData
                                SpaceService.UpdateBlockReferenceAttributes(db, targetBlkRef.ObjectId, space);
                                targetBlkRef.RecordGraphicsModified(true);
                                updatedAny = true;

                                doc.Editor.WriteMessage($"\n[SGH] Área del bloque #{space.Numero} actualizada automáticamente a {space.Area:0.00} m².");
                            }
                        }
                    }

                    tr.Commit();

                    if (updatedAny)
                    {
                        try { doc.Editor.Regen(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PolylineSyncService Process Error]: {ex.Message}");
            }
        }
    }
}
