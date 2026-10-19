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
            string nivelManualSeleccionado = null;
            using (var modal = new TagsCoorWindow(doc))
            {
                if (modal.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                accionSeleccionada = modal.SelectedAction;
                nivelManualSeleccionado = modal.SelectedLevelName;
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
                        Autodesk.Revit.UI.TaskDialog.Show("Camas Conduits", $"Se crearon {camasSel} etiquetas en las camas de conduits seleccionadas.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CamasConduitsAuto:
                    int camasAuto = MyTAGS_Cotas_RedesSecas.TaguearCamasConduitsTodoEnVista(uidoc, doc, view);
                    if (camasAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Camas Conduits", $"Se crearon {camasAuto} etiquetas de camas de conduits en la vista.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CamasTubosPorSeleccion:
                    int tubosSel = MyTAGS_Cotas_RedesSecas.TaguearCamasDeTubosPorSeleccion(uidoc, doc, view);
                    if (tubosSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Camas de Tuberías", $"Se generaron {tubosSel} tags/cotas de camas de tuberías.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CamasTubosAuto:
                    int tubosAuto = MyTAGS_Cotas_RedesSecas.TaguearCamasDeTubosTodoEnVista(uidoc, doc, view);
                    if (tubosAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Camas de Tuberías", $"Se generaron {tubosAuto} tags/cotas de camas de tuberías en la vista.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CotasBandejasPorSeleccion:
                    int bandejasSel = MyTAGS_Cotas_RedesSecas.TaguearCotasBandejasPorSeleccion(uidoc, doc, view);
                    if (bandejasSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas de Bandejas", $"Se acotaron exitosamente {bandejasSel} elementos de bandejas portacables.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.CotasBandejasAuto:
                    int bandejasAuto = MyTAGS_Cotas_RedesSecas.TaguearCotasBandejasTodoEnVista(uidoc, doc, view);
                    if (bandejasAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas de Bandejas", $"Se crearon {bandejasAuto} cotas de bandejas portacables en la vista activa.");
                    }
                    else
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Cotas de Bandejas", "No se crearon cotas. Verifica que haya muros o vigas paralelos a las bandejas (máx. ~6 m), visibles en la vista y en el modelo host (no vinculados).");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ExostosPorSeleccion:
                    int exostosSel = MyTAGS_Cotas_RedesSecas.TaguearExostosPorSeleccion(uidoc, doc, view);
                    if (exostosSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Exostos", $"Se etiquetaron {exostosSel} exostos (diámetro + materialidad).");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ExostosAuto:
                    int exostosAuto = MyTAGS_Cotas_RedesSecas.TaguearExostosTodoEnVista(uidoc, doc, view);
                    if (exostosAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Exostos", $"Se etiquetaron {exostosAuto} exostos en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagRoomPorSeleccion:
                    int buitronesSel = MyTAGS_Cotas_RedesSecas.TaguearBuitronesYCuartosPorSeleccion(uidoc, doc, view);
                    if (buitronesSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag Room", $"Se etiquetaron {buitronesSel} habitaciones/espacios (buitrones / cuartos técnicos) con Room Tags.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagRoomAuto:
                    int buitronesAuto = MyTAGS_Cotas_RedesSecas.TaguearBuitronesYCuartosTodoEnVista(uidoc, doc, view);
                    if (buitronesAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag Room", $"Se etiquetaron {buitronesAuto} habitaciones/espacios (buitrones / cuartos técnicos) en la vista activa con Room Tags.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ConduitTagsPorSeleccion:
                    int conduitsSel = MyTAGS_Cotas_RedesSecas.TaguearConduitTagsPorSeleccion(uidoc, doc, view);
                    if (conduitsSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Conduit Tags", $"Se etiquetaron {conduitsSel} conduits (Type Name + Size).");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.ConduitTagsAuto:
                    int conduitsAuto = MyTAGS_Cotas_RedesSecas.TaguearConduitTagsTodoEnVista(uidoc, doc, view);
                    if (conduitsAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Conduit Tags", $"Se etiquetaron {conduitsAuto} conduits en la vista activa (Type Name + Size).");
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

                case TagsCoorAction.TagDuctosSoloDimensionesSel:
                    int dDimSel = MyTAGS_Tags_RedesHumedas.TaguearDuctosSoloDimensionesPorSeleccion(uidoc, doc, view);
                    if (dDimSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos", $"Se etiquetaron {dDimSel} ductos con solo dimensiones.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagDuctosSoloDimensionesAuto:
                    int dDimAuto = MyTAGS_Tags_RedesHumedas.TaguearDuctosSoloDimensionesTodoEnVista(uidoc, doc, view);
                    if (dDimAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos", $"Se etiquetaron {dDimAuto} ductos con solo dimensiones en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagDuctosDimensionesMaterialSel:
                    int dMatSel = MyTAGS_Tags_RedesHumedas.TaguearDuctosDimensionesMaterialPorSeleccion(uidoc, doc, view);
                    if (dMatSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos", $"Se etiquetaron {dMatSel} ductos con dimensiones y material.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagDuctosDimensionesMaterialAuto:
                    int dMatAuto = MyTAGS_Tags_RedesHumedas.TaguearDuctosDimensionesMaterialTodoEnVista(uidoc, doc, view);
                    if (dMatAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tags de Ductos", $"Se etiquetaron {dMatAuto} ductos con dimensiones y material en la vista activa.");
                    }
                    return Result.Succeeded;

                // ==================== GENERALES (REDES SECAS Y HÚMEDAS) ====================
                case TagsCoorAction.TagSubePorSeleccion:
                    int subeSel = MyTAGS_Sube_Baja.TaguearSubePorSeleccion(uidoc, doc, view);
                    if (subeSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag SUBE", $"Se colocaron {subeSel} tags 'SUBE' en los elementos seleccionados.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagSubeAuto:
                    int subeAuto = MyTAGS_Sube_Baja.TaguearSubeTodoEnVista(uidoc, doc, view);
                    if (subeAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag SUBE Automático", $"Se colocaron {subeAuto} tags 'SUBE' en la vista activa.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagBajaPorSeleccion:
                    int bajaSel = MyTAGS_Sube_Baja.TaguearBajaPorSeleccion(uidoc, doc, view);
                    if (bajaSel > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag BAJA", $"Se colocaron {bajaSel} tags 'BAJA' en los elementos seleccionados.");
                    }
                    return Result.Succeeded;

                case TagsCoorAction.TagBajaAuto:
                    int bajaAuto = MyTAGS_Sube_Baja.TaguearBajaTodoEnVista(uidoc, doc, view);
                    if (bajaAuto > 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Tag BAJA Automático", $"Se colocaron {bajaAuto} tags 'BAJA' en la vista activa.");
                    }
                    return Result.Succeeded;

                default:
                    return Result.Cancelled;
            }
        }
    }
}