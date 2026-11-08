// FieldState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using GameEngineTools.World.Utils.Time;

    /// <summary>Where a field is in the year: bare, worked, planted, ripening, ready to reap, or harvested.</summary>
    public enum FieldStage
    {
        /// <summary>Unworked ground, between a harvest and the next ploughing.</summary>
        Fallow = 0,

        /// <summary>Ploughed and ready for seed.</summary>
        Ploughed = 1,

        /// <summary>Seed in the ground; thermal time accumulates until it matures.</summary>
        Sown = 2,

        /// <summary>Grain is ripe and can be reaped; left standing it shatters and is lost day by day.</summary>
        Ripe = 3,

        /// <summary>Harvested; the stubble stays until the field is ploughed again.</summary>
        Reaped = 4,
    }

    /// <summary>
    /// The crop state of a field object. Everything needed to continue the cycle after a save: the stage, how much thermal time the
    /// crop has collected, when it was sown and ripened, and the field's size.
    /// </summary>
    /// <param name="Stage">Current <see cref="FieldStage"/>.</param>
    /// <param name="AreaHectares">Size of the field.</param>
    /// <param name="SownAt">When the seed went in, or <c>null</c> before sowing.</param>
    /// <param name="RipeAt">When the crop ripened, or <c>null</c> while it has not.</param>
    /// <param name="DegreeDays">Thermal time collected since sowing, in degree-days above the crop's base temperature.</param>
    /// <param name="SeedUnits">Seed that went into the ground, in storage units.</param>
    /// <param name="YearFactor">Multiplier on the expected yield for this crop (weather of the season); 1 is a normal year.</param>
    public sealed record FieldState(
        FieldStage Stage,
        double AreaHectares,
        WDateTime? SownAt = null,
        WDateTime? RipeAt = null,
        double DegreeDays = 0.0,
        int SeedUnits = 0,
        double YearFactor = 1.0);
}
