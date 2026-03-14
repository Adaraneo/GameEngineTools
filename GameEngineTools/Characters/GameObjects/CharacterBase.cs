// CharacterBase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.GameObjects
{
    using System.Text.Json.Serialization;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Non-Playable/Playable Character
    /// </summary>
    public abstract class CharacterBase
    {
        [JsonInclude]
        [JsonPropertyName("Armor")]
        private ArmorSet? armor;

        private double health;

        [JsonInclude]
        [JsonPropertyName("Protection")]
        private double protection;

        internal CharacterBase()
        { }

        internal CharacterBase(double maxHealth, IHuman person)
        {
            this.protection = 0;
            MaxHealth = maxHealth;
            Health = MaxHealth;
            this.Person = person;
        }

        [JsonIgnore]
        public ArmorSet? Armor
        {
            get
            {
                return armor;
            }
            set
            {
                this.armor = value;
                this.protection = armor?.Protection ?? 0;
            }
        }

        [JsonInclude]
        public double Health
        {
            get
            {
                return this.health;
            }
            internal set
            {
                this.health = value;
            }
        }

        public double MaxHealth { get; init; }

        public IHuman Person { get; init; }

        [JsonIgnore]
        public double Protection
        {
            get
            {
                return this.protection;
            }
        }

        public Weapon? Weapon { get; set; }

        public virtual void DecreaseHealth(double amount)
        {
            this.health -= amount;
            if (this.health <= 0)
            {
                // TODO: Person->Die
            }
        }

        public bool Equals(CharacterBase other)
        {
            if (other == null)
            {
                return false;
            }

            return this.Person.Equals(other.Person);
        }

        public override bool Equals(object? obj) => obj is CharacterBase other && Equals(other);

        public virtual void IncreaseHealth(double amount)
        {
            this.health += amount;
            if (health > MaxHealth)
            {
                this.health = MaxHealth;
            }
        }

        public override int GetHashCode() => Person?.GetHashCode() ?? 0;
    }
}
