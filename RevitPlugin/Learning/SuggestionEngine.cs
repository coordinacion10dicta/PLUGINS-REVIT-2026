using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace MiNamespace.Learning
{
    internal sealed class SuggestionEngine
    {
        private static readonly HashSet<string> GenericParameterNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "category",
                "family",
                "type",
                "type name",
                "type id",
                "comments",
                "mark",
                "phase created",
                "phase demolished",
                "workset",
                "level",
                "name"
            };

        private static readonly Dictionary<string, string[]> SemanticPatterns =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "System", new[] { "system", "service", "sistema", "sys" } },
                { "Material", new[] { "material", "mat" } },
                { "Diameter", new[] { "diameter", "diam", "dn", "nominal", "pipe size", "size" } },
                { "Insulation", new[] { "insulation", "insul", "aislamiento", "lagging" } },
                { "Description", new[] { "description", "desc", "descripcion" } },
                { "Brand", new[] { "manufacturer", "brand", "marca", "fabricante" } },
                { "Model", new[] { "model", "modelo", "type mark", "modelo equipo" } },
                { "Size", new[] { "size", "dimension", "width", "height", "tamano", "medida" } },
                { "Family", new[] { "family", "familia", "type", "tipo" } }
            };

        public ParameterSuggestion Suggest(
            string semanticField,
            Element element,
            IEnumerable<string> defaultParameterNames,
            IEnumerable<LearnedMapping> priorMappings = null)
        {
            if (element == null)
                return CreateEmptySuggestion(semanticField, "Elemento nulo.");

            var candidates = CollectCandidates(element).ToList();
            if (!candidates.Any())
                return CreateEmptySuggestion(semanticField, "No se encontraron parámetros candidatos.");

            var defaults = new HashSet<string>(
                (defaultParameterNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeText),
                StringComparer.OrdinalIgnoreCase);

            var scored = candidates
                .Select(candidate => new ScoredCandidate
                {
                    Candidate = candidate,
                    Score = ScoreCandidate(semanticField, candidate, defaults, element, priorMappings)
                })
                .OrderByDescending(item => item.Score)
                .ToList();

            var best = scored.FirstOrDefault();
            var second = scored.Skip(1).FirstOrDefault();
            bool ambiguous = best == null ||
                             best.Score < 0.55 ||
                             (second != null && Math.Abs(best.Score - second.Score) < 0.12);

            if (best == null)
                return CreateEmptySuggestion(semanticField, "No hubo un candidato con suficiente respaldo.");

            var similarParameters = scored
                .Take(5)
                .Select(item => item.Candidate.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string evidence =
                $"Candidato: {best.Candidate.Name}; score={best.Score:F2}; tipo={best.Candidate.ValueKind}; " +
                $"categoria={element.Category?.Name ?? "Sin categoría"}; " +
                $"segundo={(second != null ? second.Candidate.Name + " (" + second.Score.ToString("F2", CultureInfo.InvariantCulture) + ")" : "n/a")}.";

            return new ParameterSuggestion
            {
                ParameterName = best.Candidate.Name,
                SuggestedField = semanticField,
                ExampleValue = best.Candidate.ExampleValue,
                Confidence = Math.Round(Math.Max(0, Math.Min(best.Score, 0.98)), 2),
                IsAmbiguous = ambiguous,
                Evidence = evidence,
                ValueKind = best.Candidate.ValueKind,
                SimilarParameters = similarParameters
            };
        }

        private static ParameterSuggestion CreateEmptySuggestion(string semanticField, string evidence)
        {
            return new ParameterSuggestion
            {
                SuggestedField = semanticField,
                Confidence = 0,
                IsAmbiguous = true,
                Evidence = evidence
            };
        }

        private static IEnumerable<Candidate> CollectCandidates(Element element)
        {
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in EnumerateParameters(element))
            {
                string name = parameter.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name) || !emitted.Add(name))
                    continue;

                yield return BuildCandidate(name, parameter);
            }

            var type = element.Document?.GetElement(element.GetTypeId());
            foreach (var parameter in EnumerateParameters(type))
            {
                string name = parameter.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name) || !emitted.Add(name))
                    continue;

                yield return BuildCandidate(name, parameter);
            }
        }

        private static Candidate BuildCandidate(string name, Parameter parameter)
        {
            string exampleValue = TryReadParameterValue(parameter);
            return new Candidate
            {
                Name = name,
                ExampleValue = exampleValue,
                ValueKind = InferValueKind(parameter, exampleValue)
            };
        }

        private static IEnumerable<Parameter> EnumerateParameters(Element element)
        {
            if (element == null)
                yield break;

            foreach (Parameter parameter in element.Parameters)
                yield return parameter;
        }

        private static double ScoreCandidate(
            string semanticField,
            Candidate candidate,
            ISet<string> defaults,
            Element element,
            IEnumerable<LearnedMapping> priorMappings)
        {
            string candidateName = NormalizeText(candidate.Name);
            if (string.IsNullOrWhiteSpace(candidateName))
                return 0;

            double score = 0.05;
            var evidence = new List<string>();

            if (GenericParameterNames.Contains(candidateName))
                score -= 0.45;

            if (defaults.Contains(candidateName))
                score += 0.45;

            if (SemanticPatterns.TryGetValue(semanticField, out var patterns))
            {
                foreach (string pattern in patterns)
                {
                    string normalizedPattern = NormalizeText(pattern);
                    if (candidateName.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                        score += 0.42;
                    else if (candidateName.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                        score += 0.28;
                    else if (candidateName.Contains(normalizedPattern))
                        score += 0.18;
                }
            }

            string category = NormalizeText(element?.Category?.Name ?? string.Empty);
            if (!CategorySupportsSemanticField(semanticField, category))
                score -= 0.2;

            if (!ValueKindSupportsSemanticField(semanticField, candidate.ValueKind, candidate.ExampleValue))
                score -= 0.28;
            else
                score += 0.12;

            if (semanticField.Equals("Diameter", StringComparison.OrdinalIgnoreCase))
            {
                if (candidateName.Contains("diam"))
                    score += 0.25;
                if (candidateName.Contains("size") && category.Contains("pipe"))
                    score += 0.1;
                if (candidateName.Contains("size") && category.Contains("duct"))
                    score -= 0.08;
            }

            if (semanticField.Equals("Size", StringComparison.OrdinalIgnoreCase))
            {
                if (candidateName.Contains("size") || candidateName.Contains("dimension"))
                    score += 0.22;
                if (candidateName.Contains("diam"))
                    score -= 0.18;
            }

            if (semanticField.Equals("System", StringComparison.OrdinalIgnoreCase) &&
                (candidateName.Contains("service") || candidateName.Contains("system")))
            {
                score += 0.16;
            }

            if (semanticField.Equals("Material", StringComparison.OrdinalIgnoreCase) && candidateName.Contains("material"))
                score += 0.22;

            if (semanticField.Equals("Brand", StringComparison.OrdinalIgnoreCase) &&
                (candidateName.Contains("manufacturer") || candidateName.Contains("brand")))
            {
                score += 0.22;
            }

            if (semanticField.Equals("Model", StringComparison.OrdinalIgnoreCase) && candidateName.Contains("model"))
                score += 0.22;

            if (semanticField.Equals("Description", StringComparison.OrdinalIgnoreCase) &&
                (candidateName.Contains("description") || candidateName.Contains("desc")))
            {
                score += 0.22;
            }

            foreach (var mapping in priorMappings ?? Enumerable.Empty<LearnedMapping>())
            {
                if (string.IsNullOrWhiteSpace(mapping.Parameter))
                    continue;

                string normalizedMappedParameter = NormalizeText(mapping.Parameter);
                if (candidateName.Equals(normalizedMappedParameter, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.2;

                    if (string.Equals(mapping.Category, element?.Category?.Name, StringComparison.OrdinalIgnoreCase))
                        score += 0.08;

                    if (string.Equals(mapping.Family, UnknownTracker.ResolveFamilyName(element), StringComparison.OrdinalIgnoreCase))
                        score += 0.08;
                }
            }

            return Math.Max(0, score);
        }

        private static bool CategorySupportsSemanticField(string semanticField, string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return true;

            if (semanticField.Equals("Diameter", StringComparison.OrdinalIgnoreCase))
                return category.Contains("pipe") || category.Contains("duct") || category.Contains("plumb");

            if (semanticField.Equals("Insulation", StringComparison.OrdinalIgnoreCase))
                return category.Contains("pipe") || category.Contains("duct");

            return true;
        }

        private static bool ValueKindSupportsSemanticField(string semanticField, string valueKind, string exampleValue)
        {
            if (semanticField.Equals("Diameter", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("Size", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(valueKind, "dimension", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(valueKind, "numeric", StringComparison.OrdinalIgnoreCase);
            }

            if (semanticField.Equals("Brand", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("Model", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("Description", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("Material", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                semanticField.Equals("Insulation", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(valueKind, "text", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(valueKind, "dimension", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static string InferValueKind(Parameter parameter, string exampleValue)
        {
            if (parameter == null)
                return "unknown";

            if (parameter.StorageType == StorageType.Double)
                return "dimension";

            if (parameter.StorageType == StorageType.Integer)
                return "numeric";

            if (!string.IsNullOrWhiteSpace(exampleValue))
            {
                string normalized = exampleValue.Trim();
                if (normalized.Contains("\"") || normalized.Contains("mm") || normalized.Contains("cm") || normalized.Contains("dn"))
                    return "dimension";

                if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    return "numeric";
            }

            return "text";
        }

        private static string TryReadParameterValue(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
                return string.Empty;

            string asString = parameter.AsString();
            if (!string.IsNullOrWhiteSpace(asString))
                return asString;

            string asValueString = parameter.AsValueString();
            if (!string.IsNullOrWhiteSpace(asValueString))
                return asValueString;

            if (parameter.StorageType == StorageType.Double)
                return parameter.AsDouble().ToString("G", CultureInfo.InvariantCulture);

            if (parameter.StorageType == StorageType.Integer)
                return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);

            return string.Empty;
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (char ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString();
        }

        private sealed class Candidate
        {
            public string Name { get; set; }
            public string ExampleValue { get; set; }
            public string ValueKind { get; set; }
        }

        private sealed class ScoredCandidate
        {
            public Candidate Candidate { get; set; }
            public double Score { get; set; }
        }
    }
}
