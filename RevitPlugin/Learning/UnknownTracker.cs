using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MiNamespace.Json;
using Newtonsoft.Json;

namespace MiNamespace.Learning
{
    internal sealed class UnknownTracker
    {
        private readonly string _fileName;

        public UnknownTracker(string fileName)
        {
            _fileName = fileName;
        }

        public UnknownRecord RegisterUnknown(Element element, string semanticField, ParameterSuggestion suggestion)
        {
            var store = Load();

            string category = element?.Category?.Name ?? "Sin categoría";
            string family = ResolveFamilyName(element);
            string detectedParameter = string.IsNullOrWhiteSpace(suggestion?.ParameterName)
                ? "(sin detectar)"
                : suggestion.ParameterName;

            string id = BuildId(semanticField, category, family, detectedParameter);
            var record = store.Unknowns.FirstOrDefault(item => item.Id == id);
            if (record == null)
            {
                record = new UnknownRecord
                {
                    Id = id,
                    SemanticField = semanticField,
                    Parameter = detectedParameter,
                    Category = category,
                    Family = family,
                    Frequency = 0,
                    FirstDetected = DateTime.Now,
                    State = IsReady(suggestion) ? LearningStates.Ready : LearningStates.Pending
                };
                record.ChangeHistory.Add(new ChangeLogEntry
                {
                    Timestamp = DateTime.Now,
                    Action = "created",
                    User = Environment.UserName,
                    Notes = "Caso detectado por primera vez."
                });
                store.Unknowns.Add(record);
            }

            record.LastDetected = DateTime.Now;
            record.Frequency++;

            if (!string.Equals(record.State, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(record.State, LearningStates.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                record.State = IsReady(suggestion) ? LearningStates.Ready : LearningStates.Pending;
            }

            record.IsAmbiguous = suggestion?.IsAmbiguous ?? true;
            record.Evidence = suggestion?.Evidence ?? record.Evidence;
            record.ValueKind = suggestion?.ValueKind ?? record.ValueKind;

            foreach (var similar in suggestion?.SimilarParameters ?? Enumerable.Empty<string>())
            {
                if (!record.SimilarParameters.Contains(similar, StringComparer.OrdinalIgnoreCase))
                    record.SimilarParameters.Add(similar);
            }

            if (record.SimilarParameters.Count > 8)
                record.SimilarParameters = record.SimilarParameters.Take(8).ToList();

            string exampleValue = suggestion?.ExampleValue;
            if (!string.IsNullOrWhiteSpace(exampleValue) &&
                !record.ExampleValues.Contains(exampleValue, StringComparer.OrdinalIgnoreCase))
            {
                record.ExampleValues.Add(exampleValue);
                if (record.ExampleValues.Count > 5)
                    record.ExampleValues = record.ExampleValues.Take(5).ToList();
            }

            if (suggestion != null && !string.IsNullOrWhiteSpace(suggestion.SuggestedField))
            {
                bool exists = record.Suggestions.Any(item =>
                    string.Equals(item.SuggestedField, suggestion.SuggestedField, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(item.Confidence - suggestion.Confidence) < 0.001);

                if (!exists)
                {
                    record.Suggestions.Add(new SuggestionSnapshot
                    {
                        SuggestedField = suggestion.SuggestedField,
                        Confidence = suggestion.Confidence,
                        Timestamp = DateTime.Now,
                        Evidence = suggestion.Evidence,
                        IsAmbiguous = suggestion.IsAmbiguous
                    });
                }
            }

            Save(store);
            return record;
        }

        public void MarkResolved(string id)
        {
            UpdateState(id, LearningStates.Resolved);
        }

        public void MarkRejected(string id)
        {
            UpdateState(id, LearningStates.Rejected);
        }

        private void UpdateState(string id, string state)
        {
            var store = Load();
            var record = store.Unknowns.FirstOrDefault(item => item.Id == id);
            if (record == null)
                return;

            record.State = state;
            record.LastDetected = DateTime.Now;
            record.ChangeHistory.Add(new ChangeLogEntry
            {
                Timestamp = DateTime.Now,
                Action = state,
                User = Environment.UserName,
                Notes = $"Estado actualizado a {state}."
            });
            Save(store);
        }

        private UnknownsFileModel Load()
        {
            JsonFileManager.EnsureJsonFiles();
            var store = JsonFileManager.ReadJson<UnknownsFileModel>(_fileName);
            return store ?? new UnknownsFileModel();
        }

        private void Save(UnknownsFileModel store)
        {
            JsonFileManager.WriteJson(_fileName, store);
        }

        private static bool IsReady(ParameterSuggestion suggestion)
        {
            return suggestion != null && suggestion.Confidence >= 0.85 && !suggestion.IsAmbiguous;
        }

        internal static string BuildId(string semanticField, string category, string family, string parameter)
        {
            return string.Join("|", new[]
            {
                semanticField ?? string.Empty,
                category ?? string.Empty,
                family ?? string.Empty,
                parameter ?? string.Empty
            }).ToUpperInvariant();
        }

        internal static string ResolveFamilyName(Element element)
        {
            var type = element?.Document?.GetElement(element.GetTypeId()) as ElementType;
            if (!string.IsNullOrWhiteSpace(type?.FamilyName))
                return type.FamilyName;

            if (!string.IsNullOrWhiteSpace(type?.Name))
                return type.Name;

            return "Sin familia";
        }
    }
}
