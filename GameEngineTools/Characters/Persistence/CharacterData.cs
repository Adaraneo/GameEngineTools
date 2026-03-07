// CharacterData.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Persistence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    public sealed record CharacterData
    {
        public HumanId Id { get; init; }
        public Identity Identity { get; init; }
        public SexBiology Biology { get; init; }
        public Personality Personality { get; init; }
        public EnginesSnapshot Snapshot { get; init; }
    }
}
