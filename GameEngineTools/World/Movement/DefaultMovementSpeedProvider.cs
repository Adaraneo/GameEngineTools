// DefaultMovementSpeedProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement;

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Location;
using Microsoft.Extensions.Options;

/// <summary>
/// Default implementation of <see cref="IMovementSpeedProvider"/>.
/// Computes walking speed in metres per minute from physiological state, terrain,
/// and age-related sarcopenia.
/// </summary>
/// <remarks>
/// Base speed is 80 m/min (~4.8 km/h, comfortable walking pace). Multipliers stack
/// multiplicatively: whole-body fatigue × graded pain penalty × terrain × muscle mass.
/// All tuning constants live in <see cref="MovementConfig"/> (literature-cited) rather
/// than as hardcoded values, so balancing as the world grows requires no recompile.
/// </remarks>
public sealed class DefaultMovementSpeedProvider : IMovementSpeedProvider
{
    private readonly MovementConfig _config;

    /// <summary>
    /// Creates the provider with the supplied movement tuning configuration.
    /// </summary>
    /// <param name="config">Movement tuning configuration (literature-cited defaults).</param>
    public DefaultMovementSpeedProvider(IOptions<MovementConfig> config)
        => _config = config.Value;

    /// <inheritdoc/>
    public double GetSpeedMetersPerMinute(EnginesSnapshot snapshot, TerrainType terrain = TerrainType.Indoor)
    {
        var physio = snapshot.Physiology;

        var fatigueMultiplier = physio.Energy < _config.FatigueEnergyThreshold
            ? _config.FatigueSpeedMultiplier
            : 1.0;

        var painPenalty = ComputePainPenalty(physio.Pain, _config);

        // Age-related slowdown, driven by MuscleMassFraction (sarcopenia), NOT raw age.
        // Source: Sloot et al. 2021, Gait & Posture 90:475-482 — push-off power explains
        // 54% of gait-speed variance vs. 4% for chronological age alone. No precise
        // dose-response formula exists in the literature; this is a deliberate linear
        // simplification pending future refinement, NOT a literature-derived curve.
        // The ?? 1.0 preserves backward compatibility with old snapshots that lack Aging data.
        var muscleMultiplier = snapshot.Physiology.Aging?.MuscleMassFraction ?? 1.0;

        return _config.BaseSpeedMetersPerMinute
            * fatigueMultiplier
            * painPenalty
            * TerrainMultiplier(terrain, _config)
            * muscleMultiplier;
    }

    /// <summary>
    /// Computes the pain-related speed penalty as a linear interpolation between two
    /// literature-anchored points, clamped at the high end.
    /// Source: Dal Farra et al. 2025, Front Pain Res 6:1693068 (low point, ~12% at moderate pain);
    /// Seydi et al. 2025, J Pain 29:104758 (effect direction confirmed; high point is a
    /// conservative upper bound, not independently measured at this severity).
    /// </summary>
    private static double ComputePainPenalty(double pain, MovementConfig cfg)
    {
        if (pain <= cfg.PainThresholdLow)
            return 1.0;

        var t = (pain - cfg.PainThresholdLow) / (cfg.PainThresholdHigh - cfg.PainThresholdLow);
        var reduction = cfg.PainPenaltyAtThresholdLow
            + Math.Clamp(t, 0.0, 1.0) * (cfg.PainPenaltyAtThresholdHigh - cfg.PainPenaltyAtThresholdLow);

        return 1.0 - reduction;
    }

    /// <summary>
    /// Resolves the speed multiplier for the given terrain from configuration.
    /// Unknown/future <see cref="TerrainType"/> values fall back to 1.00 (no penalty)
    /// rather than throwing — important for forward compatibility as the enum grows.
    /// </summary>
    private static double TerrainMultiplier(TerrainType terrain, MovementConfig cfg) =>
        cfg.EffectiveTerrainMultipliers.TryGetValue(terrain, out var m) ? m : 1.00;
}
