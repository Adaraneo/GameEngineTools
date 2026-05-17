// CharacterBase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.GameObjects
{
    using System.Text.Json.Serialization;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Utils.Time;

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

        [JsonIgnore]
        public int Age => Person.Age;

        [JsonIgnore]
        public StadiumType Stadium => Person.Stadium;

        /// <summary>
        /// Reduces health by <paramref name="amount"/> points.
        /// If health drops to zero the character dies: <see cref="IsDead"/> is set to
        /// <see langword="true"/> and a <see cref="CharacterDied"/> event is delivered
        /// to the underlying <see cref="Person"/>.
        /// Calls on an already-dead character are silently ignored.
        /// </summary>
        /// <param name="amount">Damage to apply. Negative values are clamped to zero.</param>
        /// <param name="now">Current world time — stamped on the <see cref="CharacterDied"/> event.</param>
        public virtual void DecreaseHealth(double amount, WDateTime now)
        {
            if (IsDead)
                return;

            if (amount <= 0)
                return;

            this.health -= amount;

            if (this.health <= 0)
            {
                this.health = 0;
                IsDead = true;

                Person.ReceiveEvent(new CharacterDied(now, Person.Id, amount));
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

        /// <summary>
        /// Increases health by <paramref name="amount"/> points, capped at <see cref="MaxHealth"/>.
        /// Dead characters cannot be healed — this call is silently ignored when <see cref="IsDead"/>
        /// is <see langword="true"/>.
        /// </summary>
        /// <param name="amount">Hit points to restore. Negative values are clamped to zero.</param>
        public virtual void IncreaseHealth(double amount)
        {
            if (IsDead)
                return;

            if (amount <= 0)
                return;

            this.health = Math.Min(this.health + amount, MaxHealth);
        }

        public override int GetHashCode() => Person?.GetHashCode() ?? 0;

        /// <summary>
        /// Gets a value indicating whether this character has died
        /// (i.e. <see cref="Health"/> reached zero or below).
        /// Once true it never returns to false — death is permanent.
        /// </summary>
        public bool IsDead { get; private set; }
    }
}
