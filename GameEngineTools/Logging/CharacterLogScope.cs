// CharacterLogScope.cs
// Copyright (c) 50PSoftware

using System.Diagnostics.CodeAnalysis;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Diagnostic scope identifying the character, subsystem and optional correlation metadata.
    /// </summary>
    /// <remarks>
    /// The logger provider extracts the scope via a direct type check. The scope describes the diagnostic context,
    /// not the character's domain state.
    /// </remarks>
    public readonly record struct CharacterLogScope
    {
        /// <summary>ID postavy pro per-person routing.</summary>
        public required Guid PersonId { get; init; }

        /// <summary>Subsystem name for per-subsystem routing.</summary>
        public required string Subsystem { get; init; }

        /// <summary>Optional correlation id of a broader operation.</summary>
        public string? CorrelationId { get; init; }

        /// <summary>Optional interaction id.</summary>
        public string? InteractionId { get; init; }

        /// <summary>Optional decision id.</summary>
        public string? DecisionId { get; init; }

        /// <summary>Optional related character.</summary>
        public Guid? RelatedPersonId { get; init; }

        /// <summary>Optional location id.</summary>
        public string? LocationId { get; init; }

        /// <summary>Optional simulation tick/key.</summary>
        public string? TickKey { get; init; }

        /// <summary>
        /// Creates a basic character scope for the given character and subsystem.
        /// </summary>
        [SetsRequiredMembers]
        public CharacterLogScope(Guid personId, string subsystem)
        {
            PersonId = personId;
            Subsystem = subsystem;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{PersonId}:{Subsystem}";
    }
}
