using System;
using System.IO;
using System.Xml.Serialization;

namespace AutoCAD.SGH.Models
{
    public class SghReportModel
    {
        // Portada y Metadatos
        public string ProjectName { get; set; } = "PROYECTO SGH";
        public string ProjectDescription { get; set; } = "Edificación con espacios destinados a diferentes usos y servicios bajo normativa NSR-10 y NFPA 101.";
        public string ReportDate { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");
        public string ArchBaseDate { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");
        public string Version { get; set; } = "01";
        public string VersionDescription { get; set; } = "Emisión del documento";

        // Equipo de Trabajo
        public string DirectorName { get; set; } = "Ing. Pedro Diaz";
        public string DirectorCert { get; set; } = "Certificado NFPA 1 & 101 & 72";
        public string CoordinatorName { get; set; } = "COORDINADOR PROYECTO";
        public string AuthorName { get; set; } = "ELABORADOR";
        public string CollaboratorName { get; set; } = "COLABORADOR";

        // Tabla de Aprobaciones
        public string ElaboroCargo { get; set; } = "Coordinadora Senior";
        public string ElaboroNombre { get; set; } = "Alejandra Velandia";
        public string ElaboroFecha { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");

        public string RevisoCargo { get; set; } = "Responsable de SGC";
        public string RevisoNombre { get; set; } = "Catherin Zamora";
        public string RevisoFecha { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");

        public string AproboCargo { get; set; } = "Responsable de SGC";
        public string AproboNombre { get; set; } = "Catherin Zamora";
        public string AproboFecha { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");

        // Parámetros del Edificio
        public string SelectedNorm { get; set; } = "NSR-10";
        public double BuildingHeight { get; set; } = 15.0;
        public bool IsHighRise => BuildingHeight > 23.0;
        public string RiskCategory { get; set; } = "Categoría II";
        public string GeneralFireResistance { get; set; } = "2 Horas";
        public string BuildingClassification { get; set; } = "Comercial (C)";
        public string SecondaryClassification { get; set; } = "Almacenamiento (A)";
        public bool HasSprinklers { get; set; } = false;

        public bool IsResidentialR1R2
        {
            get
            {
                if (string.IsNullOrWhiteSpace(BuildingClassification)) return false;
                string bc = BuildingClassification.Trim();
                if (bc.IndexOf("R-1", StringComparison.OrdinalIgnoreCase) >= 0 || 
                    bc.IndexOf("R-2", StringComparison.OrdinalIgnoreCase) >= 0 || 
                    bc.IndexOf("R1", StringComparison.OrdinalIgnoreCase) >= 0 || 
                    bc.IndexOf("R2", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (bc.IndexOf("Residencial", StringComparison.OrdinalIgnoreCase) >= 0 && 
                    bc.IndexOf("Hoteles", StringComparison.OrdinalIgnoreCase) < 0 && 
                    bc.IndexOf("R-3", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;

                return false;
            }
        }

        // Medios de Evacuación y Salidas (NSR-10 K.3)
        public int OcupacionTotalPiso { get; set; } = 0;
        public int SalidasRequeridas { get; set; } = 2;
        public int SalidasExistentes { get; set; } = 2;
        public string CumplimientoCantidadSalidas { get; set; } = "Cumple";
        public System.Collections.Generic.List<SghFloorExitItem> FloorExits { get; set; } = new System.Collections.Generic.List<SghFloorExitItem>();

        public double DiagonalEdificioM { get; set; } = 45.0;
        public double SeparacionSalidasExistenteM { get; set; } = 22.5;
        public string CumplimientoSeparacionSalidas { get; set; } = "Cumple";

        public string CumplimientoDescargaSalidas { get; set; } = "Cumple";

        public double DistanciaRecorridoMaxPermitidaM { get; set; } = 45.0;
        public double DistanciaRecorridoExistenteM { get; set; } = 32.0;
        public string CumplimientoDistanciaRecorrido { get; set; } = "Cumple";

        public string CumplimientoCapacidadMedios { get; set; } = "Cumple";

        // Conclusiones del Documento
        public const string DefaultConclusionsText = 
@"• El proyecto debe contar con sistema de rociadores en la totalidad.
• Se requieren tomas para bomberos y extintores manuales.
• Las escaleras de emergencia, al ser interiores deben contar con presurización.
• Se requiere contemplar en el diseño de iluminación el uso de señales iluminadas según los planos de señalización.
• Se debe garantizar el cumplimiento de resistencia al fuego de dos horas de la estructura, sea por diseño propio de los elementos de la misma o con recubrimientos como pinturas certificadas.
• El proyecto no cumple en la totalidad con los requisitos de cantidad y separación de salidas, sin embargo, se separan los pasillos residenciales para mitigar esto y se limita la ocupación del sótano para usar solo 1 salida.
• El proyecto incorporará pasillo protegido que descargue directamente la escalera al exterior.
• La arquitectura del proyecto debe garantizar la separación interna de la escalera tipo tijera.";

        public string Conclusiones { get; set; } = DefaultConclusionsText;

        // Imágenes del Proyecto
        public string ImplantacionImagePath { get; set; } = string.Empty;
        public string LocalizacionImagePath { get; set; } = string.Empty;

        // Opciones de Salida
        public bool GenerateExcel { get; set; } = true;
        public bool GenerateWord { get; set; } = true;
        public string ExcelOutputPath { get; set; } = string.Empty;
        public string WordOutputPath { get; set; } = string.Empty;

        // Rutas de configuración
        private static string GetConfigFilePath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dictaDir = Path.Combine(appData, "DICTA");
                if (!Directory.Exists(dictaDir))
                {
                    Directory.CreateDirectory(dictaDir);
                }
                return Path.Combine(dictaDir, "sgh_report_defaults.xml");
            }
            catch
            {
                return null;
            }
        }

        public void SaveDefaults()
        {
            try
            {
                string path = GetConfigFilePath();
                if (string.IsNullOrEmpty(path)) return;

                var defaults = new SghReportDefaults
                {
                    DirectorName = this.DirectorName,
                    DirectorCert = this.DirectorCert,
                    CoordinatorName = this.CoordinatorName,
                    AuthorName = this.AuthorName,
                    CollaboratorName = this.CollaboratorName,
                    ElaboroCargo = this.ElaboroCargo,
                    ElaboroNombre = this.ElaboroNombre,
                    RevisoCargo = this.RevisoCargo,
                    RevisoNombre = this.RevisoNombre,
                    AproboCargo = this.AproboCargo,
                    AproboNombre = this.AproboNombre,
                    RiskCategory = this.RiskCategory,
                    GeneralFireResistance = this.GeneralFireResistance,
                    BuildingClassification = this.BuildingClassification,
                    SecondaryClassification = this.SecondaryClassification,
                    HasSprinklers = this.HasSprinklers,
                    SalidasRequeridas = this.SalidasRequeridas,
                    SalidasExistentes = this.SalidasExistentes,
                    CumplimientoCantidadSalidas = this.CumplimientoCantidadSalidas,
                    DiagonalEdificioM = this.DiagonalEdificioM,
                    SeparacionSalidasExistenteM = this.SeparacionSalidasExistenteM,
                    CumplimientoSeparacionSalidas = this.CumplimientoSeparacionSalidas,
                    CumplimientoDescargaSalidas = this.CumplimientoDescargaSalidas,
                    DistanciaRecorridoMaxPermitidaM = this.DistanciaRecorridoMaxPermitidaM,
                    DistanciaRecorridoExistenteM = this.DistanciaRecorridoExistenteM,
                    CumplimientoDistanciaRecorrido = this.CumplimientoDistanciaRecorrido,
                    CumplimientoCapacidadMedios = this.CumplimientoCapacidadMedios,
                    Conclusiones = this.Conclusiones
                };

                var serializer = new XmlSerializer(typeof(SghReportDefaults));
                using (var writer = new StreamWriter(path))
                {
                    serializer.Serialize(writer, defaults);
                }
            }
            catch { }
        }

        public static SghReportModel LoadWithDefaults(string drawingName)
        {
            var model = new SghReportModel();

            // Asignar nombre del proyecto a partir del DWG si aplica
            if (!string.IsNullOrWhiteSpace(drawingName))
            {
                string fileNameOnly = Path.GetFileNameWithoutExtension(drawingName);
                if (!string.IsNullOrWhiteSpace(fileNameOnly) && fileNameOnly != "Drawing1" && fileNameOnly != "Dibujo")
                {
                    model.ProjectName = fileNameOnly.Replace('_', ' ');
                }
            }

            try
            {
                string path = GetConfigFilePath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var serializer = new XmlSerializer(typeof(SghReportDefaults));
                    using (var reader = new StreamReader(path))
                    {
                        var defaults = (SghReportDefaults)serializer.Deserialize(reader);
                        if (defaults != null)
                        {
                            if (!string.IsNullOrEmpty(defaults.DirectorName)) model.DirectorName = defaults.DirectorName;
                            if (!string.IsNullOrEmpty(defaults.DirectorCert)) model.DirectorCert = defaults.DirectorCert;
                            if (!string.IsNullOrEmpty(defaults.CoordinatorName)) model.CoordinatorName = defaults.CoordinatorName;
                            if (!string.IsNullOrEmpty(defaults.AuthorName)) model.AuthorName = defaults.AuthorName;
                            if (!string.IsNullOrEmpty(defaults.CollaboratorName)) model.CollaboratorName = defaults.CollaboratorName;

                            if (!string.IsNullOrEmpty(defaults.ElaboroCargo)) model.ElaboroCargo = defaults.ElaboroCargo;
                            if (!string.IsNullOrEmpty(defaults.ElaboroNombre)) model.ElaboroNombre = defaults.ElaboroNombre;
                            if (!string.IsNullOrEmpty(defaults.RevisoCargo)) model.RevisoCargo = defaults.RevisoCargo;
                            if (!string.IsNullOrEmpty(defaults.RevisoNombre)) model.RevisoNombre = defaults.RevisoNombre;
                            if (!string.IsNullOrEmpty(defaults.AproboCargo)) model.AproboCargo = defaults.AproboCargo;
                            if (!string.IsNullOrEmpty(defaults.AproboNombre)) model.AproboNombre = defaults.AproboNombre;

                            if (!string.IsNullOrEmpty(defaults.RiskCategory)) model.RiskCategory = defaults.RiskCategory;
                            if (!string.IsNullOrEmpty(defaults.GeneralFireResistance)) model.GeneralFireResistance = defaults.GeneralFireResistance;
                            if (!string.IsNullOrEmpty(defaults.BuildingClassification)) model.BuildingClassification = defaults.BuildingClassification;
                            if (!string.IsNullOrEmpty(defaults.SecondaryClassification)) model.SecondaryClassification = defaults.SecondaryClassification;
                            model.HasSprinklers = defaults.HasSprinklers;

                            if (defaults.SalidasRequeridas > 0) model.SalidasRequeridas = defaults.SalidasRequeridas;
                            if (defaults.SalidasExistentes > 0) model.SalidasExistentes = defaults.SalidasExistentes;
                            if (!string.IsNullOrEmpty(defaults.CumplimientoCantidadSalidas)) model.CumplimientoCantidadSalidas = defaults.CumplimientoCantidadSalidas;

                            if (defaults.DiagonalEdificioM > 0) model.DiagonalEdificioM = defaults.DiagonalEdificioM;
                            if (defaults.SeparacionSalidasExistenteM > 0) model.SeparacionSalidasExistenteM = defaults.SeparacionSalidasExistenteM;
                            if (!string.IsNullOrEmpty(defaults.CumplimientoSeparacionSalidas)) model.CumplimientoSeparacionSalidas = defaults.CumplimientoSeparacionSalidas;

                            if (!string.IsNullOrEmpty(defaults.CumplimientoDescargaSalidas)) model.CumplimientoDescargaSalidas = defaults.CumplimientoDescargaSalidas;

                            if (defaults.DistanciaRecorridoMaxPermitidaM > 0) model.DistanciaRecorridoMaxPermitidaM = defaults.DistanciaRecorridoMaxPermitidaM;
                            if (defaults.DistanciaRecorridoExistenteM > 0) model.DistanciaRecorridoExistenteM = defaults.DistanciaRecorridoExistenteM;
                            if (!string.IsNullOrEmpty(defaults.CumplimientoDistanciaRecorrido)) model.CumplimientoDistanciaRecorrido = defaults.CumplimientoDistanciaRecorrido;

                            if (!string.IsNullOrEmpty(defaults.CumplimientoCapacidadMedios)) model.CumplimientoCapacidadMedios = defaults.CumplimientoCapacidadMedios;

                            if (!string.IsNullOrEmpty(defaults.Conclusiones)) model.Conclusiones = defaults.Conclusiones;
                        }
                    }
                }
            }
            catch { }

            return model;
        }
    }

    public class SghReportDefaults
    {
        public string DirectorName { get; set; }
        public string DirectorCert { get; set; }
        public string CoordinatorName { get; set; }
        public string AuthorName { get; set; }
        public string CollaboratorName { get; set; }

        public string ElaboroCargo { get; set; }
        public string ElaboroNombre { get; set; }
        public string RevisoCargo { get; set; }
        public string RevisoNombre { get; set; }
        public string AproboCargo { get; set; }
        public string AproboNombre { get; set; }

        public string RiskCategory { get; set; }
        public string GeneralFireResistance { get; set; }
        public string BuildingClassification { get; set; }
        public string SecondaryClassification { get; set; }
        public bool HasSprinklers { get; set; }

        public int SalidasRequeridas { get; set; } = 2;
        public int SalidasExistentes { get; set; } = 2;
        public string CumplimientoCantidadSalidas { get; set; } = "Cumple";

        public double DiagonalEdificioM { get; set; } = 45.0;
        public double SeparacionSalidasExistenteM { get; set; } = 22.5;
        public string CumplimientoSeparacionSalidas { get; set; } = "Cumple";

        public string CumplimientoDescargaSalidas { get; set; } = "Cumple";

        public double DistanciaRecorridoMaxPermitidaM { get; set; } = 45.0;
        public double DistanciaRecorridoExistenteM { get; set; } = 32.0;
        public string CumplimientoDistanciaRecorrido { get; set; } = "Cumple";

        public string CumplimientoCapacidadMedios { get; set; } = "Cumple";
        public string Conclusiones { get; set; }
    }

    public class SghFloorExitItem
    {
        public string Piso { get; set; } = string.Empty;
        public int Ocupacion { get; set; } = 0;
        public int SalidasRequeridas { get; set; } = 1;
        public int SalidasExistentes { get; set; } = 2;
        public string Evaluacion { get; set; } = "Cumple";
        public bool IsTotal { get; set; } = false;
    }
}
