using System;
using System.Collections.Generic;
using System.Linq;
using MiNamespace.Json;
using Newtonsoft.Json;

namespace MiNamespace.Learning
{
    internal sealed class PendingMappingsStore
    {
        private readonly string _fileName;

        public PendingMappingsStore(string fileName)
        {
            _fileName = fileName;
        }

        public IReadOnlyList<PendingMappingItem> GetPending()
        {
            var store = Load();
            return store.Pending
                .Where(RequiresHumanReview)
                .OrderByDescending(item => item.Frequency)
                .ThenByDescending(item => item.Confidence)
                .ThenByDescending(item => item.LastDetected)
                .ToList();
        }

        public int CountPending()
        {
            return Load().Pending.Count(RequiresHumanReview);
        }

        public void Upsert(UnknownRecord unknown)
        {
            if (unknown == null)
                return;

            if (string.Equals(unknown.State, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(unknown.State, LearningStates.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                Remove(unknown.Id);
                return;
            }

            var store = Load();
            var pending = store.Pending.FirstOrDefault(item => item.Id == unknown.Id);
            if (pending == null)
            {
                pending = new PendingMappingItem { Id = unknown.Id };
                store.Pending.Add(pending);
            }

            var bestSuggestion = unknown.Suggestions
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();

            pending.SemanticField = unknown.SemanticField;
            pending.Parameter = unknown.Parameter;
            pending.Category = unknown.Category;
            pending.Family = unknown.Family;
            pending.Frequency = unknown.Frequency;
            pending.ExampleValue = unknown.ExampleValues.LastOrDefault() ?? string.Empty;
            pending.Suggestion = bestSuggestion?.SuggestedField ?? unknown.SemanticField;
            pending.Confidence = bestSuggestion?.Confidence ?? 0;
            pending.State = unknown.State;
            pending.IsAmbiguous = unknown.IsAmbiguous;
            pending.Evidence = unknown.Evidence ?? bestSuggestion?.Evidence ?? string.Empty;
            pending.ValueKind = unknown.ValueKind ?? string.Empty;
            pending.FirstDetected = unknown.FirstDetected;
            pending.LastDetected = unknown.LastDetected;
            pending.SimilarParameters = unknown.SimilarParameters?.ToList() ?? new List<string>();
            pending.ChangeHistory = unknown.ChangeHistory?.ToList() ?? new List<ChangeLogEntry>();
            pending.ResolvedField = string.IsNullOrWhiteSpace(pending.ResolvedField)
                ? pending.Suggestion
                : pending.ResolvedField;

            Save(store);
        }

        public void Remove(string id)
        {
            var store = Load();
            store.Pending.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            Save(store);
        }

        private PendingMappingsFileModel Load()
        {
            JsonFileManager.EnsureJsonFiles();
            var store = JsonFileManager.ReadJson<PendingMappingsFileModel>(_fileName);
            return store ?? new PendingMappingsFileModel();
        }

        private void Save(PendingMappingsFileModel store)
        {
            JsonFileManager.WriteJson(_fileName, store);
        }

        private static bool RequiresHumanReview(PendingMappingItem item)
        {
            if (item == null)
                return false;

            if (string.Equals(item.State, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.State, LearningStates.Rejected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (item.IsAmbiguous)
                return true;

            if (item.Confidence < 0.9)
                return true;

            return string.Equals(item.State, LearningStates.Pending, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.State, LearningStates.Ready, StringComparison.OrdinalIgnoreCase);
        }
    }
}
