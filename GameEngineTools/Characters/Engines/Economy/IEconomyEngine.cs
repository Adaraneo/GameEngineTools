// IEconomyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Economy
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Owns a character's <see cref="EconomyState.Wealth"/>: applies wages on committed <c>Work</c>,
    /// and deducts/credits coin on <see cref="Purchased"/>/<see cref="Sold"/> events emitted by the
    /// object-interaction commit path. A thin, purely event-reactive auxiliary engine ticked after
    /// Memory/SemanticMemory, mirroring the bereavement and social-comparison engines.
    /// </summary>
    public interface IEconomyEngine : IEngine<EconomyState, EconomyConfig>
    {
    }
}
