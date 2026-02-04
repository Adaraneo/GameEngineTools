// ArmorPart.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

    public class ArmorPart : IArmor
    {
        public ArmorPart()
        { }

        public ArmorPart(string name, PartType armorType, double maxProtection)
        {
            Name = name;
            TypeOfPart = armorType;
            this.Protection = maxProtection;
            MaxProtection = maxProtection;
        }

        public enum PartType
        { Head, Shoulders, Legs, Feets, Hands, Breasts, Back, Arms, Belly, Chest, Shield }

        [JsonInclude]
        public double MaxProtection { get; internal set; }

        [JsonInclude]
        public string Name { get; internal set; }

        [JsonInclude]
        public double Protection { get; set; }

        [JsonInclude]
        public PartType TypeOfPart { get; internal set; }
    }
}
