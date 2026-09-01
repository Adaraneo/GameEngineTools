// MovementConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Movement
{
    using GameEngineTools.World.Location;

    /// <summary>
    /// Tuning constants for <see cref="DefaultMovementSpeedProvider"/>.
    /// </summary>
    /// <param name="BaseSpeedMetersPerMinute">
    /// Comfortable walking pace with no penalties, in metres per minute (= 1.333 m/s).
    /// Source: Bohannon &amp; Williams Andrews 2011, Physiotherapy 97(3):182-189 (descriptive
    /// meta-analysis, n=23,111 healthy adults, 41 studies). Corroborated: Murtagh, Mair,
    /// Aguiar, Tudor-Locke &amp; Murphy 2021, Sports Medicine 51(1):125-141 (outdoor usual
    /// pace 1.31 m/s, 95% CI 1.27-1.35, n=14,015).
    /// </param>
    /// <param name="FatigueEnergyThreshold">
    /// Energy level at or below which the whole-body fatigue speed penalty applies.
    /// Represents whole-body energy depletion, NOT acute muscle fatigue (see
    /// <paramref name="FatigueSpeedMultiplier"/>).
    /// </param>
    /// <param name="FatigueSpeedMultiplier">
    /// Speed multiplier applied when <c>Energy &lt; <paramref name="FatigueEnergyThreshold"/></c>.
    /// Source: Santos et al. 2019, PLOS ONE 14(12):e0226939 — acute local muscle fatigue does
    /// NOT reliably slow gait (6/9 studies: speed increased, compensatory strategy). Source:
    /// Long Life Family Study, Aging Clin Exp Res 2022 (n=4,052) — perceived/whole-body fatigue
    /// associated with modest gait slowing (~10-20%), not muscle-specific fatigue. The previous
    /// 0.6 constant (40% reduction) overstated this effect.
    /// </param>
    /// <param name="PainThresholdLow">
    /// Pain level below which there is no speed penalty. At or above it the graded penalty begins.
    /// </param>
    /// <param name="PainThresholdHigh">
    /// Pain level at which the maximum (clamped) pain penalty is reached.
    /// </param>
    /// <param name="PainPenaltyAtThresholdLow">
    /// Fractional speed reduction at <paramref name="PainThresholdLow"/> (moderate pain).
    /// Source: Dal Farra et al. 2025, Front Pain Res 6:1693068 — meta-analysis of chronic
    /// non-specific low-back pain, gait velocity drop −15.42 cm/s (95% CI −22.78 to −8.06)
    /// ≈ −12% at baseline ~130 cm/s.
    /// </param>
    /// <param name="PainPenaltyAtThresholdHigh">
    /// Fractional speed reduction at <paramref name="PainThresholdHigh"/> (severe pain).
    /// Source: Seydi et al. 2025, J Pain 29:104758 (meta-analysis, Hedge's g=−0.30) confirms
    /// the effect exists but does not quantify the extreme end of the scale; −30% is retained
    /// as a conservative upper bound for severe/acute pain, not a precisely measured value.
    /// </param>
    /// <param name="TerrainMultipliers">
    /// Speed multiplier per <see cref="TerrainType"/>. Configurable (not hardcoded) so that
    /// terrain balancing as the world/map grows does not require a recompile — see Task 2
    /// for the literature-derived defaults and "Budoucí rozšiřitelnost".
    /// </param>
    public sealed record MovementConfig(
        double BaseSpeedMetersPerMinute = 80.0,
        double FatigueEnergyThreshold = 30.0,
        double FatigueSpeedMultiplier = 0.85,
        double PainThresholdLow = 40.0,
        double PainThresholdHigh = 90.0,
        double PainPenaltyAtThresholdLow = 0.12,
        double PainPenaltyAtThresholdHigh = 0.30,
        IReadOnlyDictionary<TerrainType, double>? TerrainMultipliers = null)
    {

        public MovementConfig() : this(80, 30, 0.85, 40, 90, 0.12, 0.30, null) { }
        /// <summary>
        /// Effective terrain multipliers — <see cref="TerrainMultipliers"/> if explicitly
        /// configured, otherwise the literature-derived defaults below.
        /// Unknown/future <see cref="TerrainType"/> values not present in the dictionary
        /// fall back to 1.00 (no penalty) rather than throwing.
        /// </summary>
        public IReadOnlyDictionary<TerrainType, double> EffectiveTerrainMultipliers =>
            TerrainMultipliers ?? DefaultTerrainMultipliers;

        private static readonly IReadOnlyDictionary<TerrainType, double> DefaultTerrainMultipliers =
            new Dictionary<TerrainType, double>
            {
                // Source: de Gruchy, Caswell & Edwards 2017, Internet Archaeology 45,
                // DOI 10.11141/ia.45.4 — velocity coefficients (n=10), multiplier = 1/coefficient.
                [TerrainType.Indoor] = 1.00, // direct analog: smooth indoor floor ≈ pavement
                [TerrainType.Road] = 0.97, // "paved OR dirt" per TerrainType doc; dirt road
                                           // not directly tested in velocity literature
                [TerrainType.Courtyard] = 0.97, // analog: lawn grass (1.03 → 0.97)
                [TerrainType.Forest] = 0.78, // ANALOGICAL ESTIMATE — between tall grassland
                                             // (0.74) and disturbed ground (0.81); forest
                                             // floor (roots/undergrowth) not directly tested
                [TerrainType.Water] = 0.56, // analog: bog/wetland crossing (1.79 → 0.56) —
                                            // worst case in the source dataset
                [TerrainType.Mountain] = 0.74, // ⚠️ PLACEHOLDER — gradient/altitude effects are
                                               // NOT covered by flat-terrain velocity coefficients
                                               // at all; see "Explicitně mimo rozsah".
                [TerrainType.Plains] = 1.03, // analog: open/mown grassland, the fastest surface
                                             // in the source dataset besides pavement itself.
                [TerrainType.Coastline] = 0.81, // analog: disturbed/uneven ground (sand, shingle,
                                                // tideline debris) — not directly tested.
            };
    }
}
