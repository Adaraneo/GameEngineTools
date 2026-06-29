// DefaultMovementSpeedProviderTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.World.Location;
using GameEngineTools.World.Movement;
using Microsoft.Extensions.Options;

namespace EngineTests;

[TestClass]
public class DefaultMovementSpeedProviderTests
{
    private static DefaultMovementSpeedProvider NewProvider(MovementConfig? config = null)
        => new(Options.Create(config ?? new MovementConfig()));

    private static EnginesSnapshot BuildSnapshot(
        double energy,
        double pain,
        PhysicalAgingState? aging = null)
    {
        var physio = new PhysiologyState(
            Energy: energy,
            SleepDebtHours: 0,
            Hunger: 10,
            Thirst: 10,
            Pain: pain,
            ImmuneLoad: 0,
            BodyTempDelta: 0,
            Cycle: null,
            Aging: aging);

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

    // ── Task 1: Energy / Pain recalibration ───────────────────────────────────

    [TestMethod]
    public void GetSpeed_FullHealth_ReturnsBaseSpeed()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        Assert.AreEqual(80.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_LowEnergy_ReducesSpeedTo85Percent()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 20, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Base 80 × 0.85 (whole-body fatigue) = 68
        Assert.AreEqual(68.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_PainAtLowThreshold_NoPenalty()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 40);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Pain == PainThresholdLow (40) → no penalty.
        Assert.AreEqual(80.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_ModeratePain_ReducesSpeedToApprox84Percent()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 50);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // t = (50-40)/(90-40) = 0.2; reduction = 0.12 + 0.2×0.18 = 0.156 → ×0.844.
        // 80 × 0.844 = 67.52
        Assert.AreEqual(67.52, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_SeverePain_ReducesSpeedTo70Percent()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 90);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Pain == PainThresholdHigh (90) → max reduction 0.30 → ×0.70. 80 × 0.70 = 56.
        Assert.AreEqual(56.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_VerySeverePain_ClampedAtHighPenalty()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 100);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Pain above PainThresholdHigh is clamped → same as Pain=90 → ×0.70.
        Assert.AreEqual(56.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_LowEnergyAndModeratePain_StacksMultipliers()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 20, pain: 50);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Base 80 × 0.85 × 0.844 = 57.392
        Assert.AreEqual(57.392, speed, 0.001);
    }

    // ── Task 2: Terrain ───────────────────────────────────────────────────────

    [TestMethod]
    public void GetSpeed_IndoorTerrain_NoChange()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot, TerrainType.Indoor);

        Assert.AreEqual(80.0, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_BogTerrain_ReducesSpeedToApprox56Percent()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot, TerrainType.Water);

        // Water (bog/wetland) multiplier 0.56 → 80 × 0.56 = 44.8 (≈56% of base).
        Assert.AreEqual(44.8, speed, 0.001);
    }

    [TestMethod]
    public void GetSpeed_UnknownFutureTerrain_FallsBackToNoPenalty()
    {
        // Config whose terrain dictionary intentionally omits Forest — simulates a
        // future TerrainType value that has no configured multiplier yet.
        var config = new MovementConfig(TerrainMultipliers: new Dictionary<TerrainType, double>
        {
            [TerrainType.Indoor] = 1.00,
        });
        var provider = NewProvider(config);
        var snapshot = BuildSnapshot(energy: 80, pain: 0);

        var speed = provider.GetSpeedMetersPerMinute(snapshot, TerrainType.Forest);

        // No dictionary entry → fallback multiplier 1.00 (no penalty), not an exception.
        Assert.AreEqual(80.0, speed, 0.001);
    }

    // ── Task 3: Aging via MuscleMassFraction ──────────────────────────────────

    [TestMethod]
    public void GetSpeed_Sarcopenia_ScalesLinearlyWithMuscleMass()
    {
        var provider = NewProvider();

        var young = provider.GetSpeedMetersPerMinute(
            BuildSnapshot(energy: 80, pain: 0, aging: new PhysicalAgingState(MuscleMassFraction: 1.0)));
        var sarcopenic = provider.GetSpeedMetersPerMinute(
            BuildSnapshot(energy: 80, pain: 0, aging: new PhysicalAgingState(MuscleMassFraction: 0.7)));

        // Exactly 0.7× of the full-muscle speed, all else equal.
        Assert.AreEqual(0.7 * young, sarcopenic, 0.001);
        Assert.AreEqual(56.0, sarcopenic, 0.001);
    }

    [TestMethod]
    public void GetSpeed_NoAgingData_IdenticalToPreAgingBehavior()
    {
        var provider = NewProvider();
        var snapshot = BuildSnapshot(energy: 80, pain: 0, aging: null);

        var speed = provider.GetSpeedMetersPerMinute(snapshot);

        // Null Aging → muscle multiplier 1.0 (no-op, backward compatible).
        Assert.AreEqual(80.0, speed, 0.001);
    }
}
