// MemoryWhatFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Factory helper pro canonical tvorbu hodnoty <c>What</c>.
    /// </summary>
    internal static class MemoryWhatFactory
    {
        #region Relation

        /// <summary>
        /// Vytvoří canonical <c>What</c> pro relation micro-positive event.
        /// </summary>
        public static string RelationMicroPositive(string kind, HumanId? from = null, HumanId? to = null)
            => BuildRelationMicro("MicroPositive", NormalizeMicroKind(kind), from, to);

        /// <summary>
        /// Vytvoří canonical <c>What</c> pro relation micro-negative event.
        /// </summary>
        public static string RelationMicroNegative(string kind, HumanId? from = null, HumanId? to = null)
            => BuildRelationMicro("MicroNegative", NormalizeMicroKind(kind), from, to);

        #endregion Relation

        #region Private

        private static string NormalizeMicroKind(string kind)
        {
            var normalized = kind.Trim().ToLowerInvariant();

            return normalized switch
            {
                "help-with-task" => MemoryMicroEventKinds.Help,
                "support-after-stress" => MemoryMicroEventKinds.Support,
                "repair-after-conflict" => MemoryMicroEventKinds.Repair,
                "warm-response" => MemoryMicroEventKinds.Warmth,
                "validation-response" => MemoryMicroEventKinds.Validation,

                "ignore-after-reachout" => MemoryMicroEventKinds.Ignore,
                "cold-response" => MemoryMicroEventKinds.Cold,
                "critical-response" => MemoryMicroEventKinds.Criticism,
                "dismissive-response" => MemoryMicroEventKinds.Dismissal,
                "slight-response" => MemoryMicroEventKinds.Slight,

                _ => normalized
            };
        }

        private static string BuildRelationMicro(string type, string kind, HumanId? from, HumanId? to)
        {
            var normalizedKind = kind.Trim().ToLowerInvariant();
            var result = $"Relation:{type}|what={normalizedKind}";

            if (from is not null)
            {
                result += $"|from={from.Value}";
            }

            if (to is not null)
            {
                result += $"|to={to.Value}";
            }

            return result;
        }

        #endregion Private
    }
}
