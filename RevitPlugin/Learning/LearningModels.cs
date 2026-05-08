using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MiNamespace.Learning
{
    internal static class LearningStates
    {
        public const string Pending = "pending";
        public const string Ready = "ready";
        public const string Resolved = "resolved";
        public const string Rejected = "rejected";
    }

    internal sealed class ParameterSuggestion
    {
        public string ParameterName { get; set; }
        public string SuggestedField { get; set; }
        public double Confidence { get; set; }
        public string ExampleValue { get; set; }
        public bool IsAmbiguous { get; set; }
        public string Evidence { get; set; }
        public string ValueKind { get; set; }
        public List<string> SimilarParameters { get; set; } = new List<string>();
    }

    internal sealed class ParameterResolution
    {
        public Parameter Parameter { get; set; }
        public string ParameterName { get; set; }
        public bool ComesFromLearningMap { get; set; }
    }

    internal sealed class LearnedMapping
    {
        public string SemanticField { get; set; }
        public string Parameter { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Status { get; set; }
        public string ConfirmedBy { get; set; }
        public string Source { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ChangeLogEntry> ChangeHistory { get; set; } = new List<ChangeLogEntry>();
    }

    internal sealed class MappingFileModel
    {
        public List<LearnedMapping> Mappings { get; set; } = new List<LearnedMapping>();
    }

    internal sealed class SuggestionSnapshot
    {
        public string SuggestedField { get; set; }
        public double Confidence { get; set; }
        public DateTime Timestamp { get; set; }
        public string Evidence { get; set; }
        public bool IsAmbiguous { get; set; }
    }

    internal sealed class ChangeLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; }
        public string User { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class UnknownRecord
    {
        public string Id { get; set; }
        public string SemanticField { get; set; }
        public string Parameter { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public int Frequency { get; set; }
        public DateTime FirstDetected { get; set; }
        public DateTime LastDetected { get; set; }
        public List<string> ExampleValues { get; set; } = new List<string>();
        public List<SuggestionSnapshot> Suggestions { get; set; } = new List<SuggestionSnapshot>();
        public string State { get; set; }
        public bool IsAmbiguous { get; set; }
        public string Evidence { get; set; }
        public string ValueKind { get; set; }
        public List<string> SimilarParameters { get; set; } = new List<string>();
        public List<ChangeLogEntry> ChangeHistory { get; set; } = new List<ChangeLogEntry>();
    }

    internal sealed class UnknownsFileModel
    {
        public List<UnknownRecord> Unknowns { get; set; } = new List<UnknownRecord>();
    }

    internal sealed class PendingMappingItem
    {
        public string Id { get; set; }
        public string SemanticField { get; set; }
        public string Parameter { get; set; }
        public string Category { get; set; }
        public string Family { get; set; }
        public string Suggestion { get; set; }
        public double Confidence { get; set; }
        public int Frequency { get; set; }
        public string ExampleValue { get; set; }
        public string State { get; set; }
        public string ResolvedField { get; set; }
        public bool IsAmbiguous { get; set; }
        public string Evidence { get; set; }
        public string ValueKind { get; set; }
        public DateTime FirstDetected { get; set; }
        public DateTime LastDetected { get; set; }
        public List<string> SimilarParameters { get; set; } = new List<string>();
        public List<ChangeLogEntry> ChangeHistory { get; set; } = new List<ChangeLogEntry>();
    }

    internal sealed class PendingMappingsFileModel
    {
        public List<PendingMappingItem> Pending { get; set; } = new List<PendingMappingItem>();
    }
}
