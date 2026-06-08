// ArmorPart.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

    /// <summary>A single piece of armour covering one body part.</summary>
    public class ArmorPart : IArmor
    {
        /// <summary>Creates an empty armour part (for deserialization).</summary>
        public ArmorPart()
        { }

        /// <summary>Creates an armour part at full protection.</summary>
        /// <param name="name">Display name.</param>
        /// <param name="armorType">Body part this piece covers.</param>
        /// <param name="maxProtection">Maximum (and initial) protection value.</param>
        public ArmorPart(string name, PartType armorType, double maxProtection)
        {
            Name = name;
            TypeOfPart = armorType;
            this.Protection = maxProtection;
            MaxProtection = maxProtection;
        }

        /// <summary>Body part an <see cref="ArmorPart"/> covers.</summary>
        public enum PartType
        {
            /// <summary>Head.</summary>
            Head,

            /// <summary>Shoulders.</summary>
            Shoulders,

            /// <summary>Legs.</summary>
            Legs,

            /// <summary>Feet.</summary>
            Feets,

            /// <summary>Hands.</summary>
            Hands,

            /// <summary>Breasts.</summary>
            Breasts,

            /// <summary>Back.</summary>
            Back,

            /// <summary>Arms.</summary>
            Arms,

            /// <summary>Belly.</summary>
            Belly,

            /// <summary>Chest.</summary>
            Chest,

            /// <summary>Shield.</summary>
            Shield
        }

        /// <inheritdoc/>
        [JsonInclude]
        public double MaxProtection { get; internal set; }

        /// <inheritdoc/>
        [JsonInclude]
        public string Name { get; internal set; }

        /// <inheritdoc/>
        [JsonInclude]
        public double Protection { get; set; }

        /// <summary>Body part this piece covers.</summary>
        [JsonInclude]
        public PartType TypeOfPart { get; internal set; }
    }
}
