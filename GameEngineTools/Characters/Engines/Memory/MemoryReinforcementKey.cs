// MemoryReinforcementKey.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using static GameEngineTools.Characters.Engines.Memory.MemoryWhatParser;

    /// <summary>
    /// Explicit key for episode reinforcement.
    ///
    /// It does not rely on the raw <c>What</c> string as the single source of truth,
    /// but on its structured meaning.
    /// </summary>
    internal sealed record MemoryReinforcementKey(
        string Category,
        string Type,
        string? Outcome,
        HumanId? OtherPerson,
        string? Variant);

    /// <summary>
    /// Builder of the reinforcement key from episodic memory.
    /// </summary>
    internal static class MemoryReinforcementKeyBuilder
    {
        /// <summary>
        /// Creates a reinforcement key from an episode.
        /// </summary>
        public static MemoryReinforcementKey From(EpisodicMemory episode)
        {
            var descriptor = MemoryWhatParser.ParseDescriptor(episode.What, episode.PerceivedWhat);

            return new MemoryReinforcementKey(
                descriptor.Category,
                descriptor.Type,
                descriptor.Outcome,
                episode.OtherPerson,
                BuildVariant(descriptor, episode.What));
        }

        /// <summary>
        /// Determines a finer-grained variant of the same episode type.
        ///
        /// Goal:
        /// - interactions of the same type with the same person should reinforce each other,
        /// - but different micro-contents such as <c>help</c> vs. <c>support</c> must not merge,
        /// - sleep can reinforce even at a different number of hours, as long as it is qualitatively the same.
        /// </summary>
        private static string? BuildVariant(MemoryEpisodeDescriptor descriptor, string rawWhat)
        {
            if (descriptor.Category == "Interaction")
            {
                // Interaction is sufficiently distinguished by Category + Type + Outcome + OtherPerson.
                return null;
            }

            if (descriptor.Category == "Action")
            {
                // Action:{ActionName} — Type already carries the meaning of the whole action.
                return null;
            }

            if (descriptor.Category == "Relation" && descriptor.Type == "FirstImpression")
            {
                // Outcome already distinguishes Positive / Neutral / Negative; OtherPerson carries the target of the impression.
                return null;
            }

            if (descriptor.Category == "Relation" && descriptor.Type == "Repair")
            {
                // Outcome + OtherPerson is enough.
                return null;
            }

            if (descriptor.Category == "Relation"
                && (descriptor.Type == "MicroPositive" || descriptor.Type == "MicroNegative"))
            {
                // The micro-content is a semantic separator and MUST NOT merge.
                return descriptor.Parameters.TryGetValue("what", out var microWhat)
                    ? Normalize(microWhat)
                    : null;
            }

            if (descriptor.Category == "Sleep" && descriptor.Type == "Ended")
            {
                // Outcome already carries the quality bucket (High / Medium / Low / Poor).
                // We do not want to use hours as the reinforcement identity.
                return null;
            }

            if (descriptor.Category == "Sleep" && descriptor.Type == "Nightmare")
            {
                return descriptor.Parameters.TryGetValue("stress", out var stress)
                    ? Normalize(stress)
                    : null;
            }

            // Fallback: canonical params independent of parameter order.
            return CanonicalizeParameters(descriptor.Parameters);
        }

        /// <summary>
        /// Normalizes a text token for comparison in the reinforcement key.
        /// </summary>
        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant();

        /// <summary>
        /// Stable fallback serialization of parameters independent of order.
        /// </summary>
        private static string? CanonicalizeParameters(IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters.Count == 0)
            {
                return null;
            }

            return string.Join(
                "|",
                parameters
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => $"{kvp.Key.ToLowerInvariant()}={Normalize(kvp.Value)}"));
        }
    }
}
