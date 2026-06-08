// MemoryMicroEventKinds.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    /// <summary>
    /// Canonical tokeny pro mikro-události v relation paměti.
    /// </summary>
    public static class MemoryMicroEventKinds
    {
        #region Positive

        /// <summary>The other person helped.</summary>
        public const string Help = "help";
        /// <summary>The other person gave emotional support.</summary>
        public const string Support = "support";
        /// <summary>The other person attempted relationship repair.</summary>
        public const string Repair = "repair";
        /// <summary>The other person showed warmth.</summary>
        public const string Warmth = "warmth";
        /// <summary>The other person validated the character.</summary>
        public const string Validation = "validation";

        #endregion Positive

        #region Negative

        /// <summary>The other person ignored the character.</summary>
        public const string Ignore = "ignore";
        /// <summary>The other person was cold.</summary>
        public const string Cold = "cold";
        /// <summary>The other person criticised the character.</summary>
        public const string Criticism = "criticism";
        /// <summary>The other person dismissed the character.</summary>
        public const string Dismissal = "dismissal";
        /// <summary>The other person slighted the character.</summary>
        public const string Slight = "slight";

        #endregion Negative
    }
}
