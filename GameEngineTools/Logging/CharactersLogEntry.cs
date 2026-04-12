// CharactersLogEntry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Normalizovaný model character log události používaný pro textový i JSONL výstup.
    /// </summary>
    internal sealed class CharactersLogEntry
    {
        public required long EventInstanceId { get; init; }

        public required DateTimeOffset RealTimestamp { get; init; }

        public required string WorldTimeText { get; init; }

        public required string Level { get; init; }

        public required string Category { get; init; }

        public required int EventId { get; init; }

        public required string Message { get; init; }

        public string? ExceptionType { get; init; }

        public string? ExceptionMessage { get; init; }

        public string? StackTrace { get; init; }

        public Guid? PersonId { get; init; }

        public string? Subsystem { get; init; }

        public string? CorrelationId { get; init; }

        public string? InteractionId { get; init; }

        public string? DecisionId { get; init; }

        public Guid? RelatedPersonId { get; init; }

        public string? LocationId { get; init; }

        public string? TickKey { get; init; }

        public required bool IsScoped { get; init; }
    }
}
