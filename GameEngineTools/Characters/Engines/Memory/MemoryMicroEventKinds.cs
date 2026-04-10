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

        public const string Help = "help";
        public const string Support = "support";
        public const string Repair = "repair";
        public const string Warmth = "warmth";
        public const string Validation = "validation";

        #endregion

        #region Negative

        public const string Ignore = "ignore";
        public const string Cold = "cold";
        public const string Criticism = "criticism";
        public const string Dismissal = "dismissal";
        public const string Slight = "slight";

        #endregion
    }
}
