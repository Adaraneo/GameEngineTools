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
    /// Explicitní klíč pro reinforcement epizod.
    ///
    /// Neopírá se o syrový <c>What</c> string jako jediný zdroj pravdy,
    /// ale o jeho strukturovaný význam.
    /// </summary>
    internal sealed record MemoryReinforcementKey(
        string Category,
        string Type,
        string? Outcome,
        HumanId? OtherPerson,
        string? Variant);

    /// <summary>
    /// Builder reinforcement klíče z epizodické paměti.
    /// </summary>
    internal static class MemoryReinforcementKeyBuilder
    {
        /// <summary>
        /// Vytvoří reinforcement klíč z epizody.
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
        /// Určí jemnější variantu stejného typu epizody.
        ///
        /// Cíl:
        /// - interakce stejného typu se stejnou osobou se mají reinforcementovat,
        /// - ale různé mikroobsahy typu <c>help</c> vs. <c>support</c> se nemají slévat,
        /// - spánek může reinforcementovat i při jiném počtu hodin, pokud je kvalitativně stejný.
        /// </summary>
        private static string? BuildVariant(MemoryEpisodeDescriptor descriptor, string rawWhat)
        {
            if (descriptor.Category == "Interaction")
            {
                // Interaction je dostatečně odlišená přes Category + Type + Outcome + OtherPerson.
                return null;
            }

            if (descriptor.Category == "Action")
            {
                // Action:{ActionName} — Type už nese význam celé akce.
                return null;
            }

            if (descriptor.Category == "Relation" && descriptor.Type == "FirstImpression")
            {
                // Outcome už rozlišuje Positive / Neutral / Negative, OtherPerson nese cíl dojmu.
                return null;
            }

            if (descriptor.Category == "Relation" && descriptor.Type == "Repair")
            {
                // Outcome + OtherPerson stačí.
                return null;
            }

            if (descriptor.Category == "Relation"
                && (descriptor.Type == "MicroPositive" || descriptor.Type == "MicroNegative"))
            {
                // Mikroobsah je významový rozdělovač a NESMÍ se slévat.
                return descriptor.Parameters.TryGetValue("what", out var microWhat)
                    ? Normalize(microWhat)
                    : null;
            }

            if (descriptor.Category == "Sleep" && descriptor.Type == "Ended")
            {
                // Outcome už nese bucket kvality (High / Medium / Low / Poor).
                // Hodiny nechceme používat jako reinforcement identitu.
                return null;
            }

            if (descriptor.Category == "Sleep" && descriptor.Type == "Nightmare")
            {
                return descriptor.Parameters.TryGetValue("stress", out var stress)
                    ? Normalize(stress)
                    : null;
            }

            // Fallback: canonical params bez závislosti na pořadí parametrů.
            return CanonicalizeParameters(descriptor.Parameters);
        }

        /// <summary>
        /// Znormalizuje textový token pro porovnávání v reinforcement klíči.
        /// </summary>
        private static string Normalize(string value)
            => value.Trim().ToLowerInvariant();

        /// <summary>
        /// Stabilní fallback serializace parametrů bez závislosti na pořadí.
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
