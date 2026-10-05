using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MiNamespace
{
    /// <summary>
    /// Comando principal que se ejecuta al presionar el botón "TAGS.COOR" en la pestaña DICTA.
    /// Abre un modal con las opciones de coordinación para Redes Secas y Redes Húmedas.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MyTAGS_TAGS_COOR : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            Autodesk.Revit.DB.View view = doc?.ActiveView;

            if (view == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "No hay una vista activa.");
                return Result.Cancelled;
            }

            // 1. Abrir la ventana modal de TAGS de Coordinación
            TagsCoorAction accionSeleccionada = TagsCoorAction.Cancelar;
            using (var modal = new TagsCoorWindow())
            {
                if (modal.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                accionSeleccionada = modal.SelectedAction;
            }

            // 2. Ejecutar la acción seleccionada delegando a la clase correspondiente
            switch (accionSeleccionada)
            {
                // ==================== REDES SECAS ====================
                case TagsCoorAction.NivelesPorSeleccion:
                    OpcionNivelReferencia opcionSel = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
                    if (opcionSel == null) return Result.Cancelled;

                    int creadosSel = MyTAGS_Cotas_RedesSecas.TaguearNivelesPorSeleccion(uidoc, doc, view, opcionSel);
                    if (creadosSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas de Nivel", $"Se crearon {creadosSel} cotas de nivel ({opcionSel.Descripcion}) en los elementos seleccionados.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.NivelesTodaLaVista:
                    OpcionNivelReferencia opcionAuto = NivelReferenciaSelectorWindow.PedirNivel(doc, view);
                    if (opcionAuto == null) return Result.Cancelled;

                    int creados = MyTAGS_Cotas_RedesSecas.TaguearNivelesTodoEnVista(uidoc, doc, view, opcionAuto);
                    Autodesk.Revit.UI.TaskDialog.Show("Cotas de Nivel", $"Se crearon {creados} cotas de nivel ({opcionAuto.Descripcion}) en la vista activa.");
                    return Result.Succeeded;

                case TagsCoorAction.CambioDeNivelPorSeleccion:
                    int cambiosSel = MyTAGS_Cotas_RedesSecas.TaguearCambioDeNivelPorSeleccion(uidoc, doc, view);
                    if (cambiosSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags \"C.N\"", $"Se etiquetaron {cambiosSel} puntos con Tags \"C.N\".");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CambioDeNivelAuto:
                    int cambiosAuto = MyTAGS_Cotas_RedesSecas.TaguearCambioDeNivelTodoEnVista(uidoc, doc, view);
                    Autodesk.Revit.UI.TaskDialog.Show("Tags \"C.N\" Automático", $"Se crearon {cambiosAuto} Tags \"C.N\" en la vista.");
                    return Result.Succeeded;

                case TagsCoorAction.CotasAlineadasPorSeleccion:
                case TagsCoorAction.NivelDeUbicacion:
                    int cotasSel = MyTAGS_Cotas_RedesSecas.TaguearCotasAlineadasPorSeleccion(uidoc, doc, view);
                    if (cotasSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas Alineadas", $"Se colocaron {cotasSel} cotas alineadas.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CotasAlineadasAuto:
                    int cotasAuto = MyTAGS_Cotas_RedesSecas.TaguearCotasAlineadasTodoEnVista(uidoc, doc, view);
                    if (cotasAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas Alineadas Automático", $"Se colocaron {cotasAuto} cotas alineadas en la vista.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CamasConduitsPorSeleccion:
                    int camasSel = MyTAGS_Cotas_RedesSecas.TaguearCamasConduitsPorSeleccion(uidoc, doc, view);
                    if (camasSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Camas de Conduits", $"Se crearon {camasSel} etiquetas en las camas de conduits seleccionadas.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CamasConduitsAuto:
                    int camasAuto = MyTAGS_Cotas_RedesSecas.TaguearCamasConduitsTodoEnVista(uidoc, doc, view);
                    if (camasAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Camas de Conduits", $"Se crearon {camasAuto} etiquetas de camas de conduits en la vista.");
                    }
                    return Result.Succeeded;

                // ==================== REDES HÚMEDAS (DESAGÜES) ====================
                case TagsCoorAction.PendientesPorClic:
                    MyTAGS_Tags_RedesHumedas.TaguearPendientePorClic(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.PendientesPorSeleccion:
                    MyTAGS_Tags_RedesHumedas.TaguearPendientePorSeleccion(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.PendientesTodoEnVista:
                    MyTAGS_Tags_RedesHumedas.TaguearPendienteTodoEnVista(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.MaterialPorSeleccion:
                    MyTAGS_Tags_RedesHumedas.TaguearMaterialPorSeleccion(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.MaterialTodoEnVista:
                    MyTAGS_Tags_RedesHumedas.TaguearMaterialTodoEnVista(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.CambioDeNivelHumedasPorSeleccion:
                    MyTAGS_Tags_RedesHumedas.TaguearCambioDeNivelHumedasPorSeleccion(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.CambioDeNivelHumedasAuto:
                    MyTAGS_Tags_RedesHumedas.TaguearCambioDeNivelHumedasTodoEnVista(uidoc, doc, view);
                    return Result.Succeeded;

                case TagsCoorAction.EmbebidasPlacaPorSeleccion:
                    int placaSel = MyTAGS_Tags_RedesHumedas.TaguearEmbebidasPlacaPorSeleccion(uidoc, doc, view);
                    if (placaSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Placa", $"Se acotaron {placaSel} tuberías embebidas en placa hacia la estructura.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.EmbebidasPlacaAuto:
                    int placaAuto = MyTAGS_Tags_RedesHumedas.TaguearEmbebidasPlacaTodoEnVista(uidoc, doc, view);
                    if (placaAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Placa", $"Se acotaron {placaAuto} tuberías embebidas en placa en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.EmbebidasPisoPorSeleccion:
                    int pisoSel = MyTAGS_Tags_RedesHumedas.TaguearEmbebidasPisoPorSeleccion(uidoc, doc, view);
                    if (pisoSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Piso", $"Se acotaron {pisoSel} tuberías embebidas en piso/afinado hacia los muros/estructura.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.EmbebidasPisoAuto:
                    int pisoAuto = MyTAGS_Tags_RedesHumedas.TaguearEmbebidasPisoTodoEnVista(uidoc, doc, view);
                    if (pisoAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Embebidas en Piso", $"Se acotaron {pisoAuto} tuberías embebidas en piso/afinado en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ElevadasPorSeleccion:
                    int elevSel = MyTAGS_Tags_RedesHumedas.TaguearTuberiaElevadaPorSeleccion(uidoc, doc, view);
                    if (elevSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tuberías Elevadas", $"Se acotaron {elevSel} tuberías elevadas / cielo raso a la estructura superior.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ElevadasAuto:
                    int elevAuto = MyTAGS_Tags_RedesHumedas.TaguearTuberiaElevadaTodoEnVista(uidoc, doc, view);
                    if (elevAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tuberías Elevadas", $"Se acotaron {elevAuto} tuberías elevadas / cielo raso en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.PasesVigaPorSeleccion:
                    int pasesSel = MyTAGS_Tags_RedesHumedas.TaguearPasesEnVigaPorSeleccion(uidoc, doc, view);
                    if (pasesSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pases en Viga", $"Se etiquetaron {pasesSel} pases en vigas con el tag de tamaño (Size).");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.PasesVigaAuto:
                    int pasesAuto = MyTAGS_Tags_RedesHumedas.TaguearPasesEnVigaTodoEnVista(uidoc, doc, view);
                    if (pasesAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Pases en Viga", $"Se etiquetaron {pasesAuto} pases en vigas en la vista activa con el tag de tamaño (Size).");
                    }
                    return Result.Succeeded;

                default:
                    return Result.Cancelled;
            }
        }
    }
}