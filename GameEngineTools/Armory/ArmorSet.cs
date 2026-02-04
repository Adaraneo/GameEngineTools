// ArmorSet.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

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

        [JsonConstructor]
        public ArmorSet(string name, List<ArmorPart> parts)
        {
            Name = name;
            Parts = parts;
            CalculateMaxProtection();
            CalculateProtection();
        }

        [JsonIgnore]
        public double MaxProtection { get; private set; }

        public string Name { get; set; }

        [JsonInclude]
        public List<ArmorPart> Parts { get; private set; }

        [JsonIgnore]
        public double Protection { get; private set; }

        public void AddPart(ArmorPart part)
        {
            Parts.Add(part);
            CalculateMaxProtection();
            CalculateProtection();
        }

        public void RemovePart(ArmorPart part)
        {
            Parts.Remove(part);
            CalculateMaxProtection();
            CalculateProtection();
        }
    }
}
