using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace MiNamespace
{
    public enum SentidoSubeBaja
    {
        Indeterminado,
        Sube,
        Baja
    }

    public class InfoSubeBaja
    {
        public Element Elemento { get; set; }
        public SentidoSubeBaja Sentido { get; set; }
        public XYZ PuntoInsercion { get; set; }
        public string NombreNivelDestino { get; set; }
        public string TextoCompleto { get; set; }
    }

    /// <summary>
    /// Módulo para colocación de Tags "SUBE" y "BAJA" en accesorios y tuberías de instalaciones (Redes Secas y Húmedas).
    /// Permite etiquetar por selección múltiple o automático forzando el tipo exacto (SUBE o BAJA) de la familia de etiquetas.
    /// </summary>
    public static class MyTAGS_Sube_Baja
    {
        #region 1. Flujos para Tag SUBE

        public static int TaguearSubePorSeleccion(UIDocument uidoc, Document doc, View view)
        {
            return EjecutarSeleccionMultiple(uidoc, doc, view, SentidoSubeBaja.Sube, "SUBE");
        }

        public static int TaguearSubeTodoEnVista(UIDocument uidoc, Document doc, View view)
        {
            return EjecutarTodoEnVista(uidoc, doc, view, SentidoSubeBaja.Sube, "SUBE");
        }

        #endregion

        #region 2. Flujos para Tag BAJA

        public static int TaguearBajaPorSeleccion(UIDocument uidoc, Document doc, View view)
        {
            return EjecutarSeleccionMultiple(uidoc, doc, view, SentidoSubeBaja.Baja, "BAJA");
        }

        public static int TaguearBajaTodoEnVista(UIDocument uidoc, Document doc, View view)
        {
            return EjecutarTodoEnVista(uidoc, doc, view, SentidoSubeBaja.Baja, "BAJA");
        }

        #endregion

        #region 3. Métodos Centrales de Selección y Escaneo

        private static int EjecutarSeleccionMultiple(UIDocument uidoc, Document doc, View view, SentidoSubeBaja sentido, string nombreAccion)
        {
            if (uidoc == null || doc == null || view == null) return 0;

            List<Reference> refs = new List<Reference>();
            var filter = new SubeBajaSelectionFilter();

            // 1. Preselección
            var preSelected = uidoc.Selection.GetElementIds();
            if (preSelected != null && preSelected.Count > 0)
            {
                foreach (var id in preSelected)
                {
                    Element el = doc.GetElement(id);
                    if (el != null && filter.AllowElement(el))
                    {
                        refs.Add(new Reference(el));
                    }
                }
            }

            // 2. Selección interactiva con PickObjects (+ agregar, - quitar, botón Finish en barra superior)
            if (refs.Count == 0)
            {
                try
                {
                    var picked = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        filter,
                        $"Selecciona los accesorios/codos que son '{nombreAccion}' y haz clic en 'Finish':"
                    );
                    if (picked != null && picked.Count > 0)
                    {
                        refs.AddRange(picked);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }

            if (refs.Count == 0) return 0;

            var elementos = refs.Select(r => doc.GetElement(r.ElementId)).Where(e => e != null).ToList();
            return ProcesarCreacionTags(doc, view, elementos, sentido);
        }

        private static int EjecutarTodoEnVista(UIDocument uidoc, Document doc, View view, SentidoSubeBaja sentido, string nombreAccion)
        {
            if (doc == null || view == null) return 0;

            var categorias = new[]
            {
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_CableTray
            };

            List<Element> candidatos = new List<Element>();
            foreach (var cat in categorias)
            {
                try
                {
                    var elems = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    candidatos.AddRange(elems);
                }
                catch { }
            }

            if (candidatos.Count == 0) return 0;

            return ProcesarCreacionTags(doc, view, candidatos, sentido);
        }

        #endregion

        #region 4. Creación y Posicionamiento de Tags

        private static int ProcesarCreacionTags(Document doc, View view, List<Element> elementos, SentidoSubeBaja sentidoForzado)
        {
            if (doc == null || view == null || elementos == null || elementos.Count == 0) return 0;

            // 1. Obtener niveles ordenados del proyecto
            List<Level> niveles = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(lvl => lvl.Elevation)
                .ToList();

            Level nivelVista = view.GenLevel;

            // 2. Preparar información de etiquetado
            List<InfoSubeBaja> itemsValidos = new List<InfoSubeBaja>();
            foreach (var el in elementos)
            {
                InfoSubeBaja info = PrepararInfoElemento(el, sentidoForzado, niveles, nivelVista);
                if (info != null)
                {
                    itemsValidos.Add(info);
                }
            }

            if (itemsValidos.Count == 0) return 0;

            // 3. Crear tags en transacción
            int colocados = 0;
            using (Transaction tx = new Transaction(doc, $"Colocar Tags {sentidoForzado}"))
            {
                tx.Start();

                foreach (var item in itemsValidos)
                {
                    try
                    {
                        FamilySymbol tagSymbol = ObtenerTagSubeBaja(doc, item.Elemento, item.Sentido);
                        if (tagSymbol == null) continue;

                        if (!tagSymbol.IsActive)
                        {
                            tagSymbol.Activate();
                            doc.Regenerate();
                        }

                        XYZ ptIns = item.PuntoInsercion ?? ObtenerCentroElemento(item.Elemento);
                        if (ptIns == null) continue;

                        // Desplazar la cabeza del tag para visualización horizontal clara con directriz
                        XYZ offset = (item.Sentido == SentidoSubeBaja.Sube)
                            ? new XYZ(1.2, 0.8, 0)
                            : new XYZ(1.2, -0.8, 0);

                        XYZ headPos = ptIns + offset;

                        Reference refEl = new Reference(item.Elemento);

                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSymbol.Id,
                            view.Id,
                            refEl,
                            true,
                            TagOrientation.Horizontal,
                            headPos
                        );

                        if (tag != null)
                        {
                            tag.TagOrientation = TagOrientation.Horizontal;
                            tag.HasLeader = true;
                            tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            tag.TagHeadPosition = headPos;

                            // Crear hombro horizontal para asegurar que la etiqueta quede 100% horizontal
                            XYZ elbowPos = new XYZ(headPos.X - 0.4, headPos.Y, headPos.Z);
#if REVIT2022_OR_LATER
                            try { tag.SetLeaderElbow(refEl, elbowPos); } catch { }
#else
                            try { tag.LeaderElbow = elbowPos; } catch { }
#endif

                            // Configurar parámetros booleanos y de texto siempre en MAYÚSCULAS
                            AplicarParametrosSubeBaja(item.Elemento, tag, item);

                            colocados++;
                        }
                    }
                    catch { }
                }

                tx.Commit();
            }

            return colocados;
        }

        private static InfoSubeBaja PrepararInfoElemento(Element el, SentidoSubeBaja sentidoForzado, List<Level> niveles, Level nivelVista)
        {
            if (el == null) return null;

            SentidoSubeBaja sentido = sentidoForzado;
            XYZ puntoCentro = ObtenerCentroElemento(el);
            double zElem = puntoCentro != null ? puntoCentro.Z : (nivelVista != null ? nivelVista.Elevation : 0);

            string nombreDestino = ObtenerNombreNivelDestino(zElem, sentido, niveles, nivelVista);
            string accion = (sentido == SentidoSubeBaja.Sube) ? "SUBE" : "BAJA";
            string textoCompleto = string.IsNullOrEmpty(nombreDestino) ? accion : $"{accion} A {nombreDestino}".ToUpperInvariant();

            return new InfoSubeBaja
            {
                Elemento = el,
                Sentido = sentido,
                PuntoInsercion = puntoCentro,
                NombreNivelDestino = nombreDestino,
                TextoCompleto = textoCompleto
            };
        }

        private static string ObtenerNombreNivelDestino(double zActual, SentidoSubeBaja sentido, List<Level> niveles, Level nivelVista)
        {
            if (niveles == null || niveles.Count == 0) return string.Empty;

            double refElevation = (nivelVista != null) ? nivelVista.Elevation : zActual;

            if (sentido == SentidoSubeBaja.Sube)
            {
                var nivelArriba = niveles.FirstOrDefault(l => l.Elevation > refElevation + 0.5);
                if (nivelArriba != null)
                {
                    return FormatearNombreNivel(nivelArriba.Name);
                }
            }
            else if (sentido == SentidoSubeBaja.Baja)
            {
                var nivelAbajo = niveles.LastOrDefault(l => l.Elevation < refElevation - 0.5);
                if (nivelAbajo != null)
                {
                    return FormatearNombreNivel(nivelAbajo.Name);
                }
            }

            return string.Empty;
        }

        private static string FormatearNombreNivel(string nombreOriginal)
        {
            if (string.IsNullOrWhiteSpace(nombreOriginal)) return string.Empty;

            string n = nombreOriginal.Trim();
            string[] partes = n.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length >= 2)
            {
                string ultima = partes[partes.Length - 1].Trim();
                if (!string.IsNullOrEmpty(ultima) && ultima.Length <= 10)
                {
                    return ultima.ToUpperInvariant();
                }
            }

            return n.ToUpperInvariant();
        }

        private static XYZ ObtenerCentroElemento(Element el)
        {
            if (el == null) return null;
            if (el.Location is LocationPoint lp) return lp.Point;
            if (el.Location is LocationCurve lc && lc.Curve != null) return lc.Curve.Evaluate(0.5, true);

            BoundingBoxXYZ bbox = el.get_BoundingBox(null);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;

            return null;
        }

        #endregion

        #region 5. Búsqueda de Símbolo y Parámetros

        public static FamilySymbol ObtenerTagSubeBaja(Document doc, Element elemento, SentidoSubeBaja sentido)
        {
            if (doc == null) return null;

            string nombreTipoDeseado = (sentido == SentidoSubeBaja.Sube) ? "SUBE" : "BAJA";

            try
            {
                var symbols = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                // 1. Prioridad: Familias de tags con Tipo exacto "SUBE" o "BAJA"
                var matchFamiliaConduitTag = symbols.FirstOrDefault(s =>
                    (s.Family?.Name?.IndexOf("TAG - accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG - accesorio ducto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG ducto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG - ducto", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG - accesorio tuberia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("TAG tuberia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("DC- Tag accesorio conduit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     s.Family?.Name?.IndexOf("DC. TAG conduit accesorio", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    s.Name.Equals(nombreTipoDeseado, StringComparison.OrdinalIgnoreCase));

                if (matchFamiliaConduitTag != null) return matchFamiliaConduitTag;

                // 2. Prioridad: Símbolo con nombre EXACTO "SUBE" o "BAJA" en la categoría del elemento
                int targetTagCatId = ObtenerCategoriaTagParaElemento(elemento);

                var matchPorCategoriaYTipo = symbols.FirstOrDefault(s =>
                    (targetTagCatId == 0 || (s.Category != null && s.Category.Id.IntegerValue == targetTagCatId)) &&
                    s.Name.Equals(nombreTipoDeseado, StringComparison.OrdinalIgnoreCase));

                if (matchPorCategoriaYTipo != null) return matchPorCategoriaYTipo;

                // 3. Buscar cualquier símbolo que tenga exactamente el nombre "SUBE" o "BAJA"
                var matchNombreExacto = symbols.FirstOrDefault(s =>
                    s.Name.Equals(nombreTipoDeseado, StringComparison.OrdinalIgnoreCase) &&
                    (s.Category != null && s.Category.Name.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0));

                if (matchNombreExacto != null) return matchNombreExacto;

                // 4. Buscar símbolo que contenga "SUBE" o "BAJA"
                var matchContiene = symbols.FirstOrDefault(s =>
                    (targetTagCatId == 0 || (s.Category != null && s.Category.Id.IntegerValue == targetTagCatId)) &&
                    s.Name.IndexOf(nombreTipoDeseado, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matchContiene != null) return matchContiene;

                // 5. Fallback a tag de la categoría
                if (targetTagCatId != 0)
                {
                    var fallbackCat = symbols.FirstOrDefault(s => s.Category != null && s.Category.Id.IntegerValue == targetTagCatId);
                    if (fallbackCat != null) return fallbackCat;
                }

                return symbols.FirstOrDefault(s => s.Category != null && s.Category.Name.ToLower().Contains("tag"));
            }
            catch
            {
                return null;
            }
        }

        private static int ObtenerCategoriaTagParaElemento(Element elemento)
        {
            if (elemento?.Category == null) return 0;
            int catId = elemento.Category.Id.IntegerValue;

            if (catId == (int)BuiltInCategory.OST_Conduit)
                return (int)BuiltInCategory.OST_ConduitTags;
            if (catId == (int)BuiltInCategory.OST_ConduitFitting)
                return (int)BuiltInCategory.OST_ConduitFittingTags;

            if (catId == (int)BuiltInCategory.OST_PipeCurves)
                return (int)BuiltInCategory.OST_PipeTags;
            if (catId == (int)BuiltInCategory.OST_PipeFitting)
                return (int)BuiltInCategory.OST_PipeFittingTags;

            if (catId == (int)BuiltInCategory.OST_DuctCurves)
                return (int)BuiltInCategory.OST_DuctTags;
            if (catId == (int)BuiltInCategory.OST_DuctFitting)
                return (int)BuiltInCategory.OST_DuctFittingTags;

            if (catId == (int)BuiltInCategory.OST_CableTray)
                return (int)BuiltInCategory.OST_CableTrayTags;
            if (catId == (int)BuiltInCategory.OST_CableTrayFitting)
                return (int)BuiltInCategory.OST_CableTrayFittingTags;

            return 0;
        }

        private static void AplicarParametrosSubeBaja(Element elemento, IndependentTag tag, InfoSubeBaja info)
        {
            if (info == null) return;

            bool esSube = info.Sentido == SentidoSubeBaja.Sube;
            bool esBaja = info.Sentido == SentidoSubeBaja.Baja;
            string textoMayus = (esSube ? "SUBE" : "BAJA").ToUpperInvariant();
            string textoDestinoMayus = (!string.IsNullOrEmpty(info.NombreNivelDestino) ? info.NombreNivelDestino.Trim().ToUpperInvariant() : string.Empty);
            string textoCompletoMayus = (!string.IsNullOrEmpty(textoDestinoMayus) ? $"{textoMayus} A {textoDestinoMayus}" : textoMayus).ToUpperInvariant();

            // 1. Parámetros booleanos SUBE / BAJA en elemento, tag y tipo de tag
            ConfigurarParametroBool(elemento, "SUBE", esSube);
            ConfigurarParametroBool(elemento, "BAJA", esBaja);
            ConfigurarParametroBool(tag, "SUBE", esSube);
            ConfigurarParametroBool(tag, "BAJA", esBaja);

            if (tag != null && tag.Document != null)
            {
                Element tagType = tag.Document.GetElement(tag.GetTypeId());
                if (tagType != null)
                {
                    ConfigurarParametroBool(tagType, "SUBE", esSube);
                    ConfigurarParametroBool(tagType, "BAJA", esBaja);
                }
            }

            // 2. Parámetros de texto en el elemento y en el tag (siempre en MAYÚSCULAS)
            string[] nombresParamsTexto = new[]
            {
                "DC. TAG conduit accesorio",
                "DC. TAG tuberia accesorio",
                "DC. TAG ducto accesorio",
                "DC. TAG bandeja accesorio",
                "DC- Tag accesorio conduit",
                "cambio de nivel",
                "Cambio de Nivel",
                "Cambio de nivel",
                "C.N",
                "SUBE",
                "BAJA",
                "Sube",
                "Baja",
                "SUBE_BAJA",
                "Sube_Baja",
                "DESTINO",
                "Destino",
                "TEXTO",
                "Texto",
                "Comments",
                "Comentarios",
                "Marca",
                "Mark"
            };

            foreach (var pName in nombresParamsTexto)
            {
                if (elemento != null)
                {
                    Parameter pElem = elemento.LookupParameter(pName);
                    if (pElem != null && !pElem.IsReadOnly && pElem.StorageType == StorageType.String)
                    {
                        if (pName.IndexOf("DESTINO", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pElem.Set(textoDestinoMayus);
                        }
                        else if (pName.IndexOf("SUBE_BAJA", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pElem.Set(textoCompletoMayus);
                        }
                        else
                        {
                            pElem.Set(textoMayus);
                        }
                    }
                }

                if (tag != null)
                {
                    Parameter pTag = tag.LookupParameter(pName);
                    if (pTag != null && !pTag.IsReadOnly && pTag.StorageType == StorageType.String)
                    {
                        if (pName.IndexOf("DESTINO", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pTag.Set(textoDestinoMayus);
                        }
                        else if (pName.IndexOf("SUBE_BAJA", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pTag.Set(textoCompletoMayus);
                        }
                        else
                        {
                            pTag.Set(textoMayus);
                        }
                    }
                }
            }
        }

        private static void ConfigurarParametroBool(Element elem, string paramName, bool valor)
        {
            if (elem == null) return;
            Parameter p = elem.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
            {
                p.Set(valor ? 1 : 0);
            }
        }

        private static void ConfigurarParametroString(Element elem, string paramName, string valor)
        {
            if (elem == null || string.IsNullOrEmpty(valor)) return;
            Parameter p = elem.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
            {
                p.Set(valor);
            }
        }

        #endregion

        #region 6. Filtro de Selección

        public class SubeBajaSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;

                return catId == (int)BuiltInCategory.OST_ConduitFitting ||
                       catId == (int)BuiltInCategory.OST_Conduit ||
                       catId == (int)BuiltInCategory.OST_PipeFitting ||
                       catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_DuctFitting ||
                       catId == (int)BuiltInCategory.OST_DuctCurves ||
                       catId == (int)BuiltInCategory.OST_CableTrayFitting ||
                       catId == (int)BuiltInCategory.OST_CableTray;
            }

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        #endregion
    }
}
