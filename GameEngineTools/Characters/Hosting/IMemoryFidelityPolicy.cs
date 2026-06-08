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
        /// <summary>Returns the memory fidelity tier for a character.</summary>
        /// <param name="human">Character identifier.</param>
        MemoryFidelityLevel GetLevel(HumanId human);

        /// <summary>Returns whether the given event should be stored at the character's current fidelity.</summary>
        /// <param name="ctx">Character context.</param>
        /// <param name="event">The event being considered.</param>
        bool ShouldStoreEvent(IHumanContext ctx, IDomainEvent @event);
    }
}
