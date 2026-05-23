// CharactersLogEntry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Normalizovaný model character log události používaný pro textový i JSONL výstup.
    /// </summary>
    internal sealed class CharactersLogEntry
    {
        public long EventInstanceId { get; init; }

        public DateTimeOffset RealTimestamp { get; init; }

        public string WorldTimeText { get; init; }

        public string Level { get; init; }

        public string Category { get; init; }

        public int EventId { get; init; }

        public string Message { get; init; }

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

        public bool IsScoped { get; init; }
    }
}
