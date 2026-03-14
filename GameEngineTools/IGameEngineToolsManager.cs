// IGameEngineToolsManager.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.GameObjects;

    public interface IGameEngineToolsManager
    {
        List<CharacterBase> Characters { get; }

        //IServiceProvider ServiceProvider { get; }

        void Initialize();

        //IClock Clock { get; }
    }
}
