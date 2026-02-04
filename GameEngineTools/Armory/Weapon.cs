// Weapon.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    using System.Text.Json.Serialization;

    public class Weapon
    {
        public Weapon()
        { }

        public Weapon(string name, WeaponType weaponType, double maxHitPoints)
        {
            Name = name;
            Type = weaponType;
            MaxHitPoints = maxHitPoints;
            HitPoints = maxHitPoints;
        }

        public enum WeaponType
        { OneHandedSword, TwoHandedSword, OneHandedAxe, Polearm, BigAxe }

        public double HitPoints { get; set; }

        [JsonInclude]
        public double MaxHitPoints { get; internal set; }

        public string Name { get; internal set; }
        public WeaponType Type { get; set; }

        public void NameWeapon(string name)
        {
            Name = name;
        }
    }
}
