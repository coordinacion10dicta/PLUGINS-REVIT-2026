using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MiNamespace.Learning;

namespace MiNamespace.UI
{
    public partial class LearningView : Window
    {
        private readonly LearningSystem _learningSystem;

        public LearningView()
        {
            InitializeComponent();
            _learningSystem = new LearningSystem();
            LoadPendingItems();
        }

        private void LoadPendingItems()
        {
            var items = _learningSystem.GetPendingMappings().ToList();
            gridPending.ItemsSource = items;
            txtSummary.Text = items.Any()
                ? $"Pendientes: {items.Count}. Priorizados por frecuencia, confianza y recurrencia reciente."
                : "No hay elementos pendientes de revision.";

            if (items.Any())
                gridPending.SelectedIndex = 0;
            else
                ClearDetailPanel();
        }

        private void BtnApprove_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                ShowValidation("Selecciona un registro antes de aprobar.");
                return;
            }

            string proposedValue = (txtEditField.Text ?? string.Empty).Trim();
            string baselineValue = (item.Suggestion ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(proposedValue) &&
                !string.Equals(proposedValue, baselineValue, StringComparison.OrdinalIgnoreCase))
            {
                ShowValidation("Si corriges el campo sugerido, usa el boton Editar para dejar trazabilidad completa.");
                return;
            }

            var result = _learningSystem.Approve(item, txtEditField.Text);
            HandleActionResult(result);
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                ShowValidation("Selecciona un registro antes de editar.");
                return;
            }

            var result = _learningSystem.Edit(item, txtEditField.Text);
            HandleActionResult(result);
        }

        private void BtnReject_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                ShowValidation("Selecciona un registro antes de rechazar.");
                return;
            }

            var result = _learningSystem.Reject(item);
            HandleActionResult(result);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadPendingItems();
            ShowValidation(string.Empty);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GridPending_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BindDetailPanel(GetSelectedItem());
        }

        private PendingMappingItem GetSelectedItem()
        {
            return gridPending.SelectedItem as PendingMappingItem;
        }

        private void BindDetailPanel(PendingMappingItem item)
        {
            if (item == null)
            {
                ClearDetailPanel();
                return;
            }

            txtFirstDetected.Text = item.FirstDetected == default ? "-" : item.FirstDetected.ToString("yyyy-MM-dd HH:mm:ss");
            txtLastDetected.Text = item.LastDetected == default ? "-" : item.LastDetected.ToString("yyyy-MM-dd HH:mm:ss");
            txtEvidence.Text = string.IsNullOrWhiteSpace(item.Evidence) ? "-" : item.Evidence;
            txtSimilarParameters.Text = item.SimilarParameters != null && item.SimilarParameters.Any()
                ? string.Join(", ", item.SimilarParameters)
                : "-";
            txtHistory.Text = BuildHistory(item.ChangeHistory);
            txtEditField.Text = item.ResolvedField ?? item.Suggestion ?? string.Empty;
            txtValidation.Text = item.IsAmbiguous
                ? "Caso ambiguo: revisa evidencia y corrige manualmente si la sugerencia no es confiable."
                : string.Empty;
        }

        private void ClearDetailPanel()
        {
            txtFirstDetected.Text = "-";
            txtLastDetected.Text = "-";
            txtEvidence.Text = "-";
            txtSimilarParameters.Text = "-";
            txtHistory.Text = string.Empty;
            txtEditField.Text = string.Empty;
            txtValidation.Text = string.Empty;
        }

        private static string BuildHistory(List<ChangeLogEntry> history)
        {
            if (history == null || !history.Any())
                return "Sin historial.";

            var builder = new StringBuilder();
            foreach (var entry in history.OrderByDescending(item => item.Timestamp))
            {
                builder.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Action} | {entry.User}");
                if (!string.IsNullOrWhiteSpace(entry.Notes))
                    builder.AppendLine(entry.Notes);
                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private void HandleActionResult(LearningActionResult result)
        {
            if (result == null)
            {
                ShowValidation("No se pudo completar la accion.");
                return;
            }

            if (!result.Success)
            {
                ShowValidation(result.Message);
                return;
            }

            LoadPendingItems();
            ShowValidation(result.Message, isError: false);
        }

        private void ShowValidation(string message, bool isError = true)
        {
            txtValidation.Foreground = isError
                ? System.Windows.Media.Brushes.Firebrick
                : System.Windows.Media.Brushes.DarkGreen;
            txtValidation.Text = message ?? string.Empty;
        }
    }
}
