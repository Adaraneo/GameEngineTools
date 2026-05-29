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

        /// <summary>
        /// Numeric world tick (<see cref="World.Utils.Time.WDateTime.WorldTicks"/>) at log time.
        /// The grouping key for the v2 tick-store; survives without parsing <see cref="WorldTimeText"/>.
        /// </summary>
        public long WorldTick { get; init; }

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

        /// <summary>
        /// Structured event fields captured from the <c>[LoggerMessage]</c> state
        /// (e.g. <c>{ "Action": "Eat", "Utility": 0.87 }</c>). Each field's JSON type
        /// is preserved (number/bool/string) so consumers read it directly instead of
        /// re-parsing <see cref="Message"/> with regexes. <c>null</c> for unstructured logs.
        /// </summary>
        public IReadOnlyDictionary<string, object?>? Payload { get; init; }
    }
}
