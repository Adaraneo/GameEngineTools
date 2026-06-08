// Surname.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    /// <summary>A surname with its sex-specific forms (as required by some languages).</summary>
    public class Surname
    {
        /// <summary>Male form of the surname.</summary>
        public string Male { get; set; }
        /// <summary>Female form of the surname.</summary>
        public string Female { get; set; }
    }
}
