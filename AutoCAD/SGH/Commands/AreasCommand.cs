using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.SGH.Models;
using AutoCAD.SGH.Services;
using AutoCAD.SGH.UI;

namespace AutoCAD.SGH.Commands
{
    public class AreasCommand
    {
        public static string LastUsedPiso { get; set; } = "1";

        [CommandMethod("SGHAREAS")]
        public void Execute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                AreaService.EnsureLayersExist(db);

                while (true)
                {
                    var peo = new PromptEntityOptions("\nSeleccione las áreas a nombrar o bloque para [Editar] (Esc para salir): ");
                    peo.SetRejectMessage("\nEl objeto seleccionado debe ser una curva/polilínea de área o un bloque SGH.");
                    peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
                    peo.AddAllowedClass(typeof(Polyline2d), exactMatch: false);
                    peo.AddAllowedClass(typeof(Polyline3d), exactMatch: false);
                    peo.AddAllowedClass(typeof(Circle), exactMatch: false);
                    peo.AddAllowedClass(typeof(Region), exactMatch: false);
                    peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);
                    peo.Keywords.Add("Editar");

                    var per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.Keyword && per.StringResult.Equals("Editar", StringComparison.OrdinalIgnoreCase))
                    {
                        EditAreaExecute();
                        continue;
                    }

                    if (per.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\n[SGH] Operación finalizada.");
                        break;
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead);

                        // Si seleccionó directamente un bloque existente, abrir en Modo Edición
                        if (ent is BlockReference blkRef)
                        {
                            var spaceFromBlock = SpaceService.ReadSpaceFromBlock(tr, blkRef);
                            tr.Commit();

                            var editDialog = new AreaInputDialog(spaceFromBlock, per.ObjectId);
                            Application.ShowModalWindow(editDialog);
                            continue;
                        }

                        // Flujo de creación con curva/polilínea
                        var curve = ent as Curve;
                        if (curve == null)
                        {
                            ed.WriteMessage("\nEl objeto seleccionado no es válido.");
                            tr.Commit();
                            continue;
                        }

                        using (doc.LockDocument())
                        {
                            if (!curve.Layer.Equals(AreaService.LayerPerimetro, StringComparison.OrdinalIgnoreCase))
                            {
                                curve.UpgradeOpen();
                                curve.Layer = AreaService.LayerPerimetro;
                                ed.WriteMessage($"\n[SGH] Se asignó la entidad a la capa '{AreaService.LayerPerimetro}'.");
                            }
                        }

                        var calc = AreaService.CalculateArea(curve);
                        tr.Commit();

                        string currentPiso = string.IsNullOrWhiteSpace(LastUsedPiso) ? "1" : LastUsedPiso;
                        int nextNum = GetNextSpaceNumber(db, currentPiso);

                        var newSpace = new SghSpace
                        {
                            Numero = nextNum.ToString(),
                            Piso = currentPiso,
                            Area = calc.AreaM2,
                            GrupoOcupacion = "A",
                            CargaOcupacion = "1",
                            InsertionPoint = calc.Centroid,
                            DrawingScale = calc.Scale,
                            PolylineId = per.ObjectId
                        };

                        var dialog = new AreaInputDialog(newSpace);
                        Application.ShowModalWindow(dialog);
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SGH Error]: {ex.Message}\n");
            }
        }

        [CommandMethod("SGHEDITAREA")]
        public void EditAreaExecute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                while (true)
                {
                    var peo = new PromptEntityOptions("\nSeleccione el bloque de área SGH que desea editar (Esc para salir): ");
                    peo.SetRejectMessage("\nDebe seleccionar una referencia de bloque.");
                    peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);

                    var per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\n[SGH] Edición finalizada.");
                        break;
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var blkRef = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null)
                        {
                            ed.WriteMessage("\nEl objeto seleccionado no es un bloque.");
                            tr.Commit();
                            continue;
                        }

                        var space = SpaceService.ReadSpaceFromBlock(tr, blkRef);
                        tr.Commit();

                        var dialog = new AreaInputDialog(space, per.ObjectId);
                        Application.ShowModalWindow(dialog);
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SGH Error]: {ex.Message}\n");
            }
        }

        public static int GetNextSpaceNumber(Database db, string piso = null)
        {
            return SpaceService.GetNextSpaceNumber(db, piso ?? LastUsedPiso);
        }
    }
}


