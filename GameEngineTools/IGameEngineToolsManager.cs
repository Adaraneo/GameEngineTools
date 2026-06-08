// IGameEngineToolsManager.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.GameObjects;

    /// <summary>Top-level entry point that owns the loaded characters and bootstraps the engine.</summary>
    public interface IGameEngineToolsManager
    {
        /// <summary>The loaded characters.</summary>
        List<CharacterBase> Characters { get; }

        //IServiceProvider ServiceProvider { get; }

        /// <summary>Initialises the manager (loads data, wires services).</summary>
        void Initialize();

        //IClock Clock { get; }
    }
}
