using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.SGH.Services;

namespace AutoCAD.SGH.Commands
{
    public class ExportExcelCommand
    {
        [CommandMethod("SGHEXPORTEXCEL")]
        public void Execute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[SGH] Iniciando exportación de Carga de Ocupación a Excel...");
                string docName = string.IsNullOrEmpty(doc.Name) ? "Dibujo" : doc.Name;

                ExcelExportService.Exportar(db, docName, ed);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[SGH Error en comando]: {ex}\n");
            }
        }

        [CommandMethod("SGHEXCEL")]
        public void ExecuteAlias()
        {
            Execute();
        }
    }
}
