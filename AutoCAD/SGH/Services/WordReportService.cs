using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Autodesk.AutoCAD.EditorInput;
using AutoCAD.SGH.Models;

namespace AutoCAD.SGH.Services
{
    public static class WordReportService
    {
        private const string TEMPLATE_FILE_NAME = "SGH-INF-001.docx";
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        private static readonly XNamespace RELS_NS = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>
        /// Localiza la plantilla Word (.docx) en los directorios del plugin o de desarrollo.
        /// </summary>
        public static string LocalizarPlantilla()
        {
            string dllDir = string.Empty;
            try
            {
                dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(dllDir))
            {
                candidates.Add(Path.Combine(dllDir, "Resources", TEMPLATE_FILE_NAME));
                candidates.Add(Path.Combine(dllDir, TEMPLATE_FILE_NAME));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents\Resources", TEMPLATE_FILE_NAME));
                candidates.Add(Path.Combine(@"C:\ProgramData\Autodesk\ApplicationPlugins\DICTA.bundle\Contents", TEMPLATE_FILE_NAME));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\..\Resources", TEMPLATE_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\..\Resources", TEMPLATE_FILE_NAME)));
                candidates.Add(Path.GetFullPath(Path.Combine(dllDir, @"..\Resources", TEMPLATE_FILE_NAME)));
            }

            candidates.Add(Path.Combine(@"C:\Users\dicta\Desktop\Proyecto_AC\Proyectos\Proyectos_v2.3\Resources", TEMPLATE_FILE_NAME));

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Genera el informe Word completando la plantilla con los metadatos, imágenes y tablas de ocupación.
        /// </summary>
        public static bool GenerarInforme(SghReportModel model, List<SghSpace> spaces, string outputPath, Editor ed = null)
        {
            if (model == null || string.IsNullOrWhiteSpace(outputPath)) return false;

            string templatePath = LocalizarPlantilla();
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                ed?.WriteMessage($"\n[SGH Error]: No se encontró la plantilla de Word '{TEMPLATE_FILE_NAME}'.\n");
                return false;
            }

            try
            {
                string targetDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Copiar la plantilla al destino
                File.Copy(templatePath, outputPath, overwrite: true);

                // Modificar el paquete docx (.zip)
                using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Update))
                {
                    string rIdImgImplantacion = null;
                    string rIdImgLocalizacion = null;
                    long impCx = 5486400, impCy = 3086100;
                    long locCx = 5486400, locCy = 3086100;

                    // 1. Incrustar Imagen de Implantación si fue seleccionada
                    if (!string.IsNullOrWhiteSpace(model.ImplantacionImagePath) && File.Exists(model.ImplantacionImagePath))
                    {
                        try
                        {
                            string ext = Path.GetExtension(model.ImplantacionImagePath).ToLowerInvariant();
                            if (string.IsNullOrEmpty(ext)) ext = ".png";
                            string targetEntryName = "word/media/dicta_implantacion" + ext;

                            var imgEntry = zip.GetEntry(targetEntryName);
                            if (imgEntry != null) imgEntry.Delete();

                            zip.CreateEntryFromFile(model.ImplantacionImagePath, targetEntryName);
                            rIdImgImplantacion = "rIdDictaImg1";

                            CalculateImageEmuDimensions(model.ImplantacionImagePath, out impCx, out impCy);
                        }
                        catch { }
                    }

                    // 2. Incrustar Imagen de Localización si fue seleccionada
                    if (!string.IsNullOrWhiteSpace(model.LocalizacionImagePath) && File.Exists(model.LocalizacionImagePath))
                    {
                        try
                        {
                            string ext = Path.GetExtension(model.LocalizacionImagePath).ToLowerInvariant();
                            if (string.IsNullOrEmpty(ext)) ext = ".png";
                            string targetEntryName = "word/media/dicta_localizacion" + ext;

                            var imgEntry = zip.GetEntry(targetEntryName);
                            if (imgEntry != null) imgEntry.Delete();

                            zip.CreateEntryFromFile(model.LocalizacionImagePath, targetEntryName);
                            rIdImgLocalizacion = "rIdDictaImg2";

                            CalculateImageEmuDimensions(model.LocalizacionImagePath, out locCx, out locCy);
                        }
                        catch { }
                    }

                    // 3. Actualizar word/_rels/document.xml.rels con las nuevas relaciones de imágenes
                    if (rIdImgImplantacion != null || rIdImgLocalizacion != null)
                    {
                        var relsEntry = zip.GetEntry("word/_rels/document.xml.rels");
                        if (relsEntry != null)
                        {
                            XDocument xRels;
                            using (var stream = relsEntry.Open())
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                xRels = XDocument.Load(reader);
                            }

                            if (xRels.Root != null)
                            {
                                if (rIdImgImplantacion != null)
                                {
                                    string ext = Path.GetExtension(model.ImplantacionImagePath).ToLowerInvariant();
                                    if (string.IsNullOrEmpty(ext)) ext = ".png";
                                    xRels.Root.Add(new XElement(RELS_NS + "Relationship",
                                        new XAttribute("Id", rIdImgImplantacion),
                                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                                        new XAttribute("Target", "media/dicta_implantacion" + ext)
                                    ));
                                }

                                if (rIdImgLocalizacion != null)
                                {
                                    string ext = Path.GetExtension(model.LocalizacionImagePath).ToLowerInvariant();
                                    if (string.IsNullOrEmpty(ext)) ext = ".png";
                                    xRels.Root.Add(new XElement(RELS_NS + "Relationship",
                                        new XAttribute("Id", rIdImgLocalizacion),
                                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                                        new XAttribute("Target", "media/dicta_localizacion" + ext)
                                    ));
                                }
                            }

                            relsEntry.Delete();
                            var newRelsEntry = zip.CreateEntry("word/_rels/document.xml.rels", CompressionLevel.Optimal);
                            using (var stream = newRelsEntry.Open())
                            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                            {
                                xRels.Save(writer);
                            }
                        }
                    }

                    // 4. Procesar document.xml (Textos, Portada, Tablas, Imágenes y quitar resaltados)
                    var docEntry = zip.GetEntry("word/document.xml");
                    if (docEntry != null)
                    {
                        XDocument xDoc;
                        using (var stream = docEntry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            xDoc = XDocument.Load(reader);
                        }

                        // Reemplazar textos, tablas e imágenes
                        ProcessDocumentXml(xDoc, model, spaces, rIdImgImplantacion, rIdImgLocalizacion, impCx, impCy, locCx, locCy);

                        // Eliminar TODOS los resaltados amarillos del documento final
                        xDoc.Descendants(W + "highlight").Remove();

                        // Guardar cambios en document.xml
                        docEntry.Delete();
                        var newDocEntry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
                        using (var stream = newDocEntry.Open())
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            xDoc.Save(writer);
                        }
                    }

                    // 5. Procesar headers y footers por si contienen el nombre del proyecto o resaltados
                    foreach (var entry in zip.Entries.ToList())
                    {
                        if (entry.FullName.StartsWith("word/header") || entry.FullName.StartsWith("word/footer"))
                        {
                            XDocument xHf;
                            using (var stream = entry.Open())
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                xHf = XDocument.Load(reader);
                            }

                            ReplaceSimplePlaceholders(xHf, model);
                            xHf.Descendants(W + "highlight").Remove();

                            entry.Delete();
                            var newHfEntry = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                            using (var stream = newHfEntry.Open())
                            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                            {
                                xHf.Save(writer);
                            }
                        }
                    }
                }

                ed?.WriteMessage($"\n[SGH] Informe Word generado exitosamente: {outputPath}\n");
                return true;
            }
            catch (Exception ex)
            {
                ed?.WriteMessage($"\n[SGH Error en generación Word]: {ex.Message}\n");
                return false;
            }
        }

        private static void CalculateImageEmuDimensions(string imagePath, out long cx, out long cy)
        {
            // Tamaño objetivo máximo: Ancho 14.5 cm (aprox 5,200,000 EMU) y Alto 9 cm (aprox 3,200,000 EMU)
            const long maxCx = 5220000;
            const long maxCy = 3200000;

            cx = maxCx;
            cy = maxCy;

            try
            {
                using (var img = System.Drawing.Image.FromFile(imagePath))
                {
                    double aspect = (double)img.Width / Math.Max(1, img.Height);
                    if (aspect >= 1.0)
                    {
                        cx = maxCx;
                        cy = (long)(maxCx / aspect);
                        if (cy > maxCy)
                        {
                            cy = maxCy;
                            cx = (long)(maxCy * aspect);
                        }
                    }
                    else
                    {
                        cy = maxCy;
                        cx = (long)(maxCy * aspect);
                    }
                }
            }
            catch
            {
                cx = maxCx;
                cy = maxCy;
            }
        }

        private static void ReplaceSimplePlaceholders(XDocument doc, SghReportModel model)
        {
            if (doc.Root == null) return;

            foreach (var p in doc.Descendants(W + "p"))
            {
                string pText = GetParagraphText(p);
                if (pText.Contains("NOMBRE DE PROYECTO") || pText.Contains("NOMBRE DE PROYECTO "))
                {
                    SetParagraphSingleText(p, pText.Replace("NOMBRE DE PROYECTO", model.ProjectName));
                }
            }
        }

        private static void ProcessDocumentXml(XDocument doc, SghReportModel model, List<SghSpace> spaces,
            string rIdImplantacion, string rIdLocalizacion, long impCx, long impCy, long locCx, long locCy)
        {
            if (doc.Root == null) return;

            // 0. Eliminar párrafos vacíos residuales antes del salto de página de la portada para evitar páginas en blanco intermedias
            EliminateExtraCoverBlankPages(doc);

            var paragraphs = doc.Descendants(W + "p").ToList();

            foreach (var p in paragraphs)
            {
                string text = GetParagraphText(p);
                if (string.IsNullOrWhiteSpace(text)) continue;

                // --- 1. Portada: Alineación de etiquetas y nombres ---
                if (text.Contains("Director del proyecto:"))
                {
                    FormatCoverItem(p, "Director del proyecto:", model.DirectorName);
                    continue;
                }

                if (text.Contains("Certificado NFPA"))
                {
                    FormatDirectorCert(p, model.DirectorCert);
                    continue;
                }

                if (text.Contains("Coordinador del proyecto:"))
                {
                    FormatCoverItem(p, "Coordinador del proyecto:", model.CoordinatorName);
                    continue;
                }

                if (text.Contains("Elaborado por:"))
                {
                    FormatCoverItem(p, "Elaborado por:", model.AuthorName);
                    continue;
                }

                if (text.Contains("Colaborador:"))
                {
                    FormatCoverItem(p, "Colaborador:", model.CollaboratorName);
                    continue;
                }

                if (text.StartsWith("Fecha:") || text.Contains("Fecha:            ") || (text.Contains("Fecha:") && text.Contains("XXXX")))
                {
                    FormatCoverItem(p, "Fecha:", model.ReportDate);
                    continue;
                }

                // --- 2. Introducción: Nombre del proyecto y fechas en negrilla ---
                if (text.Contains("En el proyecto") && text.Contains("NOMBRE DE PROYECTO"))
                {
                    FormatIntroParagraph1(p, model.ProjectName);
                    continue;
                }

                if ((text.Contains("versión de arquitectura del") || text.Contains("version de arquitectura del")) && text.Contains("El presente informe"))
                {
                    FormatIntroParagraph2(p, model.ArchBaseDate, model.ProjectName);
                    continue;
                }

                // --- 3. Descripción del Proyecto (Texto normal según documento sin negrilla) ---
                if (text.Contains("DESCRIPCIÓN PROYECTO") || text.Contains("DESCRIPCION PROYECTO"))
                {
                    SetParagraphNormalText(p, model.ProjectDescription);
                    continue;
                }

                // --- 4. Metodología (Fecha de arquitectura en negrilla) ---
                if (text.Contains("Bajo el marco normativo de NSR-10") && (text.Contains("versión de arquitectura del") || text.Contains("version de arquitectura del") || text.Contains("FECHA BASE ARQ")))
                {
                    FormatMethodologyParagraph(p, model.ArchBaseDate);
                    continue;
                }

                // --- 5. Imágenes del Proyecto (Implantación y Localización) ---
                if (text.Contains("IMG: IMPLANTACIÓN") || text.Contains("IMG: IMPLANTACION"))
                {
                    if (!string.IsNullOrEmpty(rIdImplantacion))
                    {
                        p.ReplaceWith(BuildImageParagraph(rIdImplantacion, "Implantación Inicial", impCx, impCy));
                    }
                    else
                    {
                        p.Remove();
                    }
                    continue;
                }

                if (text.Contains("IMG: LOCALIZACIÓN") || text.Contains("IMG: LOCALIZACION"))
                {
                    if (!string.IsNullOrEmpty(rIdLocalizacion))
                    {
                        p.ReplaceWith(BuildImageParagraph(rIdLocalizacion, "Localización / Edificio", locCx, locCy));
                    }
                    else
                    {
                        p.Remove();
                    }
                    continue;
                }

                // --- 6. Clasificación de Edificación (NSR-10 K.2) ---
                if (text.Contains("De acuerdo con la clasificación de edificaciones estipulada en el capítulo K.2") || text.Contains("De acuerdo con la clasificacion de edificaciones"))
                {
                    FormatClassificationIntroParagraph(p, model.BuildingClassification, model.SecondaryClassification);
                    continue;
                }

                if (text.Contains("TABLA: CLASIFICACIÓN") || text.Contains("TABLA: CLASIFICACION") || text.Contains("TABLA:CLASIFICACIÓN"))
                {
                    var tblClassification = BuildWordClassificationTable(spaces, model);
                    p.ReplaceWith(tblClassification);
                    continue;
                }

                if (text.Contains("TABLA: CLASIFICACIÓN SECUNDARIA") || text.Contains("TABLA: CLASIFICACION SECUNDARIA") || text.Contains("TABLA: Almacenamiento") || text.Contains("TABLA: ALMACENAMIENTO"))
                {
                    p.Remove();
                    continue;
                }

                // --- 7. Altura y Catalogación de Gran Altura ---
                if (text.Contains("nivel de calle al") && (text.Contains("último nivel habitable") || text.Contains("ultimo nivel habitable") || text.Contains("K.3.1.3") || text.Contains("ES/NO ES DE GRAN ALTURA") || text.Contains("inferior/superior")))
                {
                    FormatBuildingHeightParagraph(p, model.BuildingHeight, model.IsHighRise);
                    continue;
                }

                // --- 8. Resistencia al Fuego (Texto con Negrillas y Tabla J.3.4-3) ---
                if (text.Contains("La clasificación del proyecto corresponde a la") || text.Contains("La clasificacion del proyecto corresponde a la") || text.Contains("CATEGORIA PROYCTO"))
                {
                    FormatFireResistanceParagraph(p, model.RiskCategory, model.GeneralFireResistance);
                    continue;
                }

                if (text.Contains("TABLA: RESISTENCIA AL FUEGO DE ELEMENTOS") || text.Contains("TABLA: RESISTENCIA AL FUEGO"))
                {
                    var tblFire = model.IsResidentialR1R2
                        ? BuildWordResidentialFireResistanceTable(model)
                        : BuildWordFireResistanceTable(model);
                    p.ReplaceWith(tblFire);
                    continue;
                }

                // --- 9. Ocupación (Sección 5.3): Se inserta la tabla de ocupación calculada de los espacios ---
                if (text.Contains("TABLA: OCUPACIÓN") || text.Contains("TABLA: OCUPACION") || text.Contains("TABLA:OCUPACIÓN"))
                {
                    var tblOcc = BuildWordOccupancyTable(spaces);
                    p.ReplaceWith(tblOcc);
                    continue;
                }

                // --- 10. Salidas y Evacuación (Sección 5.4) ---
                if (text.Contains("se debe tener como mínimo") && (text.Contains("#SALIDAS REQUERIDAS") || text.Contains("salidas accesibles desde cualquier punto")))
                {
                    FormatExitsQuantityParagraph(p, model);
                    continue;
                }

                if (text.Contains("TABLA: NÚMERO DE SALIDAS") || text.Contains("TABLA: NUMERO DE SALIDAS") || text.Contains("TABLA:NÚMERO DE SALIDAS"))
                {
                    var tblExits = BuildWordNumberOfExitsTable(spaces, model);
                    p.ReplaceWith(tblExits);
                    continue;
                }

                if (text.Contains("De acuerdo con el cálculo de ocupación y las restricciones de cantidad de salidas") || text.Contains("De acuerdo con el calculo de ocupacion y las restricciones"))
                {
                    FormatExitsEvaluationParagraph(p, model);
                    continue;
                }

                if (text.Contains("TABLA: SEPARACIÓN SALIDAS") || text.Contains("TABLA: SEPARACION SALIDAS") || text.Contains("TABLA:SEPARACIÓN SALIDAS"))
                {
                    var tblSep = BuildWordExitSeparationTable(spaces, model);
                    p.ReplaceWith(tblSep);
                    continue;
                }

                if (text.Contains("El proyecto") && (text.Contains("con la separación entre salidas") || text.Contains("con la separacion entre salidas")))
                {
                    FormatExitSeparationEvaluationParagraph(p, model);
                    continue;
                }

                if (text.Contains("El edificio actualmente") && text.Contains("con este requerimiento"))
                {
                    FormatExitDischargeEvaluationParagraph(p, model);
                    continue;
                }

                if (text.Contains("La distancia máxima de recorrido desde el punto más alejado") || text.Contains("La distancia maxima de recorrido desde el punto mas alejado"))
                {
                    FormatTravelDistanceParagraph(p, model);
                    continue;
                }

                if (text.Contains("Actualmente el edificio") && (text.Contains("con el requerimiento normativo") || text.Contains("con el requerimiento normativo.")))
                {
                    FormatTravelDistanceEvaluationParagraph(p, model);
                    continue;
                }

                if (text.Contains("Los medios de evacuación del edificio") || text.Contains("Los medios de evacuacion del edificio"))
                {
                    FormatEgressCapacityEvaluationParagraph(p, model);
                    continue;
                }

                // --- 11. Reemplazos generales con texto en negrilla para todos los campos completados ---
                ReplacePlaceholdersWithBoldRuns(p, model);
            }

            // Actualizar tablas de Control/Aprobaciones y de Historial del Documento
            UpdateDocumentTables(doc, model);

            // Formatear la sección de CONCLUSIONES con viñetas personalizadas según imagen 1
            ProcessConclusionsSection(doc, model);
        }

        private static void ProcessConclusionsSection(XDocument doc, SghReportModel model)
        {
            if (doc.Root == null) return;

            var paragraphs = doc.Descendants(W + "p").ToList();
            XElement pTitle = null;
            var toRemove = new List<XElement>();

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                string text = GetParagraphText(p).Trim();

                if (text.Equals("CONCLUSIONES", StringComparison.OrdinalIgnoreCase) || text.StartsWith("CONCLUSIONES", StringComparison.OrdinalIgnoreCase))
                {
                    pTitle = p;
                    // Recorrer los siguientes párrafos hasta encontrar "Fin del documento"
                    for (int j = i + 1; j < paragraphs.Count; j++)
                    {
                        var nextP = paragraphs[j];
                        string nextText = GetParagraphText(nextP);
                        if (nextText.Contains("Fin del documento") || nextText.Contains("Fin del documento"))
                        {
                            break;
                        }
                        toRemove.Add(nextP);
                    }
                    break;
                }
            }

            if (pTitle == null) return;

            // Eliminar párrafos temporales entre CONCLUSIONES y Fin del documento
            foreach (var r in toRemove)
            {
                r.Remove();
            }

            // Crear los párrafos de viñetas con las conclusiones del modelo
            string rawConclusions = !string.IsNullOrWhiteSpace(model.Conclusiones) ? model.Conclusiones : SghReportModel.DefaultConclusionsText;
            var lines = rawConclusions.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            XElement insertAfterElement = pTitle;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Quitar viñeta inicial si ya la tiene para formatear uniformemente
                if (trimmed.StartsWith("•") || trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                {
                    trimmed = trimmed.Substring(1).Trim();
                }

                var pBullet = new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                        new XElement(W + "ind", new XAttribute(W + "left", "540"), new XAttribute(W + "hanging", "270")),
                        new XElement(W + "jc", new XAttribute(W + "val", "both")),
                        new XElement(W + "spacing", new XAttribute(W + "after", "100"), new XAttribute(W + "line", "260"), new XAttribute(W + "lineRule", "auto"))
                    ),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                            new XElement(W + "b"),
                            new XElement(W + "sz", new XAttribute(W + "val", "22")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                            new XElement(W + "color", new XAttribute(W + "val", "000000"))
                        ),
                        new XElement(W + "t", "•")
                    ),
                    new XElement(W + "r",
                        new XElement(W + "tab")
                    ),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                            new XElement(W + "sz", new XAttribute(W + "val", "22")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                            new XElement(W + "color", new XAttribute(W + "val", "000000"))
                        ),
                        new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), trimmed)
                    )
                );

                insertAfterElement.AddAfterSelf(pBullet);
                insertAfterElement = pBullet;
            }
        }

        private static void FormatCoverItem(XElement p, string label, string value)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr");
                p.AddFirst(pPr);
            }

            // Configurar tabulación a 3800 dxa (aprox 6.7 cm para alineación perfecta)
            var tabs = pPr.Element(W + "tabs");
            if (tabs == null)
            {
                tabs = new XElement(W + "tabs");
                pPr.Add(tabs);
            }
            tabs.Elements().Remove();
            tabs.Add(new XElement(W + "tab", new XAttribute(W + "val", "left"), new XAttribute(W + "pos", "3800")));

            // Espaciado entre líneas
            var sp = pPr.Element(W + "spacing");
            if (sp == null)
            {
                sp = new XElement(W + "spacing", new XAttribute(W + "after", "120"), new XAttribute(W + "line", "260"), new XAttribute(W + "lineRule", "auto"));
                pPr.Add(sp);
            }

            // Run de etiqueta (Negrita)
            // Run de etiqueta (Negrita)
            var rLabel = CreateRun(label, true);

            // Run del tabulador
            var rTab = new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                    new XElement(W + "sz", new XAttribute(W + "val", "22")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "22"))
                ),
                new XElement(W + "tab")
            );

            // Run del valor
            var rVal = CreateRun(value ?? string.Empty, true);

            p.Add(rLabel, rTab, rVal);
        }

        private static void FormatDirectorCert(XElement p, string cert)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr");
                p.AddFirst(pPr);
            }

            var tabs = pPr.Element(W + "tabs");
            if (tabs == null)
            {
                tabs = new XElement(W + "tabs");
                pPr.Add(tabs);
            }
            tabs.Elements().Remove();
            tabs.Add(new XElement(W + "tab", new XAttribute(W + "val", "left"), new XAttribute(W + "pos", "3800")));

            var rTab = new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                    new XElement(W + "sz", new XAttribute(W + "val", "22")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "22"))
                ),
                new XElement(W + "tab")
            );

            var rVal = CreateRun(cert ?? string.Empty, true);

            p.Add(rTab, rVal);
        }

        private static void FormatIntroParagraph1(XElement p, string projectName)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "both")));
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("En el proyecto ", false);
            var r2 = CreateRun(projectName ?? string.Empty, true);

            string remainder = " se realiza el análisis de protección contra fuego y medios de evacuación bajo el marco normativo del Reglamento Colombiano de Construcciones Sismo Resistentes NSR-10 promulgada por el Decreto 926 del 19 de marzo de 2010 y sus respectivas modificaciones en los decretos 926 de 2010 2525 del 13 de julio de 2010; 092 del 17 de enero de 2011; 340 del 13 de febrero de 2012 y 945 del 5 de junio de 2017. LA COMISIÓN ASESORA PERMANENTE PARA EL RÉGIMEN DE CONSTRUCCIONES SISMORESISTENTES estipula que la normativa NFPA puede ser referenciada en caso de requerir mayor análisis sobre PCI y protección de la vida. En el Acta #108 de la mencionada comisión, en reunión efectuada el 5 de marzo de 2012, se estableció en el numeral 46, la confirmación para aplicar la NFPA 101 en sistemas de protección pasiva y activa en los medios de evacuación.";
            var r3 = CreateRun(remainder, false);

            p.Add(r1, r2, r3);
        }

        private static void FormatIntroParagraph2(XElement p, string archDate, string projectName)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "both")));
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("El presente informe está realizado con base en la versión de arquitectura del ", false);
            var r2 = CreateRun(archDate ?? string.Empty, true);
            var r3 = CreateRun(", del proyecto ", false);
            var r4 = CreateRun(projectName ?? string.Empty, true);
            var r5 = CreateRun(".", false);

            p.Add(r1, r2, r3, r4, r5);
        }

        private static void SetParagraphNormalText(XElement p, string normalText)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "both")), new XElement(W + "spacing", new XAttribute(W + "after", "140")));
                p.AddFirst(pPr);
            }
            pPr.Elements(W + "rPr").Elements(W + "b").Remove();

            var r = CreateRun(normalText ?? string.Empty, false);
            p.Add(r);
        }

        private static void FormatMethodologyParagraph(XElement p, string archDate)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "both")), new XElement(W + "spacing", new XAttribute(W + "after", "140")));
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Bajo el marco normativo de NSR-10 en sus títulos J y K y NFPA 101 se evalúan los requerimientos de seguridad humana para el edificio, contrastándolos con las condiciones de la versión de arquitectura del ", false);
            var r2 = CreateRun(archDate ?? string.Empty, true);
            var r3 = CreateRun(".", false);

            p.Add(r1, r2, r3);
        }

        private static void FormatBuildingHeightParagraph(XElement p, double height, bool isHighRise)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            string heightStr = isHighRise
                ? $"aproximada de {height.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')}m"
                : $"{height.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')} m";

            string relationStr = isHighRise
                ? ", siendo superior de 23m desde el nivel de calle al último nivel habitable, por lo que se cataloga como edificio que "
                : ", siendo inferior de 23m desde el nivel de calle al último nivel habitable, por lo que se cataloga como edificio que ";

            string highRiseStatus = isHighRise ? "ES DE GRAN ALTURA" : "NO ES DE GRAN ALTURA";

            var r1 = CreateRun("El edificio tiene una altura de ", false);
            var r2 = CreateRun(heightStr, true);
            var r3 = CreateRun(relationStr, false);
            var r4 = CreateRun(highRiseStatus, true);
            var r5 = CreateRun(" de acuerdo con K.3.1.3 NSR-10 y 3.3.37.7 NFPA 101.", false);

            p.Add(r1, r2, r3, r4, r5);
        }

        private class Nsr10GroupDef
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public List<(string SubCode, string SubName)> Subgroups { get; set; } = new List<(string, string)>();
        }

        private static readonly Dictionary<string, Nsr10GroupDef> Nsr10Catalog = new Dictionary<string, Nsr10GroupDef>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "A", new Nsr10GroupDef
                {
                    Code = "A",
                    Name = "ALMACENAMIENTO",
                    Subgroups = new List<(string, string)> { ("(A-1)", "Riesgo moderado"), ("(A-2)", "Bajo riesgo") }
                }
            },
            {
                "C", new Nsr10GroupDef
                {
                    Code = "C",
                    Name = "COMERCIAL",
                    Subgroups = new List<(string, string)> { ("(C-1)", "Servicios"), ("(C-2)", "Bienes y productos") }
                }
            },
            {
                "E", new Nsr10GroupDef
                {
                    Code = "E",
                    Name = "ESPECIAL",
                    Subgroups = new List<(string, string)> { ("(E-1)", "Estructuras atípicas") }
                }
            },
            {
                "F", new Nsr10GroupDef
                {
                    Code = "F",
                    Name = "FABRIL E INDUSTRIAL",
                    Subgroups = new List<(string, string)> { ("(F-1)", "Riesgo moderado"), ("(F-2)", "Bajo riesgo") }
                }
            },
            {
                "I", new Nsr10GroupDef
                {
                    Code = "I",
                    Name = "INSTITUCIONAL",
                    Subgroups = new List<(string, string)> { ("(I-1)", "Reclusión"), ("(I-2)", "Salud o incapacidad"), ("(I-3)", "Educación"), ("(I-4)", "Seguridad pública"), ("(I-5)", "Servicio público") }
                }
            },
            {
                "L", new Nsr10GroupDef
                {
                    Code = "L",
                    Name = "LUGARES DE REUNIÓN",
                    Subgroups = new List<(string, string)> { ("(L-1)", "Deportivos y recreativos"), ("(L-2)", "Culturales y religiosos"), ("(L-3)", "Restaurantes y clubes"), ("(L-4)", "Transporte"), ("(L-5)", "Espacios abiertos") }
                }
            },
            {
                "M", new Nsr10GroupDef
                {
                    Code = "M",
                    Name = "MIXTO Y OTROS",
                    Subgroups = new List<(string, string)> { ("(M-1)", "Mixto") }
                }
            },
            {
                "P", new Nsr10GroupDef
                {
                    Code = "P",
                    Name = "ALTA PELIGROSIDAD",
                    Subgroups = new List<(string, string)> { ("(P-1)", "Alta peligrosidad") }
                }
            },
            {
                "R", new Nsr10GroupDef
                {
                    Code = "R",
                    Name = "RESIDENCIAL",
                    Subgroups = new List<(string, string)> { ("(R-1)", "Unifamiliar y bifamiliar"), ("(R-2)", "Multifamiliar"), ("(R-3)", "Hoteles") }
                }
            },
            {
                "T", new Nsr10GroupDef
                {
                    Code = "T",
                    Name = "TEMPORAL Y MISCELÁNEO",
                    Subgroups = new List<(string, string)> { ("(T-1)", "Temporal y misceláneo") }
                }
            }
        };

        private static XElement BuildWordClassificationTable(List<SghSpace> spaces, SghReportModel model)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "7800"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila de Encabezado
            var headerRow = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Grupos y subgrupos de ocupación", "2800", true, "D9D9D9", "000000", "center"),
                CreateCell("Clasificación", "5000", true, "D9D9D9", "000000", "center")
            );
            table.Add(headerRow);

            // Identificar los grupos presentes a partir de los espacios o parámetros
            var presentGroups = new SortedDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (spaces != null && spaces.Count > 0)
            {
                foreach (var s in spaces)
                {
                    string rawCode = (s.GrupoOcupacionNsr ?? s.GrupoOcupacion ?? string.Empty).Trim().Trim('(', ')');
                    if (string.IsNullOrWhiteSpace(rawCode)) continue;

                    string groupLetter = rawCode.Substring(0, 1).ToUpperInvariant();

                    if (!presentGroups.ContainsKey(groupLetter))
                    {
                        presentGroups[groupLetter] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    if (rawCode.Contains("-"))
                    {
                        presentGroups[groupLetter].Add(rawCode);
                    }
                }
            }

            // Si no hay grupos detectados de los espacios, usar los grupos estándar del proyecto (Img 4)
            if (presentGroups.Count == 0)
            {
                presentGroups["A"] = new HashSet<string> { "A-1" };
                presentGroups["C"] = new HashSet<string> { "C-1" };
                presentGroups["R"] = new HashSet<string> { "R-2", "R-3" };
            }

            foreach (var kvp in presentGroups)
            {
                string groupCode = kvp.Key;
                if (!Nsr10Catalog.TryGetValue(groupCode, out var groupDef))
                {
                    groupDef = new Nsr10GroupDef { Code = groupCode, Name = groupCode };
                }

                // Fila del Grupo Principal (Fondo Gris, Negrita, Centrado)
                var groupRow = new XElement(W + "tr",
                    CreateCell(groupDef.Code, "2800", true, "D9D9D9", "000000", "center"),
                    CreateCell(groupDef.Name, "5000", true, "D9D9D9", "000000", "center")
                );
                table.Add(groupRow);

                // Subgrupos
                var subSet = kvp.Value;
                var subList = new List<(string SubCode, string SubName)>();

                if (subSet != null && subSet.Count > 0)
                {
                    foreach (var sCode in subSet)
                    {
                        var match = groupDef.Subgroups.FirstOrDefault(sg => sg.SubCode.Equals($"({sCode})", StringComparison.OrdinalIgnoreCase) || sg.SubCode.Equals(sCode, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.SubCode))
                        {
                            subList.Add(match);
                        }
                        else
                        {
                            subList.Add(($"({sCode})", sCode));
                        }
                    }
                }
                else
                {
                    // Si no se especificó subcódigo, tomar todos o el primero del catálogo
                    if (groupDef.Subgroups.Count > 0)
                    {
                        subList.Add(groupDef.Subgroups[0]);
                    }
                }

                foreach (var sub in subList)
                {
                    var subRow = new XElement(W + "tr",
                        CreateCell(sub.SubCode, "2800", false, null, "000000", "center"),
                        CreateCell(sub.SubName, "5000", false, null, "000000", "center")
                    );
                    table.Add(subRow);
                }
            }

            return table;
        }

        private static void EliminateExtraCoverBlankPages(XDocument doc)
        {
            if (doc.Root == null) return;

            var allP = doc.Descendants(W + "p").ToList();
            XElement firstPageBreakP = null;

            foreach (var p in allP)
            {
                if (p.Descendants(W + "br").Any(b => b.Attribute(W + "type")?.Value == "page"))
                {
                    firstPageBreakP = p;
                    break;
                }
            }

            if (firstPageBreakP != null)
            {
                var prevElements = firstPageBreakP.ElementsBeforeSelf().Reverse().ToList();
                foreach (var el in prevElements)
                {
                    if (el.Name == W + "p")
                    {
                        string t = string.Concat(el.Descendants(W + "t").Select(x => x.Value)).Trim();
                        if (string.IsNullOrEmpty(t) && !el.Descendants(W + "drawing").Any())
                        {
                            el.Remove();
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private static void FormatClassificationIntroParagraph(XElement p, string buildingClass, string secondaryClass)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("De acuerdo con la clasificación de edificaciones estipulada en el capítulo K.2 se determina que el edificio se clasifica en ", false);
            var r2 = CreateRun(buildingClass ?? string.Empty, true);
            var r3 = CreateRun(", con espacios clasificados como ", false);
            var r4 = CreateRun(secondaryClass ?? string.Empty, true);
            var r5 = CreateRun(".", false);

            p.Add(r1, r2, r3, r4, r5);
        }

        private static void FormatFireResistanceParagraph(XElement p, string riskCategory, string fireResistance)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("La clasificación del proyecto corresponde a la ", false);
            var r2 = CreateRun(riskCategory ?? string.Empty, true);
            var r3 = CreateRun(". Según J.3.4 el proyecto deberá contemplar una ", false);
            var r4 = CreateRun("RF de acuerdo con la siguiente tabla", true);
            var r5 = CreateRun(":", false);

            p.Add(r1, r2, r3, r4, r5);
        }

        private static string FormatFireResistanceCode(string rf)
        {
            if (string.IsNullOrWhiteSpace(rf)) return "2H";
            if (rf.IndexOf("1", StringComparison.OrdinalIgnoreCase) >= 0 && rf.IndexOf("1/2", StringComparison.OrdinalIgnoreCase) < 0 && rf.IndexOf("1/4", StringComparison.OrdinalIgnoreCase) < 0) return "1H";
            if (rf.IndexOf("2", StringComparison.OrdinalIgnoreCase) >= 0) return "2H";
            if (rf.IndexOf("3", StringComparison.OrdinalIgnoreCase) >= 0) return "3H";
            if (rf.IndexOf("4", StringComparison.OrdinalIgnoreCase) >= 0) return "4H";
            return rf;
        }

        private static XElement BuildWordResidentialFireResistanceTable(SghReportModel model)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "8400"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila 1: Título de la tabla (Spans 4 columns)
            var row1 = new XElement(W + "tr",
                CreateCell("TABLA J.3.4-2", "8400", true, "FFFFFF", "000000", "center", 4)
            );
            table.Add(row1);

            // Fila 2: Subtítulo de la tabla (Spans 4 columns)
            var row2 = new XElement(W + "tr",
                CreateCell("RESISTENCIA REQUERIDA AL FUEGO PARA EDIFICACIONES DEL GRUPO RESIDENCIAL (R-1 Y R-2)", "8400", false, "FFFFFF", "000000", "center", 4)
            );
            table.Add(row2);

            // Fila 3: Encabezado principal (Elementos + Categoría)
            var row3 = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Elementos de la Construcción", "5100", true, "D9D9D9", "000000", "center"),
                CreateCell("Categoría según el número de pisos", "3300", true, "D9D9D9", "000000", "center", 3)
            );
            table.Add(row3);

            // Fila 4: Subencabezados de Categoría (I, II, III)
            string rCat = model.RiskCategory ?? string.Empty;
            bool isCat1 = rCat.IndexOf(" I", StringComparison.OrdinalIgnoreCase) >= 0 && rCat.IndexOf("II", StringComparison.OrdinalIgnoreCase) < 0 && rCat.IndexOf("III", StringComparison.OrdinalIgnoreCase) < 0;
            bool isCat2 = rCat.IndexOf("II", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCat3 = rCat.IndexOf("III", StringComparison.OrdinalIgnoreCase) >= 0;

            var row4 = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("", "5100", false, "D9D9D9", "000000", "center"),
                CreateCell("I (≥ 4 pisos)", "1100", true, isCat1 ? "BDD7EE" : "D9D9D9", "000000", "center"),
                CreateCell("II (2-3 pisos)", "1100", true, isCat2 ? "BDD7EE" : "D9D9D9", "000000", "center"),
                CreateCell("III (1 piso)", "1100", true, isCat3 ? "BDD7EE" : "D9D9D9", "000000", "center")
            );
            table.Add(row4);

            var rfRows = new List<(string Element, string C1, string C2, string C3)>
            {
                ("Muros Cortafuego", "2", "2", "1"),
                ("Muros de cerramiento de escaleras protegidas, ascensores, buitrones, ductos para basuras y corredores protegidos", "2", "1", "1"),
                ("Muros divisorios entre unidades", "1", "1", "1"),
                ("Muros interiores no portantes", "1/2", "1/4", "-"),
                ("Elementos estructurales de los materiales cubiertos por los títulos C a G del reglamento NSR-10", "2", "1", "1"),
                ("Cubiertas", "1", "1/2", "1/4"),
                ("Escaleras interiores no encerradas con muros corta fuego", "1", "1", "1")
            };

            foreach (var item in rfRows)
            {
                var dataRow = new XElement(W + "tr",
                    CreateCell(item.Element, "5100", false, null, "000000", "left"),
                    CreateCell(item.C1, "1100", isCat1, isCat1 ? "F2F7FA" : null, "000000", "center"),
                    CreateCell(item.C2, "1100", isCat2, isCat2 ? "F2F7FA" : null, "000000", "center"),
                    CreateCell(item.C3, "1100", isCat3, isCat3 ? "F2F7FA" : null, "000000", "center")
                );
                table.Add(dataRow);
            }

            return table;
        }

        private static XElement BuildWordFireResistanceTable(SghReportModel model)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "8400"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila 1: Título de la tabla (Spans 4 columns)
            var row1 = new XElement(W + "tr",
                CreateCell("TABLA J.3.4-3", "8400", true, "FFFFFF", "000000", "center", 4)
            );
            table.Add(row1);

            // Fila 2: Subtítulo de la tabla (Spans 4 columns)
            var row2 = new XElement(W + "tr",
                CreateCell("TABLA PARA OCUPACIONES EXCEPTO R-1 Y R-2", "8400", false, "FFFFFF", "000000", "center", 4)
            );
            table.Add(row2);

            // Fila 3: Encabezado principal (Elementos + Categoría)
            var row3 = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Elementos de la Construcción", "5100", true, "D9D9D9", "000000", "center"),
                CreateCell("Categoría según la clasificación Dada en", "3300", true, "D9D9D9", "000000", "center", 3)
            );
            table.Add(row3);

            // Fila 4: Subencabezados de Categoría (I, II, III)
            string rCat = model.RiskCategory ?? string.Empty;
            bool isCat1 = rCat.IndexOf(" I", StringComparison.OrdinalIgnoreCase) >= 0 && rCat.IndexOf("II", StringComparison.OrdinalIgnoreCase) < 0 && rCat.IndexOf("III", StringComparison.OrdinalIgnoreCase) < 0;
            bool isCat2 = rCat.IndexOf("II", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCat3 = rCat.IndexOf("III", StringComparison.OrdinalIgnoreCase) >= 0;

            var row4 = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("", "5100", false, "D9D9D9", "000000", "center"),
                CreateCell("I", "1100", true, isCat1 ? "BDD7EE" : "D9D9D9", "000000", "center"),
                CreateCell("II", "1100", true, isCat2 ? "BDD7EE" : "D9D9D9", "000000", "center"),
                CreateCell("III", "1100", true, isCat3 ? "BDD7EE" : "D9D9D9", "000000", "center")
            );
            table.Add(row4);

            var rfRows = new List<(string Element, string C1, string C2, string C3)>
            {
                ("Muros Cortafuego", "3", "2", "1"),
                ("Muros de cerramiento de escaleras protegidas, ascensores, buitrones, ductos para basuras y corredores protegidos", "2", "2", "1"),
                ("Muros divisorios entre unidades", "1", "1", "1"),
                ("Muros interiores no portantes", "1/2", "1/4", "-"),
                ("Elementos estructurales de los materiales cubiertos por los títulos C a G del reglamento NSR-10", "2", "1", "1"),
                ("Cubiertas", "1", "1", "1/2"),
                ("Escaleras interiores no encerradas con muros corta fuego", "2", "1", "1")
            };

            foreach (var item in rfRows)
            {
                var dataRow = new XElement(W + "tr",
                    CreateCell(item.Element, "5100", false, null, "000000", "left"),
                    CreateCell(item.C1, "1100", isCat1, isCat1 ? "F2F7FA" : null, "000000", "center"),
                    CreateCell(item.C2, "1100", isCat2, isCat2 ? "F2F7FA" : null, "000000", "center"),
                    CreateCell(item.C3, "1100", isCat3, isCat3 ? "F2F7FA" : null, "000000", "center")
                );
                table.Add(dataRow);
            }

            return table;
        }

        private static void ReplacePlaceholdersWithBoldRuns(XElement p, SghReportModel model)
        {
            string text = GetParagraphText(p);
            if (string.IsNullOrWhiteSpace(text)) return;

            var replacements = new List<(string Key, string Val)>
            {
                ("NOMBRE DE PROYECTO", model.ProjectName),
                ("FECHA BASE ARQ", model.ArchBaseDate),
                ("CLASIFICACIÓN EDIFICIO", model.BuildingClassification),
                ("CLASIFICACION EDIFICIO", model.BuildingClassification),
                ("CLASIFICACIÓN SECUNDARIA", model.SecondaryClassification),
                ("CLASIFICACION SECUNDARIA", model.SecondaryClassification),
                ("CATEGORIA PROYCTO", model.RiskCategory),
                ("CATEGORIA PROYECTO", model.RiskCategory),
                ("RESISTENCIA AL FUEGO GENERAL", model.GeneralFireResistance),
                ("REQUERIMIENTOS SISTEMA DE DETECCIÓN Y ALARMA", "contar con un sistema de detección y alarma en cumplimiento del capítulo J.4.2 y NFPA 72"),
                ("REQUERIMIENTOS SISTEMA DE DETECCION Y ALARMA", "contar con un sistema de detección y alarma en cumplimiento del capítulo J.4.2 y NFPA 72"),
                ("REQUERIMIENTOS SISTEMA DE EXTINCIÓN AUTOMÁTICA", model.HasSprinklers ? "contar con sistema de rociadores automáticos según J.4.3 y NFPA 13" : "evaluarse según área de construcción y uso bajo el capítulo J.4.3"),
                ("REQUERIMIENTOS SISTEMA DE EXTINCION AUTOMATICA", model.HasSprinklers ? "contar con sistema de rociadores automáticos según J.4.3 y NFPA 13" : "evaluarse según área de construcción y uso bajo el capítulo J.4.3"),
                ("REQUERIMIENTOS TOMAS FIJAS PARA BOMBEROS", "tomas fijas para bomberos y mangueras para extinción de incendios según J.4.4 y NFPA 14"),
                ("REQUERIMIENTOS EXTINTORES", "contar con extintores portátiles de fuego distribuidos según J.4.5 y NFPA 10")
            };

            bool hasMatches = replacements.Any(r => text.Contains(r.Key));
            if (!hasMatches) return;

            string current = text;
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            int idx = 0;
            while (idx < current.Length)
            {
                int bestPos = -1;
                string bestKey = null;
                string bestVal = null;

                foreach (var r in replacements)
                {
                    int pPos = current.IndexOf(r.Key, idx, StringComparison.Ordinal);
                    if (pPos >= 0 && (bestPos < 0 || pPos < bestPos))
                    {
                        bestPos = pPos;
                        bestKey = r.Key;
                        bestVal = r.Val;
                    }
                }

                if (bestPos >= 0)
                {
                    if (bestPos > idx)
                    {
                        string normalPiece = current.Substring(idx, bestPos - idx);
                        p.Add(CreateRun(normalPiece, false));
                    }

                    p.Add(CreateRun(bestVal ?? string.Empty, true));
                    idx = bestPos + bestKey.Length;
                }
                else
                {
                    string remaining = current.Substring(idx);
                    p.Add(CreateRun(remaining, false));
                    break;
                }
            }
        }

        private static void FormatExitsQuantityParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("De acuerdo con la clasificación de ocupación del edificio y la carga de ocupantes estudiada en el punto anterior, se debe tener como mínimo ", false);
            var r2 = CreateRun(model.SalidasRequeridas.ToString(), true);
            var r3 = CreateRun(" salidas accesibles desde cualquier punto de acuerdo con la norma. El espacio cuenta con ", false);
            var r4 = CreateRun(model.SalidasExistentes.ToString(), true);
            var r5 = CreateRun(" salidas, por lo que ", false);
            var r6 = CreateRun(model.CumplimientoCantidadSalidas ?? "Cumple", true);
            var r7 = CreateRun(" plenamente con el requerimiento.", false);

            p.Add(r1, r2, r3, r4, r5, r6, r7);
        }

        private static XElement BuildWordNumberOfExitsTable(List<SghSpace> spaces, SghReportModel model)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "9600"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila de Encabezado
            var headerRow = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Nivel / Piso", "2400", true, "D9D9D9", "000000", "center"),
                CreateCell("Carga de Ocupantes (p)", "1800", true, "D9D9D9", "000000", "center"),
                CreateCell("Salidas Requeridas", "1800", true, "D9D9D9", "000000", "center"),
                CreateCell("Salidas Existentes", "1800", true, "D9D9D9", "000000", "center"),
                CreateCell("Evaluación", "1800", true, "D9D9D9", "000000", "center")
            );
            table.Add(headerRow);

            if (model?.FloorExits != null && model.FloorExits.Count > 0)
            {
                var regularRows = model.FloorExits.Where(f => !f.IsTotal).ToList();
                var totalItem = model.FloorExits.FirstOrDefault(f => f.IsTotal);

                foreach (var f in regularRows)
                {
                    var dataRow = new XElement(W + "tr",
                        CreateCell(f.Piso, "2400", false, null, "000000", "left"),
                        CreateCell(f.Ocupacion.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(f.SalidasRequeridas.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(f.SalidasExistentes.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(f.Evaluacion ?? "Cumple", "1800", true, null, "000000", "center")
                    );
                    table.Add(dataRow);
                }

                if (totalItem != null)
                {
                    var totalRow = new XElement(W + "tr",
                        CreateCell(totalItem.Piso, "2400", true, "D9D9D9", "000000", "left"),
                        CreateCell(totalItem.Ocupacion.ToString(), "1800", true, "D9D9D9", "000000", "center"),
                        CreateCell(totalItem.SalidasRequeridas.ToString(), "1800", true, "D9D9D9", "000000", "center"),
                        CreateCell(totalItem.SalidasExistentes.ToString(), "1800", true, "D9D9D9", "000000", "center"),
                        CreateCell(totalItem.Evaluacion ?? "Cumple", "1800", true, "D9D9D9", "000000", "center")
                    );
                    table.Add(totalRow);
                }
            }
            else
            {
                var grouped = (spaces ?? new List<SghSpace>()).GroupBy(s => OccupancyService.GetCanonicalPisoKey(s.Piso)).ToList();
                int grandTotalOcc = 0;

                foreach (var group in grouped)
                {
                    int pisoOcc = 0;
                    foreach (var s in group)
                    {
                        double area = s.Area;
                        double? factor = OccupancyService.GetFactor_NSR10(s.GrupoOcupacionNsr);
                        int occ = (int.TryParse(s.CargaOcupacionNsr?.Trim(), out int val) && val > 0)
                            ? val
                            : (factor.HasValue && factor.Value > 0 ? (int)Math.Max(1, Math.Ceiling(area / factor.Value)) : 1);
                        pisoOcc += occ;
                    }

                    grandTotalOcc += pisoOcc;
                    int reqExits = pisoOcc <= 100 ? 1 : (pisoOcc <= 500 ? 2 : (pisoOcc <= 1000 ? 3 : 4));
                    int existExits = model?.SalidasExistentes > 0 ? model.SalidasExistentes : 2;
                    string eval = existExits >= reqExits ? "Cumple" : "No Cumple";

                    var dataRow = new XElement(W + "tr",
                        CreateCell(group.Key, "2400", false, null, "000000", "left"),
                        CreateCell(pisoOcc.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(reqExits.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(existExits.ToString(), "1800", false, null, "000000", "center"),
                        CreateCell(eval, "1800", true, null, "000000", "center")
                    );
                    table.Add(dataRow);
                }

                // Fila Total General
                int totalReqExits = (model != null && model.SalidasRequeridas > 0) ? model.SalidasRequeridas : (grandTotalOcc <= 100 ? 1 : (grandTotalOcc <= 500 ? 2 : (grandTotalOcc <= 1000 ? 3 : 4)));
                var totalRow = new XElement(W + "tr",
                    CreateCell("TOTAL EDIFICIO", "2400", true, "D9D9D9", "000000", "left"),
                    CreateCell(grandTotalOcc.ToString(), "1800", true, "D9D9D9", "000000", "center"),
                    CreateCell(totalReqExits.ToString(), "1800", true, "D9D9D9", "000000", "center"),
                    CreateCell((model?.SalidasExistentes ?? 2).ToString(), "1800", true, "D9D9D9", "000000", "center"),
                    CreateCell(model?.CumplimientoCantidadSalidas ?? "Cumple", "1800", true, "D9D9D9", "000000", "center")
                );
                table.Add(totalRow);
            }

            return table;
        }

        private static void FormatExitsEvaluationParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Evaluación: ", true);
            var r2 = CreateRun("De acuerdo con el cálculo de ocupación y las restricciones de cantidad de salidas para ", false);
            var r3 = CreateRun(model.BuildingClassification ?? string.Empty, true);
            var r4 = CreateRun(", ", false);
            var r5 = CreateRun(model.SecondaryClassification ?? string.Empty, true);
            var r6 = CreateRun(", el proyecto ", false);
            var r7 = CreateRun(model.CumplimientoCantidadSalidas ?? "Cumple", true);
            var r8 = CreateRun(" el requerimiento.", false);

            p.Add(r1, r2, r3, r4, r5, r6, r7, r8);
        }

        private static XElement BuildWordExitSeparationTable(List<SghSpace> spaces, SghReportModel model)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "9600"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila de Encabezado
            var headerRow = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Nivel / Piso", "2200", true, "D9D9D9", "000000", "center"),
                CreateCell("Diagonal Mayor D (m)", "2000", true, "D9D9D9", "000000", "center"),
                CreateCell("Separación Req. (m)", "2200", true, "D9D9D9", "000000", "center"),
                CreateCell("Separación Existente (m)", "2000", true, "D9D9D9", "000000", "center"),
                CreateCell("Evaluación", "1200", true, "D9D9D9", "000000", "center")
            );
            table.Add(headerRow);

            var grouped = spaces.GroupBy(s => OccupancyService.GetCanonicalPisoKey(s.Piso)).ToList();
            double sepReqVal = model.HasSprinklers ? (model.DiagonalEdificioM / 3.0) : (model.DiagonalEdificioM / 2.0);
            string sepReqText = model.HasSprinklers
                ? $"{sepReqVal.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}m (D/3)"
                : $"{sepReqVal.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}m (D/2)";
            string sepExistText = $"{model.SeparacionSalidasExistenteM.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}m";
            string diagText = $"{model.DiagonalEdificioM.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')}m";

            foreach (var group in grouped)
            {
                var dataRow = new XElement(W + "tr",
                    CreateCell(group.Key, "2200", false, null, "000000", "left"),
                    CreateCell(diagText, "2000", false, null, "000000", "center"),
                    CreateCell(sepReqText, "2200", false, null, "000000", "center"),
                    CreateCell(sepExistText, "2000", false, null, "000000", "center"),
                    CreateCell(model.CumplimientoSeparacionSalidas ?? "Cumple", "1200", true, null, "000000", "center")
                );
                table.Add(dataRow);
            }

            return table;
        }

        private static void FormatExitSeparationEvaluationParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Evaluación: ", true);
            var r2 = CreateRun("El proyecto ", false);
            var r3 = CreateRun(model.CumplimientoSeparacionSalidas ?? "Cumple", true);
            var r4 = CreateRun(" con la separación entre salidas.", false);

            p.Add(r1, r2, r3, r4);
        }

        private static void FormatExitDischargeEvaluationParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Evaluación: ", true);
            var r2 = CreateRun("El edificio actualmente ", false);
            var r3 = CreateRun(model.CumplimientoDescargaSalidas ?? "Cumple", true);
            var r4 = CreateRun(" con este requerimiento.", false);

            p.Add(r1, r2, r3, r4);
        }

        private static void FormatTravelDistanceParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            string distReq = $"{model.DistanciaRecorridoMaxPermitidaM.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')}m";

            var r1 = CreateRun("La distancia máxima de recorrido desde el punto más alejado hasta el centro de cualquier salida de emergencia, en ocupación ", false);
            var r2 = CreateRun(model.BuildingClassification ?? string.Empty, true);
            var r3 = CreateRun(" no debe sobrepasar ", false);
            var r4 = CreateRun(distReq, true);
            var r5 = CreateRun(", y en A-1 60m sin sistema de rociadores y 75m con sistema de rociadores según K.3.6-1 de la norma. Validación de distancias de recorrido:", false);

            p.Add(r1, r2, r3, r4, r5);
        }

        private static void FormatTravelDistanceEvaluationParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Evaluación: ", true);
            var r2 = CreateRun("Actualmente el edificio ", false);
            var r3 = CreateRun(model.CumplimientoDistanciaRecorrido ?? "Cumple", true);
            var r4 = CreateRun(" con el requerimiento normativo.", false);

            p.Add(r1, r2, r3, r4);
        }

        private static void FormatEgressCapacityEvaluationParagraph(XElement p, SghReportModel model)
        {
            p.Elements().Where(e => e.Name != W + "pPr").Remove();

            var pPr = p.Element(W + "pPr");
            if (pPr == null)
            {
                pPr = new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")),
                    new XElement(W + "ind", new XAttribute(W + "left", "792")),
                    new XElement(W + "jc", new XAttribute(W + "val", "both"))
                );
                p.AddFirst(pPr);
            }

            var r1 = CreateRun("Evaluación: ", true);
            var r2 = CreateRun("Los medios de evacuación del edificio ", false);
            var r3 = CreateRun(model.CumplimientoCapacidadMedios ?? "Cumple", true);
            var r4 = CreateRun(", con el ancho mínimo definido por la NSR-10, de acuerdo con la revisión de ocupaciones.", false);

            p.Add(r1, r2, r3, r4);
        }

        private static XElement CreateRun(string text, bool isBold)
        {
            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                new XElement(W + "sz", new XAttribute(W + "val", "22")),
                new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                new XElement(W + "color", new XAttribute(W + "val", "000000"))
            );

            if (isBold)
            {
                rPr.Add(new XElement(W + "b"));
            }

            var t = new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? string.Empty);
            return new XElement(W + "r", rPr, t);
        }

        private static XElement BuildImageParagraph(string rId, string imgTitle, long cx, long cy)
        {
            int docPrId = (int)(DateTime.Now.Ticks % 100000);
            return new XElement(W + "p",
                new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "center")), new XElement(W + "spacing", new XAttribute(W + "after", "180"))),
                new XElement(W + "r",
                    new XElement(W + "drawing",
                        new XElement(WP + "inline",
                            new XAttribute("distT", "0"), new XAttribute("distB", "0"), new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                            new XElement(WP + "extent", new XAttribute("cx", cx.ToString()), new XAttribute("cy", cy.ToString())),
                            new XElement(WP + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")),
                            new XElement(WP + "docPr", new XAttribute("id", docPrId.ToString()), new XAttribute("name", imgTitle)),
                            new XElement(WP + "cNvGraphicFramePr",
                                new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))
                            ),
                            new XElement(A + "graphic",
                                new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                    new XElement(PIC + "pic",
                                        new XElement(PIC + "nvPicPr",
                                            new XElement(PIC + "cNvPr", new XAttribute("id", docPrId.ToString()), new XAttribute("name", imgTitle)),
                                            new XElement(PIC + "cNvPicPr")
                                        ),
                                        new XElement(PIC + "blipFill",
                                            new XElement(A + "blip", new XAttribute(R + "embed", rId)),
                                            new XElement(A + "stretch", new XElement(A + "fillRect"))
                                        ),
                                        new XElement(PIC + "spPr",
                                            new XElement(A + "xfrm",
                                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                                new XElement(A + "ext", new XAttribute("cx", cx.ToString()), new XAttribute("cy", cy.ToString()))
                                            ),
                                            new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
            );
        }

        private static void UpdateDocumentTables(XDocument doc, SghReportModel model)
        {
            var tables = doc.Descendants(W + "tbl").ToList();
            foreach (var tbl in tables)
            {
                var rows = tbl.Descendants(W + "tr").ToList();
                foreach (var row in rows)
                {
                    string rowText = string.Concat(row.Descendants(W + "t").Select(t => t.Value));

                    // Tabla de firmas / Aprobaciones
                    if (rowText.Contains("Elaboró") || rowText.Contains("Elaboro"))
                    {
                        ReplaceInRow(row, "Coordinadora Senior", model.ElaboroCargo);
                        ReplaceInRow(row, "Alejandra Velandia", model.ElaboroNombre);
                        ReplaceInRow(row, "2021/11/29", model.ElaboroFecha);
                    }
                    else if (rowText.Contains("Revisó") || rowText.Contains("Reviso"))
                    {
                        ReplaceInRow(row, "Responsable de SGC", model.RevisoCargo);
                        ReplaceInRow(row, "Catherin Zamora", model.RevisoNombre);
                        ReplaceInRow(row, "2021/11/29", model.RevisoFecha);
                    }
                    else if (rowText.Contains("Aprobó") || rowText.Contains("Aprobo"))
                    {
                        ReplaceInRow(row, "Responsable de SGC", model.AproboCargo);
                        ReplaceInRow(row, "Catherin Zamora", model.AproboNombre);
                        ReplaceInRow(row, "2021/11/29", model.AproboFecha);
                    }
                    else if (rowText.Contains("Emisión del documento") || rowText.Contains("Emision del documento"))
                    {
                        ReplaceInRow(row, "01", model.Version);
                        ReplaceInRow(row, "Emisión del documento", model.VersionDescription);
                        ReplaceInRow(row, "2021/08/19", model.ReportDate);
                        ReplaceInRow(row, "Catherin Zamora", model.AproboNombre);
                    }
                    // Tabla de Historial del Documento en la portada
                    else if (rowText.Contains("Versión 1") || rowText.Contains("Version 1") || rowText.Contains("XXXX/XX/XX"))
                    {
                        ReplaceInRow(row, "Versión 1", $"Versión {model.Version}");
                        ReplaceInRow(row, "Version 1", $"Versión {model.Version}");
                        ReplaceInRow(row, "XXXX/XX/XX", model.ReportDate);
                    }
                }
            }
        }

        private static void ReplaceInRow(XElement row, string oldVal, string newVal)
        {
            if (string.IsNullOrEmpty(oldVal) || string.IsNullOrEmpty(newVal)) return;
            foreach (var cell in row.Descendants(W + "tc"))
            {
                string cText = string.Concat(cell.Descendants(W + "t").Select(t => t.Value));
                if (cText.Contains(oldVal))
                {
                    foreach (var p in cell.Descendants(W + "p"))
                    {
                        string pText = GetParagraphText(p);
                        if (pText.Contains(oldVal))
                        {
                            SetParagraphSingleText(p, pText.Replace(oldVal, newVal));
                        }
                    }
                }
            }
        }

        private static string GetParagraphText(XElement p)
        {
            return string.Concat(p.Descendants(W + "t").Select(t => t.Value));
        }

        private static void SetParagraphSingleText(XElement p, string newText)
        {
            var firstT = p.Descendants(W + "t").FirstOrDefault();
            if (firstT != null)
            {
                firstT.Value = newText;
                var otherRuns = p.Elements(W + "r").Skip(1).ToList();
                foreach (var r in otherRuns)
                {
                    if (!r.Descendants(W + "drawing").Any())
                    {
                        r.Remove();
                    }
                }
            }
        }

        /// <summary>
        /// Construye una tabla OpenXML con el desglose de áreas, índices y cargas de ocupación.
        /// </summary>
        private static XElement BuildWordOccupancyTable(List<SghSpace> spaces)
        {
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "9600"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "tblBorders",
                        new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000")),
                        new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "000000"))
                    )
                )
            );

            // Fila de Encabezado
            var headerRow = new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "tblHeader")),
                CreateCell("Nivel / Piso", "1400", true, "D9D9D9", "000000", "center"),
                CreateCell("No.", "600", true, "D9D9D9", "000000", "center"),
                CreateCell("Espacio", "2400", true, "D9D9D9", "000000", "center"),
                CreateCell("Uso NSR-10", "1800", true, "D9D9D9", "000000", "center"),
                CreateCell("Área (m²)", "1100", true, "D9D9D9", "000000", "center"),
                CreateCell("Factor (m²/p)", "1100", true, "D9D9D9", "000000", "center"),
                CreateCell("Ocupantes", "1200", true, "D9D9D9", "000000", "center")
            );
            table.Add(headerRow);

            var grouped = spaces.GroupBy(s => OccupancyService.GetCanonicalPisoKey(s.Piso)).ToList();
            double grandTotalArea = 0;
            int grandTotalOcc = 0;

            foreach (var group in grouped)
            {
                double pisoArea = 0;
                int pisoOcc = 0;

                foreach (var s in group)
                {
                    double area = s.Area;
                    double? factor = OccupancyService.GetFactor_NSR10(s.GrupoOcupacionNsr);
                    int occ = (int.TryParse(s.CargaOcupacionNsr?.Trim(), out int val) && val > 0)
                        ? val
                        : (factor.HasValue && factor.Value > 0 ? (int)Math.Max(1, Math.Ceiling(area / factor.Value)) : 1);

                    pisoArea += area;
                    pisoOcc += occ;

                    var dataRow = new XElement(W + "tr",
                        CreateCell(s.Piso, "1400", false, null, "000000", "left"),
                        CreateCell(s.Numero, "600", false, null, "000000", "center"),
                        CreateCell(s.Espacio, "2400", false, null, "000000", "left"),
                        CreateCell(s.GrupoOcupacionNsr, "1800", false, null, "000000", "left"),
                        CreateCell(area.ToString("0.00", CultureInfo.InvariantCulture), "1100", false, null, "000000", "right"),
                        CreateCell(factor.HasValue ? factor.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-", "1100", false, null, "000000", "center"),
                        CreateCell(occ.ToString(), "1200", false, null, "000000", "center")
                    );
                    table.Add(dataRow);
                }

                grandTotalArea += pisoArea;
                grandTotalOcc += pisoOcc;

                // Fila Subtotal por Piso
                var subtotalRow = new XElement(W + "tr",
                    CreateCell($"Subtotal {group.Key}", "6200", true, "F2F2F2", "000000", "left", 4),
                    CreateCell(pisoArea.ToString("0.00", CultureInfo.InvariantCulture), "1100", true, "F2F2F2", "000000", "right"),
                    CreateCell("-", "1100", true, "F2F2F2", "000000", "center"),
                    CreateCell(pisoOcc.ToString(), "1200", true, "F2F2F2", "000000", "center")
                );
                table.Add(subtotalRow);
            }

            // Fila Total General
            var totalRow = new XElement(W + "tr",
                CreateCell("TOTAL EDIFICIO", "6200", true, "D9D9D9", "000000", "left", 4),
                CreateCell(grandTotalArea.ToString("0.00", CultureInfo.InvariantCulture), "1100", true, "D9D9D9", "000000", "right"),
                CreateCell("-", "1100", true, "D9D9D9", "000000", "center"),
                CreateCell(grandTotalOcc.ToString(), "1200", true, "D9D9D9", "000000", "center")
            );
            table.Add(totalRow);

            return table;
        }

        private static XElement CreateCell(string text, string widthDxa, bool isBold = false, string fillColorHex = null, string textColorHex = "000000", string align = "left", int gridSpan = 1)
        {
            var tcPr = new XElement(W + "tcPr",
                new XElement(W + "tcW", new XAttribute(W + "w", widthDxa), new XAttribute(W + "type", "dxa")),
                new XElement(W + "vAlign", new XAttribute(W + "val", "center"))
            );

            if (!string.IsNullOrEmpty(fillColorHex))
            {
                tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", fillColorHex)));
            }

            if (gridSpan > 1)
            {
                tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", gridSpan.ToString())));
            }

            var pPr = new XElement(W + "pPr");
            if (align != "left")
            {
                pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", align == "center" ? "center" : "right")));
            }

            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts", new XAttribute(W + "ascii", "Arial Narrow"), new XAttribute(W + "hAnsi", "Arial Narrow")),
                new XElement(W + "sz", new XAttribute(W + "val", "20")),
                new XElement(W + "color", new XAttribute(W + "val", textColorHex))
            );

            if (isBold)
            {
                rPr.Add(new XElement(W + "b"));
            }

            var t = new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? string.Empty);
            var r = new XElement(W + "r", rPr, t);
            var p = new XElement(W + "p", pPr, r);

            return new XElement(W + "tc", tcPr, p);
        }
    }
}
