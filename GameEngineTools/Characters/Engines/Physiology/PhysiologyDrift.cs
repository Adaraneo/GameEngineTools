// PhysiologyDrift.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    /// <summary>
    /// Immutable bundle of the per-tick continuous physiological drift deltas
    /// produced by <see cref="DefaultPhysiologyEngine.ComputeDrift"/>.
    /// </summary>
    /// <remarks>
    /// Every value is an additive delta in the same [0..100] scale as
    /// <see cref="PhysiologyState"/>, already scaled by elapsed game hours.
    /// A negative delta reduces the value; for needs that grow over time
    /// (Hunger, Thirst) the awake/sleep branches are positive while the
    /// consuming actions (Eat, Drink) are negative.
    /// </remarks>
    /// <param name="Energy">Energy delta (negative = depletion; 0 during sleep).</param>
    /// <param name="Hunger">Hunger delta (positive = rising; negative while eating).</param>
    /// <param name="Thirst">Thirst delta (positive = rising; negative while drinking).</param>
    /// <param name="Pain">Pain delta (negative = recovery).</param>
    /// <param name="Immune">Immune-load delta (negative = recovery).</param>
    internal readonly record struct PhysiologyDrift(
        double Energy,
        double Hunger,
        double Thirst,
        double Pain,
        double Immune);
}
