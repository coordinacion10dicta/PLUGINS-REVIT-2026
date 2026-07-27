using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MiNamespace.ValidadorParametros
{
    /// <summary>
    /// Ejecuta la validación dentro del contexto propio de Revit (IExternalEventHandler).
    /// La API de Revit solo puede llamarse desde aquí — nunca desde handlers WPF directamente.
    /// </summary>
    public class ValidacionEventHandler : IExternalEventHandler
    {
        // ─── Request (se llena desde la UI antes de Raise()) ────────────────────
        public string                   Disciplina { get; set; }
        public List<DisciplineParameter> Reglas    { get; set; } = new List<DisciplineParameter>();

        // ─── Callbacks hacia la UI (siempre en dispatcher WPF) ───────────────────
        public Action<Document, ValidationSummary> OnCompleted { get; set; }
        public Action<string>                      OnError      { get; set; }
        public Action                              OnStarted    { get; set; }

        public void Execute(UIApplication app)
        {
            // Este método corre en el hilo principal de Revit — contexto 100% seguro
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    InvokeUi(() => OnError?.Invoke("No hay un documento activo en Revit."));
                    return;
                }

                var summary = ParameterValidator.Validate(doc, Disciplina, Reglas);

                InvokeUi(() => OnCompleted?.Invoke(doc, summary));
            }
            catch (Exception ex)
            {
                InvokeUi(() => OnError?.Invoke($"Error en validación:\n{ex.Message}"));
            }
        }

        public string GetName() => "ValidadorParametros_Validacion";

        // ExternalEventHandler.Execute() runs on Revit main thread.
        // WPF dispatcher marshalling is automatic when called from the same thread.
        // Guard against null callbacks and revive the call even if the action throws.
        private static void InvokeUi(Action action)
        {
            try { action?.Invoke(); }
            catch { }
        }
    }
}
