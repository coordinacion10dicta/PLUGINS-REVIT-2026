using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutoCAD.Ribbon;

[assembly: ExtensionApplication(typeof(AutoCAD.App))]

namespace AutoCAD
{
    public class App : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                AppRibbon.CreateRibbon();
                AutoCAD.SGH.Services.PolylineSyncService.Initialize();

                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Editor ed = doc.Editor;
                    ed.WriteMessage("\n[DICTA] Plugin AutoCAD inicializado correctamente.\n");
                }
            }
            catch (System.Exception ex)
            {
                try
                {
                    var doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        doc.Editor.WriteMessage($"\n[DICTA] Error inicializando plugin: {ex.Message}\n");
                    }
                }
                catch
                {
                    // Prevenir fallos si aún no hay documento activo
                }
            }
        }

        public void Terminate()
        {
        }
    }
}
