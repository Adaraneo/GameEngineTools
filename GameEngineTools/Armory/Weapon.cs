// Weapon.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

    /// <summary>A weapon with a type, a name and current/maximum durability (hit points).</summary>
    public class Weapon
    {
        /// <summary>Creates an empty weapon (for deserialization).</summary>
        public Weapon()
        { }

        /// <summary>Creates a weapon at full durability.</summary>
        /// <param name="name">Display name.</param>
        /// <param name="weaponType">Weapon category.</param>
        /// <param name="maxHitPoints">Maximum (and initial) durability.</param>
        public Weapon(string name, WeaponType weaponType, double maxHitPoints)
        {
            Name = name;
            Type = weaponType;
            MaxHitPoints = maxHitPoints;
            HitPoints = maxHitPoints;
        }

        /// <summary>Category of weapon.</summary>
        public enum WeaponType
        {
            /// <summary>One-handed sword.</summary>
            OneHandedSword,

            /// <summary>Two-handed sword.</summary>
            TwoHandedSword,

            /// <summary>One-handed axe.</summary>
            OneHandedAxe,

            /// <summary>Polearm.</summary>
            Polearm,

            /// <summary>Large two-handed axe.</summary>
            BigAxe,

            /// <summary>Dagger.</summary>
            Dagger
        }

        /// <summary>Current durability.</summary>
        public double HitPoints { get; set; }

        /// <summary>Maximum durability when undamaged.</summary>
        [JsonInclude]
        public double MaxHitPoints { get; internal set; }

        /// <summary>Display name.</summary>
        [JsonInclude]
        public string Name { get; internal set; }

        /// <summary>Weapon category.</summary>
        public WeaponType Type { get; set; }

        /// <summary>Renames the weapon.</summary>
        /// <param name="name">The new name.</param>
        public void NameWeapon(string name)
        {
            Name = name;
        }
    }
}
