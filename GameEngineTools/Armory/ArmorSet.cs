// ArmorSet.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

    /// <summary>A collection of <see cref="ArmorPart"/>s whose protection totals are aggregated.</summary>
    public class ArmorSet : IArmor
    {
        private void CalculateMaxProtection()
        {
            MaxProtection = 0;
            foreach (var part in Parts)
            {
                MaxProtection += part.MaxProtection;
            }
        }

        private void CalculateProtection()
        {
            Protection = 0;
            foreach (var part in Parts)
            {
                Protection += part.Protection;
            }
        }

        /// <summary>Creates an armour set from its parts and recomputes aggregate protection.</summary>
        /// <param name="name">Display name.</param>
        /// <param name="parts">The armour parts in this set.</param>
        [JsonConstructor]
        public ArmorSet(string name, List<ArmorPart> parts)
        {
            Name = name;
            Parts = parts;
            CalculateMaxProtection();
            CalculateProtection();
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public double MaxProtection { get; private set; }

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <summary>The armour parts that make up this set.</summary>
        [JsonInclude]
        public List<ArmorPart> Parts { get; private set; }

        /// <inheritdoc/>
        [JsonIgnore]
        public double Protection { get; private set; }

        /// <summary>Adds a part to the set and recomputes aggregate protection.</summary>
        /// <param name="part">The part to add.</param>
        public void AddPart(ArmorPart part)
        {
            Parts.Add(part);
            CalculateMaxProtection();
            CalculateProtection();
        }

        /// <summary>Removes a part from the set and recomputes aggregate protection.</summary>
        /// <param name="part">The part to remove.</param>
        public void RemovePart(ArmorPart part)
        {
            Parts.Remove(part);
            CalculateMaxProtection();
            CalculateProtection();
        }
    }
}
