// Name.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    /// <summary>A character's given name plus any familiar/diminutive forms.</summary>
    public class Name
    {
        /// <summary>The canonical given name.</summary>
        public string Original { get; set; }

        /// <summary>Familiar / diminutive forms of the name.</summary>
        public string[] Familiar { get; set; }
    }
}
