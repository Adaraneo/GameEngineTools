// DefaultMemoryFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Sleep;
using Microsoft.Extensions.Options;
using static GameEngineTools.Characters.Engines.ActionNames;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Default memory fidelity policy backed by current runtime LOD.
    /// </summary>
    public sealed class DefaultMemoryFidelityPolicy : IMemoryFidelityPolicy
    {
        #region Private fields

        private readonly CharacterFidelityConfig _cfg;
        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        #endregion

        #region Constructor

        public DefaultMemoryFidelityPolicy(
            IOptions<CharacterFidelityConfig> cfg,
            ICognitiveResolutionLevelRuntime lodRuntime)
        {
            _cfg = cfg.Value;
            _lodRuntime = lodRuntime;
        }

        #endregion

        #region IMemoryFidelityPolicy

        public MemoryFidelityLevel GetLevel(HumanId human)
            => _lodRuntime.Get(human) switch
            {
                CognitiveResolutionLevel.Player => _cfg.PlayerMemory,
                CognitiveResolutionLevel.Nearby => _cfg.NearbyMemory,
                CognitiveResolutionLevel.Background => _cfg.BackgroundMemory,
                _ => MemoryFidelityLevel.Full
            };

        public bool ShouldStoreEvent(IHumanContext ctx, IDomainEvent @event)
            => GetLevel(ctx.Id) switch
            {
                MemoryFidelityLevel.Full => true,
                MemoryFidelityLevel.Reduced => ShouldStoreReduced(@event),
                MemoryFidelityLevel.Minimal => ShouldStoreMinimal(@event),
                _ => true
            };

        #endregion

        #region Private helpers

        private static bool ShouldStoreReduced(IDomainEvent @event)
            => @event switch
            {
                ActionCommitted ac => IsMeaningfulAction(ac.ActionName) || ac.TargetHuman is not null,
                InteractionOutcome => true,
                FirstImpressionFormed => true,
                MicroPositive => true,
                MicroNegative => true,
                RepairAttempt => true,
                SexualEncounterOutcome => true,
                NightmareTriggered => true,
                SleepEnded => true,
                _ => true
            };

        private static bool ShouldStoreMinimal(IDomainEvent @event)
            => @event switch
            {
                ActionCommitted ac => IsMeaningfulAction(ac.ActionName) || ac.TargetHuman is not null,
                InteractionOutcome => true,
                FirstImpressionFormed => true,
                MicroPositive mp => IsImportantPositiveMicroKind(mp.Kind),
                MicroNegative => true,
                RepairAttempt => true,
                SexualEncounterOutcome => true,
                NightmareTriggered => true,
                SleepEnded => true,
                _ => false
            };

        private static bool IsMeaningfulAction(string actionName)
            => actionName is InviteIntimacy or ReachOut or SelfCare or Flee or Fight;

        private static bool IsImportantPositiveMicroKind(string kind)
        {
            var normalized = kind.Trim().ToLowerInvariant();
            return normalized is MemoryMicroEventKinds.Help
                or MemoryMicroEventKinds.Support
                or MemoryMicroEventKinds.Repair
                or MemoryMicroEventKinds.Validation;
        }

        #endregion
    }
}
