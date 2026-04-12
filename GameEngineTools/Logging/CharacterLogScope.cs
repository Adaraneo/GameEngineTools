// CharacterLogScope.cs
// Copyright (c) 50PSoftware

using System.Diagnostics.CodeAnalysis;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Diagnostic scope identifikující postavu, subsystem a volitelná korelační metadata.
    /// </summary>
    /// <remarks>
    /// Logger provider extrahuje scope přímou typovou kontrolou. Scope popisuje diagnostický kontext,
    /// ne doménový stav postavy.
    /// </remarks>
    public readonly record struct CharacterLogScope
    {
        /// <summary>ID postavy pro per-person routing.</summary>
        public required Guid PersonId { get; init; }

        /// <summary>Název subsystemu pro per-subsystem routing.</summary>
        public required string Subsystem { get; init; }

        /// <summary>Volitelné korelační ID širší operace.</summary>
        public string? CorrelationId { get; init; }

        /// <summary>Volitelné ID interakce.</summary>
        public string? InteractionId { get; init; }

        /// <summary>Volitelné ID rozhodnutí.</summary>
        public string? DecisionId { get; init; }

        /// <summary>Volitelná související postava.</summary>
        public Guid? RelatedPersonId { get; init; }

        /// <summary>Volitelné ID lokace.</summary>
        public string? LocationId { get; init; }

        /// <summary>Volitelný klíč/tick simulace.</summary>
        public string? TickKey { get; init; }

        /// <summary>
        /// Vytvoří základní character scope pro danou postavu a subsystem.
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
