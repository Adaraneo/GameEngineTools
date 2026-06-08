// CharacterLoggingExtensions.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Helpers for centrally creating character log scopes.
    /// </summary>
    public static class CharacterLoggingExtensions
    {
        /// <summary>Begins a logging scope tagged with a character id and subsystem.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="personId">Character identifier.</param>
        /// <param name="subsystem">Subsystem name.</param>
        public static IDisposable? BeginCharacterScope(this ILogger logger, Guid personId, string subsystem)
            => logger.BeginScope(new CharacterLogScope(personId, subsystem));

        /// <summary>Begins a logging scope with full character correlation context.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="personId">Character identifier.</param>
        /// <param name="subsystem">Subsystem name.</param>
        /// <param name="correlationId">Optional correlation id.</param>
        /// <param name="interactionId">Optional interaction id.</param>
        /// <param name="decisionId">Optional decision id.</param>
        /// <param name="relatedPersonId">Optional related character id.</param>
        /// <param name="locationId">Optional location id.</param>
        /// <param name="tickKey">Optional tick key.</param>
        public static IDisposable? BeginCharacterScope(
            this ILogger logger,
            Guid personId,
            string subsystem,
            string? correlationId = null,
            string? interactionId = null,
            string? decisionId = null,
            Guid? relatedPersonId = null,
            string? locationId = null,
            string? tickKey = null)
            => logger.BeginScope(new CharacterLogScope(personId, subsystem)
            {
                CorrelationId = correlationId,
                InteractionId = interactionId,
                DecisionId = decisionId,
                RelatedPersonId = relatedPersonId,
                LocationId = locationId,
                TickKey = tickKey
            });
    }
}
