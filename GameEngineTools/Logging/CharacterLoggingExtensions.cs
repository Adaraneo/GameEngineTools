// CharacterLoggingExtensions.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Helpery pro centralizované vytváření character log scopů.
    /// </summary>
    public static class CharacterLoggingExtensions
    {
        public static IDisposable? BeginCharacterScope(this ILogger logger, Guid personId, string subsystem)
            => logger.BeginScope(new CharacterLogScope(personId, subsystem));

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
