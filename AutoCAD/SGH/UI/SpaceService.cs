using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutoCAD.SGH.Models;
using AutoCAD.SGH.Services;

namespace AutoCAD.SGH.UI
{
    public class SpaceService
    {
        public const string BlockName = "DICTA_SGH_TAG_V2";
        public const string AppIdName = "DICTA_SGH_XDATA";

        /// <summary>
        /// Comprueba si el nombre de un bloque corresponde al tag de SGH.
        /// </summary>
        public static bool IsSghBlock(string blockName)
        {
            if (string.IsNullOrEmpty(blockName)) return false;
            return blockName.Equals("DICTA_SGH_TAG", StringComparison.OrdinalIgnoreCase) ||
                   blockName.Equals("DICTA_SGH_TAG_V2", StringComparison.OrdinalIgnoreCase) ||
                   blockName.StartsWith("DICTA_SGH", StringComparison.OrdinalIgnoreCase);
        }

        public static void RegisterAppId(Database db, Transaction tr)
        {
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!regTable.Has(AppIdName))
            {
                regTable.UpgradeOpen();
                var regApp = new RegAppTableRecord { Name = AppIdName };
                regTable.Add(regApp);
                tr.AddNewlyCreatedDBObject(regApp, true);
            }
        }

        public static void WriteXDataToBlock(Transaction tr, Database db, BlockReference blkRef, SghSpace space)
        {
            try
            {
                RegisterAppId(db, tr);

                if (string.IsNullOrWhiteSpace(space.PolylineHandle) && !space.PolylineId.IsNull && space.PolylineId.IsValid)
                {
                    try { space.PolylineHandle = space.PolylineId.Handle.ToString(); } catch { }
                }

                var rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppIdName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.Numero ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.Espacio ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.Piso ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.GrupoOcupacion ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.CargaOcupacion ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataReal, space.Area),
                    new TypedValue((int)DxfCode.ExtendedDataReal, space.AnchoPasillosMm ?? 0.0),
                    new TypedValue((int)DxfCode.ExtendedDataReal, space.AnchoEscalerasMm ?? 0.0),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.GrupoOcupacionNfpa ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.CargaOcupacionNfpa ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.GrupoOcupacionNsr ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.CargaOcupacionNsr ?? ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, space.PolylineHandle ?? "")
                );

                blkRef.XData = rb;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SGH XData Write Error]: {ex.Message}");
            }
        }

        public static void WriteXDataToPolyline(Transaction tr, Database db, DBObject curve, string blockHandle)
        {
            try
            {
                if (curve == null || string.IsNullOrWhiteSpace(blockHandle)) return;
                RegisterAppId(db, tr);
                var rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppIdName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, blockHandle)
                );
                if (curve.IsWriteEnabled)
                {
                    curve.XData = rb;
                }
                else
                {
                    curve.UpgradeOpen();
                    curve.XData = rb;
                }
            }
            catch { }
        }

        public static string ReadBlockHandleFromPolyline(DBObject curve)
        {
            try
            {
                if (curve == null) return null;
                var rb = curve.GetXDataForApplication(AppIdName);
                if (rb != null)
                {
                    var tvs = rb.AsArray();
                    if (tvs != null && tvs.Length > 1 && tvs[1].Value != null)
                    {
                        return tvs[1].Value.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool ReadXDataFromBlock(BlockReference blkRef, SghSpace space)
        {
            try
            {
                var rb = blkRef.GetXDataForApplication(AppIdName);
                if (rb != null)
                {
                    var tvs = rb.AsArray();
                    if (tvs != null && tvs.Length >= 6)
                    {
                        if (tvs.Length > 1 && tvs[1].Value != null) space.Numero = tvs[1].Value.ToString();
                        if (tvs.Length > 2 && tvs[2].Value != null) space.Espacio = tvs[2].Value.ToString();
                        if (tvs.Length > 3 && tvs[3].Value != null) space.Piso = tvs[3].Value.ToString();
                        if (tvs.Length > 4 && tvs[4].Value != null) space.GrupoOcupacion = tvs[4].Value.ToString();
                        if (tvs.Length > 5 && tvs[5].Value != null) space.CargaOcupacion = tvs[5].Value.ToString();

                        if (tvs.Length > 6 && double.TryParse(tvs[6].Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double a) && a > 0)
                            space.Area = a;

                        if (tvs.Length > 7 && double.TryParse(tvs[7].Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pas) && pas > 0)
                            space.AnchoPasillosMm = pas;

                        if (tvs.Length > 8 && double.TryParse(tvs[8].Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double esc) && esc > 0)
                            space.AnchoEscalerasMm = esc;

                        if (tvs.Length > 9 && tvs[9].Value != null) space.GrupoOcupacionNfpa = tvs[9].Value.ToString();
                        if (tvs.Length > 10 && tvs[10].Value != null) space.CargaOcupacionNfpa = tvs[10].Value.ToString();
                        if (tvs.Length > 11 && tvs[11].Value != null) space.GrupoOcupacionNsr = tvs[11].Value.ToString();
                        if (tvs.Length > 12 && tvs[12].Value != null) space.CargaOcupacionNsr = tvs[12].Value.ToString();
                        if (tvs.Length > 13 && tvs[13].Value != null) space.PolylineHandle = tvs[13].Value.ToString();

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SGH XData Read Error]: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Lee los datos SghSpace de una referencia de bloque existente.
        /// </summary>
        public static SghSpace ReadSpaceFromBlock(Transaction tr, BlockReference blkRef)
        {
            var space = new SghSpace
            {
                InsertionPoint = blkRef.Position,
                DrawingScale = blkRef.ScaleFactors.X,
                PolylineId = ObjectId.Null
            };

            // 1. Cargar desde XData para máxima confiabilidad
            bool hasXData = ReadXDataFromBlock(blkRef, space);

            // 2. Complementar con los Atributos gráficos del bloque (sólo si faltan datos en XData)
            foreach (ObjectId attId in blkRef.AttributeCollection)
            {
                if (attId.IsNull || attId.IsErased) continue;
                var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                if (att == null) continue;

                string tag = (att.Tag ?? "").Trim().ToUpperInvariant();
                string val = (att.TextString ?? "").Trim();

                if (tag == "NUMERO")
                {
                    if (string.IsNullOrWhiteSpace(space.Numero) && !string.IsNullOrWhiteSpace(val))
                        space.Numero = val;
                }
                else if (tag == "AREA")
                {
                    if (space.Area <= 0)
                    {
                        string clean = val.Replace("A:", "").Replace("m2", "").Replace("M2", "").Trim();
                        if (double.TryParse(clean.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedArea) && parsedArea > 0)
                        {
                            space.Area = parsedArea;
                        }
                    }
                }
                else if (tag == "USO")
                {
                    if (string.IsNullOrWhiteSpace(space.GrupoOcupacion))
                    {
                        string clean = val.Replace("U:", "").Replace("(", "").Replace(")", "").Trim();
                        if (!string.IsNullOrWhiteSpace(clean)) space.GrupoOcupacion = clean;
                    }
                }
                else if (tag == "CO")
                {
                    if (string.IsNullOrWhiteSpace(space.CargaOcupacion))
                    {
                        string clean = val.Replace("CO:", "").Trim();
                        if (!string.IsNullOrWhiteSpace(clean)) space.CargaOcupacion = clean;
                    }
                }
                else if (tag == "UBICACION" || tag == "ESPACIO" || tag == "UB" || tag == "NOMBRE")
                {
                    if (string.IsNullOrWhiteSpace(space.Espacio))
                    {
                        string clean = Regex.Replace(val, @"^(UB\s*:\s*|ESPACIO\s*:\s*|ESP\s*:\s*|NOMBRE\s*:\s*)", "", RegexOptions.IgnoreCase).Trim();
                        if (!string.IsNullOrWhiteSpace(clean)) space.Espacio = clean;
                    }
                }
                else if (tag == "NIVEL" || tag == "PISO" || tag == "NV" || tag == "LEVEL")
                {
                    if (string.IsNullOrWhiteSpace(space.Piso))
                    {
                        string clean = Regex.Replace(val, @"^(NV\s*:\s*|NIVEL\s*:\s*|PISO\s*:\s*|LEVEL\s*:\s*)", "", RegexOptions.IgnoreCase).Trim();
                        if (!string.IsNullOrWhiteSpace(clean)) space.Piso = clean;
                    }
                }
            }

            return space;
        }

        /// <summary>
        /// Obtiene el número consecutivo más alto para un piso/nivel específico.
        /// </summary>
        public static int GetNextSpaceNumber(Database db, string piso)
        {
            int maxNum = 0;
            string targetPisoKey = OccupancyService.GetCanonicalPisoKey(piso);

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    string[] sghBlockNames = new string[] { "DICTA_SGH_TAG", "DICTA_SGH_TAG_V2" };

                    foreach (string bName in sghBlockNames)
                    {
                        if (bt.Has(bName))
                        {
                            var btr = (BlockTableRecord)tr.GetObject(bt[bName], OpenMode.ForRead);
                            var refs = btr.GetBlockReferenceIds(true, true);
                            foreach (ObjectId refId in refs)
                            {
                                if (refId.IsErased) continue;
                                var blkRef = tr.GetObject(refId, OpenMode.ForRead) as BlockReference;
                                if (blkRef != null)
                                {
                                    var space = ReadSpaceFromBlock(tr, blkRef);
                                    string blkPisoKey = OccupancyService.GetCanonicalPisoKey(space.Piso);

                                    bool isMatch = string.IsNullOrEmpty(targetPisoKey) ||
                                                   string.Equals(blkPisoKey, targetPisoKey, StringComparison.OrdinalIgnoreCase);

                                    if (isMatch && int.TryParse(space.Numero, out int n))
                                    {
                                        if (n > maxNum) maxNum = n;
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch
            {
            }
            return maxNum + 1;
        }


        public static bool IsEspacioTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            tag = tag.Trim().ToUpperInvariant();
            return tag == "ESPACIO" || tag == "UBICACION" || tag == "ESP" || tag == "UB" || tag == "NOMBRE";
        }

        public static bool IsPisoTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            tag = tag.Trim().ToUpperInvariant();
            return tag == "PISO" || tag == "NIVEL" || tag == "NV" || tag == "LEVEL";
        }

        /// <summary>
        /// Sincroniza las propiedades e invisibilidad de los atributos de todas las referencias de bloque insertadas.
        /// </summary>
        private static void SyncAllBlockReferences(Transaction tr, BlockTableRecord btrDef)
        {
            double r = 0.25;
            double cxNew = -0.375;
            double textXNew = cxNew + 0.05;
            double numX = cxNew - r * 0.45;

            var ids = btrDef.GetBlockReferenceIds(true, true);
            foreach (ObjectId refId in ids)
            {
                if (refId.IsErased) continue;
                var blkRef = tr.GetObject(refId, OpenMode.ForWrite) as BlockReference;
                if (blkRef == null) continue;

                foreach (ObjectId attId in blkRef.AttributeCollection)
                {
                    if (attId.IsErased) continue;
                    var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                    if (att == null) continue;

                    string tag = (att.Tag ?? "").ToUpperInvariant();

                    if (IsEspacioTag(tag) || IsPisoTag(tag))
                    {
                        att.Invisible = true;
                    }
                    else if (tag == "AREA")
                    {
                        att.Invisible = false;
                        att.Height = 0.075 * blkRef.ScaleFactors.X;
                        att.Justify = AttachmentPoint.MiddleLeft;
                        att.Position = new Point3d(textXNew, 0.11, 0).TransformBy(blkRef.BlockTransform);
                        att.AlignmentPoint = att.Position;
                    }
                    else if (tag == "USO")
                    {
                        att.Invisible = false;
                        att.Height = 0.075 * blkRef.ScaleFactors.X;
                        att.Justify = AttachmentPoint.MiddleLeft;
                        att.Position = new Point3d(textXNew, 0.00, 0).TransformBy(blkRef.BlockTransform);
                        att.AlignmentPoint = att.Position;
                    }
                    else if (tag == "CO")
                    {
                        att.Invisible = false;
                        att.Height = 0.075 * blkRef.ScaleFactors.X;
                        att.Justify = AttachmentPoint.MiddleLeft;
                        att.Position = new Point3d(textXNew, -0.11, 0).TransformBy(blkRef.BlockTransform);
                        att.AlignmentPoint = att.Position;
                    }
                    else if (tag == "NUMERO")
                    {
                        att.Invisible = false;
                        att.Height = 0.20 * blkRef.ScaleFactors.X;
                        att.Justify = AttachmentPoint.MiddleCenter;
                        att.Position = new Point3d(numX, 0, 0).TransformBy(blkRef.BlockTransform);
                        att.AlignmentPoint = att.Position;
                    }
                }
            }
        }

        /// <summary>
        /// Actualiza los atributos de una referencia de bloque existente en el plano.
        /// </summary>
        public static void UpdateBlockReferenceAttributes(Database db, ObjectId blockRefId, SghSpace space)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blkRef = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                if (blkRef != null)
                {
                    double r = 0.25;
                    double cxNew = -0.375;
                    double textXNew = cxNew + 0.05;
                    double numX = cxNew - r * 0.45;

                    // 1. Guardar XData en la referencia de bloque
                    WriteXDataToBlock(tr, db, blkRef, space);

                    // 2. Actualizar atributos existentes
                    var existingTags = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var btrDef = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId attId in blkRef.AttributeCollection)
                    {
                        if (attId.IsErased) continue;
                        var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                        if (att == null) continue;

                        string tag = (att.Tag ?? "").ToUpperInvariant();
                        existingTags.Add(tag);

                        if (IsEspacioTag(tag))
                        {
                            att.Invisible = true;
                            att.TextString = space.FormattedEspacio;
                        }
                        else if (IsPisoTag(tag))
                        {
                            att.Invisible = true;
                            att.TextString = space.FormattedPiso;
                        }
                        else if (tag == "NUMERO")
                        {
                            att.Invisible = false;
                            att.TextString = space.Numero;
                            att.Height = 0.20 * blkRef.ScaleFactors.X;
                            att.Justify = AttachmentPoint.MiddleCenter;
                            att.Position = new Point3d(numX, 0, 0).TransformBy(blkRef.BlockTransform);
                            att.AlignmentPoint = att.Position;
                            att.AdjustAlignment(db);
                        }
                        else if (tag == "AREA")
                        {
                            att.Invisible = false;
                            att.TextString = space.FormattedArea;
                            att.Height = 0.075 * blkRef.ScaleFactors.X;
                            att.Justify = AttachmentPoint.MiddleLeft;
                            att.Position = new Point3d(textXNew, 0.11, 0).TransformBy(blkRef.BlockTransform);
                            att.AlignmentPoint = att.Position;
                            att.AdjustAlignment(db);
                        }
                        else if (tag == "USO")
                        {
                            att.Invisible = false;
                            att.TextString = space.FormattedUso;
                            att.Height = 0.075 * blkRef.ScaleFactors.X;
                            att.Justify = AttachmentPoint.MiddleLeft;
                            att.Position = new Point3d(textXNew, 0.00, 0).TransformBy(blkRef.BlockTransform);
                            att.AlignmentPoint = att.Position;
                            att.WidthFactor = Math.Min(1.0, Math.Max(0.45, 6.0 / Math.Max(1, (space.GrupoOcupacion ?? "").Length)));
                            att.AdjustAlignment(db);
                        }
                        else if (tag == "CO")
                        {
                            att.Invisible = false;
                            att.TextString = space.FormattedCo;
                            att.Height = 0.075 * blkRef.ScaleFactors.X;
                            att.Justify = AttachmentPoint.MiddleLeft;
                            att.Position = new Point3d(textXNew, -0.11, 0).TransformBy(blkRef.BlockTransform);
                            att.AlignmentPoint = att.Position;
                            att.AdjustAlignment(db);
                        }
                    }

                    // 3. Sincronizar atributos que falten en el bloque
                    if (btrDef != null)
                    {
                        foreach (ObjectId id in btrDef)
                        {
                            var attDef = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                            if (attDef != null && !attDef.Constant)
                            {
                                string tag = attDef.Tag.ToUpperInvariant();
                                bool exists = existingTags.Contains(tag) ||
                                             (IsEspacioTag(tag) && (existingTags.Contains("ESPACIO") || existingTags.Contains("UBICACION") || existingTags.Contains("ESP"))) ||
                                             (IsPisoTag(tag) && (existingTags.Contains("PISO") || existingTags.Contains("NIVEL") || existingTags.Contains("NV")));

                                if (!exists)
                                {
                                    var attRef = new AttributeReference();
                                    attRef.SetAttributeFromBlock(attDef, blkRef.BlockTransform);

                                    if (IsEspacioTag(tag))
                                    {
                                        attRef.Invisible = true;
                                        attRef.TextString = space.FormattedEspacio;
                                    }
                                    else if (IsPisoTag(tag))
                                    {
                                        attRef.Invisible = true;
                                        attRef.TextString = space.FormattedPiso;
                                    }
                                    else if (tag == "NUMERO")
                                    {
                                        attRef.Invisible = false;
                                        attRef.TextString = space.Numero;
                                        attRef.Height = 0.20 * blkRef.ScaleFactors.X;
                                        attRef.Justify = AttachmentPoint.MiddleCenter;
                                        attRef.Position = new Point3d(numX, 0, 0).TransformBy(blkRef.BlockTransform);
                                        attRef.AlignmentPoint = attRef.Position;
                                        attRef.AdjustAlignment(db);
                                    }
                                    else if (tag == "AREA")
                                    {
                                        attRef.Invisible = false;
                                        attRef.TextString = space.FormattedArea;
                                        attRef.Height = 0.075 * blkRef.ScaleFactors.X;
                                        attRef.Justify = AttachmentPoint.MiddleLeft;
                                        attRef.Position = new Point3d(textXNew, 0.11, 0).TransformBy(blkRef.BlockTransform);
                                        attRef.AlignmentPoint = attRef.Position;
                                        attRef.AdjustAlignment(db);
                                    }
                                    else if (tag == "USO")
                                    {
                                        attRef.Invisible = false;
                                        attRef.TextString = space.FormattedUso;
                                        attRef.Height = 0.075 * blkRef.ScaleFactors.X;
                                        attRef.Justify = AttachmentPoint.MiddleLeft;
                                        attRef.Position = new Point3d(textXNew, 0.00, 0).TransformBy(blkRef.BlockTransform);
                                        attRef.AlignmentPoint = attRef.Position;
                                        attRef.AdjustAlignment(db);
                                    }
                                    else if (tag == "CO")
                                    {
                                        attRef.Invisible = false;
                                        attRef.TextString = space.FormattedCo;
                                        attRef.Height = 0.075 * blkRef.ScaleFactors.X;
                                        attRef.Justify = AttachmentPoint.MiddleLeft;
                                        attRef.Position = new Point3d(textXNew, -0.11, 0).TransformBy(blkRef.BlockTransform);
                                        attRef.AlignmentPoint = attRef.Position;
                                        attRef.AdjustAlignment(db);
                                    }

                                    blkRef.AttributeCollection.AppendAttribute(attRef);
                                    tr.AddNewlyCreatedDBObject(attRef, true);
                                    existingTags.Add(tag);
                                }
                            }
                        }
                    }

                    blkRef.RecordGraphicsModified(true);
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Crea o valida las definiciones de bloque SGH (DICTA_SGH_TAG y DICTA_SGH_TAG_V2) con la cápsula exacta.
        /// </summary>
        public static ObjectId EnsureBlockDefinitionExists(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                double r = 0.25;
                double wRight = 0.75;
                double cxNew = -0.375;
                double textXNew = cxNew + 0.05;
                double numX = cxNew - r * 0.45;

                string[] sghBlockNames = new string[] { "DICTA_SGH_TAG", "DICTA_SGH_TAG_V2" };
                foreach (string bName in sghBlockNames)
                {
                    if (bt.Has(bName))
                    {
                        var btrId = bt[bName];
                        var btrExisting = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);

                        foreach (ObjectId id in btrExisting)
                        {
                            var attDef = tr.GetObject(id, OpenMode.ForWrite) as AttributeDefinition;
                            if (attDef != null)
                            {
                                string t = attDef.Tag.ToUpperInvariant();

                                if (IsEspacioTag(t) || IsPisoTag(t))
                                {
                                    attDef.Invisible = true;
                                }
                                else if (t == "AREA")
                                {
                                    attDef.Invisible = false;
                                    attDef.Height = 0.075;
                                    attDef.Justify = AttachmentPoint.MiddleLeft;
                                    attDef.AlignmentPoint = new Point3d(textXNew, 0.11, 0);
                                    attDef.Position = new Point3d(textXNew, 0.11, 0);
                                }
                                else if (t == "USO")
                                {
                                    attDef.Invisible = false;
                                    attDef.Height = 0.075;
                                    attDef.Justify = AttachmentPoint.MiddleLeft;
                                    attDef.AlignmentPoint = new Point3d(textXNew, 0.00, 0);
                                    attDef.Position = new Point3d(textXNew, 0.00, 0);
                                }
                                else if (t == "CO")
                                {
                                    attDef.Invisible = false;
                                    attDef.Height = 0.075;
                                    attDef.Justify = AttachmentPoint.MiddleLeft;
                                    attDef.AlignmentPoint = new Point3d(textXNew, -0.11, 0);
                                    attDef.Position = new Point3d(textXNew, -0.11, 0);
                                }
                                else if (t == "NUMERO")
                                {
                                    attDef.Invisible = false;
                                    attDef.Height = 0.20;
                                    attDef.Justify = AttachmentPoint.MiddleCenter;
                                    attDef.AlignmentPoint = new Point3d(numX, 0, 0);
                                    attDef.Position = new Point3d(numX, 0, 0);
                                }
                            }
                        }

                        SyncAllBlockReferences(tr, btrExisting);
                    }
                }

                if (bt.Has(BlockName))
                {
                    tr.Commit();
                    return bt[BlockName];
                }

                bt.UpgradeOpen();
                var btr = new BlockTableRecord
                {
                    Name = BlockName,
                    Origin = Point3d.Origin
                };
                var newBtrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                var border = new Polyline();
                border.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                border.AddVertexAt(0, new Point2d(cxNew, -r), 0, 0, 0);
                border.AddVertexAt(1, new Point2d(cxNew + wRight, -r), 1.0, 0, 0);
                border.AddVertexAt(2, new Point2d(cxNew + wRight, r), 0, 0, 0);
                border.AddVertexAt(3, new Point2d(cxNew, r), 1.0, 0, 0);
                border.Closed = true;

                btr.AppendEntity(border);
                tr.AddNewlyCreatedDBObject(border, true);

                var divLine = new Line(new Point3d(cxNew, -r, 0), new Point3d(cxNew, r, 0));
                divLine.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                btr.AppendEntity(divLine);
                tr.AddNewlyCreatedDBObject(divLine, true);

                var attNumero = new AttributeDefinition
                {
                    Tag = "NUMERO",
                    Prompt = "Número de Espacio",
                    TextString = "1",
                    Height = 0.20,
                    Justify = AttachmentPoint.MiddleCenter,
                    AlignmentPoint = new Point3d(numX, 0, 0),
                    Position = new Point3d(numX, 0, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attNumero);
                tr.AddNewlyCreatedDBObject(attNumero, true);

                var attEsp = new AttributeDefinition
                {
                    Tag = "ESPACIO",
                    Prompt = "Espacio",
                    TextString = "ESP: Espacio 1",
                    Height = 0.065,
                    Invisible = true,
                    Justify = AttachmentPoint.MiddleLeft,
                    AlignmentPoint = new Point3d(textXNew, 0.165, 0),
                    Position = new Point3d(textXNew, 0.165, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attEsp);
                tr.AddNewlyCreatedDBObject(attEsp, true);

                var attPis = new AttributeDefinition
                {
                    Tag = "PISO",
                    Prompt = "Piso",
                    TextString = "PISO: Piso 1",
                    Height = 0.075,
                    Invisible = true,
                    Justify = AttachmentPoint.MiddleLeft,
                    AlignmentPoint = new Point3d(textXNew, 0.055, 0),
                    Position = new Point3d(textXNew, 0.055, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attPis);
                tr.AddNewlyCreatedDBObject(attPis, true);

                var attArea = new AttributeDefinition
                {
                    Tag = "AREA",
                    Prompt = "Área",
                    TextString = "A: 0,00m2",
                    Height = 0.075,
                    Justify = AttachmentPoint.MiddleLeft,
                    AlignmentPoint = new Point3d(textXNew, 0.11, 0),
                    Position = new Point3d(textXNew, 0.11, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attArea);
                tr.AddNewlyCreatedDBObject(attArea, true);

                var attUso = new AttributeDefinition
                {
                    Tag = "USO",
                    Prompt = "Grupo de Ocupación",
                    TextString = "U:  (A-1)",
                    Height = 0.075,
                    Justify = AttachmentPoint.MiddleLeft,
                    AlignmentPoint = new Point3d(textXNew, 0.00, 0),
                    Position = new Point3d(textXNew, 0.00, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attUso);
                tr.AddNewlyCreatedDBObject(attUso, true);

                var attCo = new AttributeDefinition
                {
                    Tag = "CO",
                    Prompt = "Carga de Ocupación",
                    TextString = "CO:  1",
                    Height = 0.075,
                    Justify = AttachmentPoint.MiddleLeft,
                    AlignmentPoint = new Point3d(textXNew, -0.11, 0),
                    Position = new Point3d(textXNew, -0.11, 0),
                    Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
                };
                btr.AppendEntity(attCo);
                tr.AddNewlyCreatedDBObject(attCo, true);

                tr.Commit();
                return newBtrId;
            }
        }

        /// <summary>
        /// Inserta la referencia del bloque con sus atributos en el espacio actual.
        /// </summary>
        public static ObjectId InsertSpaceTag(Database db, SghSpace space)
        {
            var blockDefId = EnsureBlockDefinitionExists(db);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var blockRef = new BlockReference(space.InsertionPoint, blockDefId)
                {
                    Layer = AreaService.LayerTags,
                    ScaleFactors = new Scale3d(space.DrawingScale)
                };

                curSpace.AppendEntity(blockRef);
                tr.AddNewlyCreatedDBObject(blockRef, true);

                // 1. Guardar XData en el bloque
                WriteXDataToBlock(tr, db, blockRef, space);

                // 2. Crear referencias de atributos
                var btr = (BlockTableRecord)tr.GetObject(blockDefId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    DBObject dbObj = null;
                    try { dbObj = tr.GetObject(id, OpenMode.ForRead, false); } catch { continue; }

                    if (dbObj is AttributeDefinition attDef && !attDef.Constant)
                    {
                        var attRef = new AttributeReference();
                        attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                        attRef.Justify = attDef.Justify;
                        if (attDef.Justify != AttachmentPoint.BaseLeft)
                        {
                            attRef.AlignmentPoint = attDef.AlignmentPoint.TransformBy(blockRef.BlockTransform);
                        }
                        attRef.Position = attDef.Position.TransformBy(blockRef.BlockTransform);

                        string tag = attDef.Tag.ToUpperInvariant();
                        if (IsEspacioTag(tag))
                        {
                            attRef.Invisible = true;
                            attRef.TextString = space.FormattedEspacio;
                        }
                        else if (IsPisoTag(tag))
                        {
                            attRef.Invisible = true;
                            attRef.TextString = space.FormattedPiso;
                        }
                        else if (tag == "NUMERO")
                        {
                            attRef.Invisible = false;
                            attRef.TextString = space.Numero;
                        }
                        else if (tag == "AREA")
                        {
                            attRef.Invisible = false;
                            attRef.TextString = space.FormattedArea;
                        }
                        else if (tag == "USO")
                        {
                            attRef.Invisible = false;
                            attRef.TextString = space.FormattedUso;
                            attRef.WidthFactor = Math.Min(1.0, Math.Max(0.45, 6.0 / Math.Max(1, (space.GrupoOcupacion ?? "").Length)));
                        }
                        else if (tag == "CO")
                        {
                            attRef.Invisible = false;
                            attRef.TextString = space.FormattedCo;
                        }

                        blockRef.AttributeCollection.AppendAttribute(attRef);
                        tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }

                if (!space.PolylineId.IsNull && space.PolylineId.IsValid)
                {
                    try
                    {
                        var poly = tr.GetObject(space.PolylineId, OpenMode.ForWrite);
                        if (poly != null)
                        {
                            WriteXDataToPolyline(tr, db, poly, blockRef.Handle.ToString());
                        }
                    }
                    catch { }
                }

                tr.Commit();
                return blockRef.ObjectId;
            }
        }
    }
}
