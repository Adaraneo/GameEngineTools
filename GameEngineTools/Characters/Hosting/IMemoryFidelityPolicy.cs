// IMemoryFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Resolves runtime memory fidelity and filters memory ingestion.
    /// </summary>
    public interface IMemoryFidelityPolicy
    {
        MemoryFidelityLevel GetLevel(HumanId human);

        bool ShouldStoreEvent(IHumanContext ctx, IDomainEvent @event);
    }
}
