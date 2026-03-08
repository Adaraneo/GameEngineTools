// CharacterData.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Persistence
{
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    public sealed record CharacterData
    {
        public HumanId Id { get; init; }
        public required Identity Identity { get; init; }
        public SexBiology Biology { get; init; }
        public required Personality Personality { get; init; }
        public required PhysicalAppearance PhysicalAppearance { get; init; }
        public required EnginesSnapshot Snapshot { get; init; }

        public double MaxHealth { get; init; }
        public double Health { get; init; }
        public ArmorSet? Armor { get; init; }
        public Weapon? Weapon { get; init; }
        public double Protection { get; init; }
    }
}
