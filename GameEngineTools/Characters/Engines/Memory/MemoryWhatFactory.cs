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
            => BuildRelationMicro("MicroPositive", kind, from, to);

        /// <summary>
        /// Vytvoří canonical <c>What</c> pro relation micro-negative event.
        /// </summary>
        public static string RelationMicroNegative(string kind, HumanId? from = null, HumanId? to = null)
            => BuildRelationMicro("MicroNegative", kind, from, to);

        #endregion

        #region Private

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

        #endregion
    }
}
