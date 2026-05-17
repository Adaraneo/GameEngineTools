// DefaultMovementSpeedProviderTests.cs
// Copyright (c) 50PSoftware

using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.World.Movement;
using System.Collections.Generic;

namespace EngineTests;

[TestClass]
public class DefaultMovementSpeedProviderTests
{
    private static EnginesSnapshot BuildSnapshot(double energy, double pain)
    {
        var physio = new PhysiologyState(
            Energy:        energy,
            SleepDebtHours: 0,
            Hunger:        10,
            Thirst:        10,
            Pain:          pain,
            ImmuneLoad:    0,
            BodyTempDelta: 0,
            Cycle:         null);

        var psych = new PsychologyState(0.1, 0.5, 0.5, 10, 50, DiscreteEmotion.Neutral);

        return new EnginesSnapshot(
            physio,
            psych,
            new BehaviorState(10, 5, 5, 20, 50, 30, null),
            new InteractionSurface("test", false, 0.3, 0.3, SurfaceKind.Unknown),
            new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
            new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()),
            SemanticMemoryState.Empty);
    }

    [TestMethod]
    public void GetSpeed_FullHealth_ReturnsBaseSpeed()
    {
        var provider = new DefaultMovementSpeedProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        Assert.AreEqual(80.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_LowEnergy_ReducesSpeed()
    {
        var provider = new DefaultMovementSpeedProvider();
        var snapshot = BuildSnapshot(energy: 20, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Base 80 × 0.6 = 48
        Assert.AreEqual(48.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_HighPain_ReducesSpeed()
    {
        var provider = new DefaultMovementSpeedProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 60);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Base 80 × 0.7 = 56
        Assert.AreEqual(56.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_LowEnergyAndHighPain_StacksMultipliers()
    {
        var provider = new DefaultMovementSpeedProvider();
        var snapshot = BuildSnapshot(energy: 20, pain: 60);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Base 80 × 0.6 × 0.7 = 33.6
        Assert.AreEqual(33.6, speed, 0.001);
    }
}
