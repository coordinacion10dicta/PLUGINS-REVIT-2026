using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MiNamespace.Json;
using Newtonsoft.Json;

namespace MiNamespace.Learning
{
    internal sealed class LearningSystem
    {
        private readonly SuggestionEngine _suggestionEngine;
        private readonly UnknownTracker _unknownTracker;
        private readonly PendingMappingsStore _pendingMappingsStore;
        private readonly MappingStore _mappingStore;
        private readonly HashSet<string> _sessionTrackedUnknowns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LearningSystem()
        {
            JsonFileManager.EnsureRuntimeEnvironment();
            _suggestionEngine = new SuggestionEngine();
            _unknownTracker = new UnknownTracker("unknowns.json");
            _pendingMappingsStore = new PendingMappingsStore("pending_mappings.json");
            _mappingStore = new MappingStore("mapping.json");
        }

        public bool TryResolveParameter(
            Element element,
            string semanticField,
            out ParameterResolution resolution,
            params string[] defaultParameterNames)
        {
            resolution = null;
            if (element == null)
                return false;

            string category = element.Category?.Name ?? "Sin categoría";
            string family = UnknownTracker.ResolveFamilyName(element);

            if (TryResolveMappedParameter(element, semanticField, out resolution))
                return true;

            foreach (string defaultParameterName in defaultParameterNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                if (TryFindParameterByName(element, defaultParameterName, out var defaultParameter))
                {
                    resolution = new ParameterResolution
                    {
                        Parameter = defaultParameter,
                        ParameterName = defaultParameterName,
                        ComesFromLearningMap = false
                    };
                    return true;
                }
            }

            TrackUnknown(element, semanticField, defaultParameterNames);
            return false;
        }

        public bool TryResolveMappedParameter(
            Element element,
            string semanticField,
            out ParameterResolution resolution)
        {
            resolution = null;
            if (element == null)
                return false;

            string category = element.Category?.Name ?? "Sin categoría";
            string family = UnknownTracker.ResolveFamilyName(element);

            foreach (var mapping in _mappingStore.GetMappings(semanticField, category, family))
            {
                if (TryFindParameterByName(element, mapping.Parameter, out var mappedParameter))
                {
                    resolution = new ParameterResolution
                    {
                        Parameter = mappedParameter,
                        ParameterName = mapping.Parameter,
                        ComesFromLearningMap = true
                    };
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<PendingMappingItem> GetPendingMappings()
        {
            return _pendingMappingsStore.GetPending();
        }

        public LearningActionResult Approve(PendingMappingItem item, string resolvedField)
        {
            if (item == null)
                return LearningActionResult.Fail("No hay un registro seleccionado.");

            string semanticField = string.IsNullOrWhiteSpace(resolvedField)
                ? item.Suggestion
                : resolvedField.Trim();

            if (string.IsNullOrWhiteSpace(semanticField) || string.IsNullOrWhiteSpace(item.Parameter))
                return LearningActionResult.Fail("El campo aprobado y el parámetro detectado son obligatorios.");

            string conflict = _mappingStore.ValidateConflict(item, semanticField);
            if (!string.IsNullOrWhiteSpace(conflict))
                return LearningActionResult.Fail(conflict);

            _mappingStore.Upsert(new LearnedMapping
            {
                SemanticField = semanticField,
                Parameter = item.Parameter,
                Category = item.Category,
                Family = item.Family,
                Status = LearningStates.Resolved
            }, "approved", $"Mapping aprobado desde bandeja. Campo final: {semanticField}.");

            _unknownTracker.MarkResolved(item.Id);
            _pendingMappingsStore.Remove(item.Id);
            return LearningActionResult.Ok("Mapping aprobado.");
        }

        public LearningActionResult Edit(PendingMappingItem item, string correctedField)
        {
            if (item == null)
                return LearningActionResult.Fail("No hay un registro seleccionado.");

            string semanticField = correctedField?.Trim();
            if (string.IsNullOrWhiteSpace(semanticField))
                return LearningActionResult.Fail("Debes ingresar un campo corregido antes de editar.");

            string conflict = _mappingStore.ValidateConflict(item, semanticField);
            if (!string.IsNullOrWhiteSpace(conflict))
                return LearningActionResult.Fail(conflict);

            _mappingStore.Upsert(new LearnedMapping
            {
                SemanticField = semanticField,
                Parameter = item.Parameter,
                Category = item.Category,
                Family = item.Family,
                Status = LearningStates.Resolved
            }, "edited", $"Mapping editado manualmente. Sugerencia inicial: {item.Suggestion}; campo final: {semanticField}.");

            _unknownTracker.MarkResolved(item.Id);
            _pendingMappingsStore.Remove(item.Id);
            return LearningActionResult.Ok("Mapping editado y confirmado.");
        }

        public LearningActionResult Reject(PendingMappingItem item)
        {
            if (item == null)
                return LearningActionResult.Fail("No hay un registro seleccionado.");

            _unknownTracker.MarkRejected(item.Id);
            _pendingMappingsStore.Remove(item.Id);
            return LearningActionResult.Ok("Caso rechazado.");
        }

        public int GetPendingCount()
        {
            return _pendingMappingsStore.CountPending();
        }

        private void TrackUnknown(Element element, string semanticField, IEnumerable<string> defaultParameterNames)
        {
            string sessionKey = $"{element.UniqueId}|{semanticField}";
            if (!_sessionTrackedUnknowns.Add(sessionKey))
                return;

            var suggestion = _suggestionEngine.Suggest(
                semanticField,
                element,
                defaultParameterNames,
                _mappingStore.GetResolvedMappingsForSuggestion(semanticField));
            var record = _unknownTracker.RegisterUnknown(element, semanticField, suggestion);
            _pendingMappingsStore.Upsert(record);
        }

        private static bool TryFindParameterByName(Element element, string parameterName, out Parameter parameter)
        {
            parameter = null;
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
                return false;

            parameter = element.LookupParameter(parameterName);
            if (parameter != null)
                return true;

            var type = element.Document?.GetElement(element.GetTypeId());
            parameter = type?.LookupParameter(parameterName);
            return parameter != null;
        }

        private sealed class MappingStore
        {
            private readonly string _fileName;

            public MappingStore(string fileName)
            {
                _fileName = fileName;
            }

            public IEnumerable<LearnedMapping> GetMappings(string semanticField, string category, string family)
            {
                var store = Load();
                return store.Mappings
                    .Where(item => string.Equals(item.Status, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase))
                    .Where(item => string.Equals(item.SemanticField, semanticField, StringComparison.OrdinalIgnoreCase))
                    .Where(item =>
                        string.IsNullOrWhiteSpace(item.Category) ||
                        string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                    .Where(item =>
                        string.IsNullOrWhiteSpace(item.Family) ||
                        string.Equals(item.Family, family, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => string.Equals(item.Family, family, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(item => item.UpdatedAt);
            }

            public void Upsert(LearnedMapping mapping)
            {
                Upsert(mapping, "approved", "Mapping confirmado.");
            }

            public void Upsert(LearnedMapping mapping, string action, string notes)
            {
                if (mapping == null)
                    return;

                var store = Load();
                var existing = store.Mappings.FirstOrDefault(item =>
                    string.Equals(item.SemanticField, mapping.SemanticField, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Category, mapping.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Family, mapping.Family, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    mapping.ConfirmedBy = Environment.UserName;
                    mapping.Source = "LearningReviewQueue";
                    mapping.CreatedAt = DateTime.Now;
                    mapping.UpdatedAt = DateTime.Now;
                    mapping.ChangeHistory.Add(new ChangeLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Action = action,
                        User = Environment.UserName,
                        Notes = notes
                    });
                    store.Mappings.Add(mapping);
                }
                else
                {
                    if (existing.ChangeHistory == null)
                        existing.ChangeHistory = new List<ChangeLogEntry>();
                    existing.ChangeHistory.Add(new ChangeLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Action = action,
                        User = Environment.UserName,
                        Notes = notes
                    });
                    existing.Parameter = mapping.Parameter;
                    existing.Status = mapping.Status;
                    existing.ConfirmedBy = Environment.UserName;
                    existing.Source = "LearningReviewQueue";
                    existing.UpdatedAt = DateTime.Now;
                }

                Save(store);
            }

            public string ValidateConflict(PendingMappingItem item, string semanticField)
            {
                if (item == null)
                    return "No hay un registro para validar.";

                var store = Load();

                var semanticConflict = store.Mappings.FirstOrDefault(existing =>
                    string.Equals(existing.Status, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.SemanticField, semanticField, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Category, item.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Family, item.Family, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing.Parameter, item.Parameter, StringComparison.OrdinalIgnoreCase));

                if (semanticConflict != null)
                {
                    return $"Conflicto: ya existe un mapping confirmado para '{semanticField}' con el parámetro '{semanticConflict.Parameter}' en la misma categoría/familia.";
                }

                var parameterConflict = store.Mappings.FirstOrDefault(existing =>
                    string.Equals(existing.Status, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Parameter, item.Parameter, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Category, item.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Family, item.Family, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing.SemanticField, semanticField, StringComparison.OrdinalIgnoreCase));

                if (parameterConflict != null)
                {
                    return $"Conflicto: el parámetro '{item.Parameter}' ya está confirmado como '{parameterConflict.SemanticField}' para la misma categoría/familia.";
                }

                return null;
            }

            public IEnumerable<LearnedMapping> GetResolvedMappingsForSuggestion(string semanticField)
            {
                var store = Load();
                return store.Mappings
                    .Where(item => string.Equals(item.Status, LearningStates.Resolved, StringComparison.OrdinalIgnoreCase))
                    .Where(item => string.Equals(item.SemanticField, semanticField, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            private MappingFileModel Load()
            {
                JsonFileManager.EnsureJsonFiles();
                var store = JsonFileManager.ReadJson<MappingFileModel>(_fileName);
                return store ?? new MappingFileModel();
            }

            private void Save(MappingFileModel store)
            {
                JsonFileManager.WriteJson(_fileName, store);
            }
        }
    }

    internal sealed class LearningActionResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        public static LearningActionResult Ok(string message)
        {
            return new LearningActionResult { Success = true, Message = message };
        }

        public static LearningActionResult Fail(string message)
        {
            return new LearningActionResult { Success = false, Message = message };
        }
    }
}
