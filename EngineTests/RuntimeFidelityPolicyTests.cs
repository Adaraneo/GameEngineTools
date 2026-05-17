// RuntimeFidelityPolicyTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class RuntimeFidelityPolicyTests : TestBase
    {
        [TestMethod]
        public void AddCharactersCore_RegistersFidelityPolicies()
        {
            using var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IConfiguration>(Config.ConfigProvider.Configuration)
                .AddCharactersCore()
                .BuildServiceProvider();

            Assert.IsNotNull(services.GetRequiredService<IMemoryFidelityPolicy>());
            Assert.IsNotNull(services.GetRequiredService<IPerceptionFidelityPolicy>());
            Assert.IsNotNull(services.GetRequiredService<ISocialFidelityPolicy>());
        }

        [TestMethod]
        public void PerceptionAndSocialFidelityPolicies_UseRuntimeTier()
        {
            var self = new HumanId(Guid.NewGuid());
            var runtime = new DefaultCognitiveResolutionLevelRuntime();
            var cfg = Options.Create(new CharacterFidelityConfig(
                BackgroundPerception: PerceptionFidelityLevel.Coarse,
                BackgroundSocial: SocialFidelityLevel.Minimal));
            var perception = new DefaultPerceptionFidelityPolicy(cfg, runtime);
            var social = new DefaultSocialFidelityPolicy(cfg, runtime);

            runtime.Set(self, CognitiveResolutionLevel.Background);

            Assert.AreEqual(PerceptionFidelityLevel.Coarse, perception.GetLevel(self));
            Assert.AreEqual(SocialFidelityLevel.Minimal, social.GetLevel(self));
        }

        [TestMethod]
        public void MemoryFidelityPolicy_UsesRuntimeTierWithoutRecreatingCharacter()
        {
            var self = new HumanId(Guid.NewGuid());
            var runtime = new DefaultCognitiveResolutionLevelRuntime();
            var policy = BuildMemoryPolicy(runtime, new CharacterFidelityConfig(
                PlayerMemory: MemoryFidelityLevel.Full,
                NearbyMemory: MemoryFidelityLevel.Full,
                BackgroundMemory: MemoryFidelityLevel.Minimal));

            Assert.AreEqual(MemoryFidelityLevel.Full, policy.GetLevel(self));

            runtime.Set(self, CognitiveResolutionLevel.Background);

            Assert.AreEqual(MemoryFidelityLevel.Minimal, policy.GetLevel(self));
        }

        [TestMethod]
        public void MemoryFidelityPolicy_MinimalFiltersRoutineActionsButKeepsSocialEvents()
        {
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var runtime = new DefaultCognitiveResolutionLevelRuntime();
            runtime.Set(self, CognitiveResolutionLevel.Background);
            var policy = BuildMemoryPolicy(runtime, new CharacterFidelityConfig(BackgroundMemory: MemoryFidelityLevel.Minimal));
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;

            Assert.IsFalse(policy.ShouldStoreEvent(ctx, new ActionCommitted(new WDateTime(0), self, Drink, WTimeSpan.FromHours(0.1))));
            Assert.IsTrue(policy.ShouldStoreEvent(ctx, new ActionCommitted(new WDateTime(0), self, ReachOut, WTimeSpan.FromHours(0.5), other)));
            Assert.IsTrue(policy.ShouldStoreEvent(ctx, new InteractionOutcome(new WDateTime(0), self, other, false, "rejected", SpeechAct.Invite)));
        }

        [TestMethod]
        public void MemoryEngine_MinimalFidelitySkipsRoutineActionEncoding()
        {
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var runtime = new DefaultCognitiveResolutionLevelRuntime();
            runtime.Set(self, CognitiveResolutionLevel.Background);
            var policy = BuildMemoryPolicy(runtime, new CharacterFidelityConfig(BackgroundMemory: MemoryFidelityLevel.Minimal));
            var engine = BuildMemoryEngine(policy);
            var ctx = BehaviorComponentTestFactory.Context(selfId: self).HumanContext;
            var outbox = new EventCollector();

            engine.Handle(new ActionCommitted(new WDateTime(0), self, Drink, WTimeSpan.FromHours(0.1)), ctx, outbox);

            Assert.AreEqual(0, engine.State.Episodes.Count);

            engine.Handle(new InteractionOutcome(new WDateTime(0), self, other, false, "rejected", SpeechAct.Invite), ctx, outbox);

            Assert.AreEqual(1, engine.State.Episodes.Count);
            Assert.AreEqual(other, engine.State.Episodes.Single().OtherPerson);
        }

        private static DefaultMemoryFidelityPolicy BuildMemoryPolicy(
            ICognitiveResolutionLevelRuntime runtime,
            CharacterFidelityConfig cfg)
            => new(Options.Create(cfg), runtime);

        private static DefaultMemoryEngine BuildMemoryEngine(IMemoryFidelityPolicy policy)
            => new(
                Options.Create(new MemoryConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                policy);
    }
}
