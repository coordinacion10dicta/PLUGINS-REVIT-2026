using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MiNamespace.ValidadorParametros
{
    public class CategoryComplianceModel
    {
        public string Nombre        { get; set; }
        public int    TotalErrores  { get; set; }
        public int    Criticos      { get; set; }
    }

    public class DisciplineComplianceModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Nombre                        { get; set; }
        public int    TotalElementos                { get; set; }
        public int    ElementosConErrores           { get; set; }
        public int    TotalErrores                  { get; set; }
        public int    ErroresCriticos               { get; set; }
        public List<CategoryComplianceModel> Categorias { get; set; } = new List<CategoryComplianceModel>();

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public double CompliancePct => TotalElementos > 0
            ? Math.Round((TotalElementos - ElementosConErrores) * 100.0 / TotalElementos, 1)
            : 100.0;

        public string ComplianceTexto   => $"{CompliancePct:0}%";
        public string ResumenErrores    => $"{ErroresCriticos} crítico(s) · {TotalErrores} total";

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ValidationSummary
    {
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
        public int TotalFamilias            { get; set; }
        public int TotalElementos           { get; set; }
    }
}
