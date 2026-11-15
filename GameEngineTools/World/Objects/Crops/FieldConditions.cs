// FieldConditions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
    using System.Linq;

    /// <summary>
    /// The conditions a field task can name in <c>tasks.json</c> (<c>requires</c>); all of a task's conditions must hold. This closed list is the whole vocabulary: a new job
    /// is built from these, a new condition needs code here. Pure, so the rules can be tested against any field, season and settings.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>stage:A|B</c> the field is in one of the stages (Fallow, Ploughed, Sown, Ripe, Reaped).</item>
    /// <item><c>pending</c> reaped grain is still in the sheaf; <c>no_pending</c> there is none.</item>
    /// <item><c>workable</c> the air is warm enough to work the ground (<c>MinWorkTemperatureCelsius</c>).</item>
    /// <item><c>rested</c> the field has lain fallow the required years since it was reaped.</item>
    /// <item><c>sowing_weather</c> cooling autumn weather between the sowing limits.</item>
    /// <item><c>seed_enough</c> the bin holds at least the minimum share of the seed this field needs.</item>
    /// </list>
    /// </remarks>
    public static class FieldConditions
    {
        private static readonly string[] Plain = { "pending", "no_pending", "workable", "rested", "sowing_weather", "seed_enough" };

        /// <summary>Whether the condition is part of the vocabulary (a <c>stage:</c> condition must name real stages).</summary>
        public static bool IsKnown(string condition)
        {
            if (condition.StartsWith("stage:", StringComparison.Ordinal))
                return condition["stage:".Length..].Split('|').All(s => Enum.TryParse<FieldStage>(s, ignoreCase: false, out _));
            return Plain.Contains(condition);
        }

        /// <summary>Whether the condition holds for the field in the season.</summary>
        /// <exception cref="ArgumentException">The condition is not in the vocabulary.</exception>
        public static bool Holds(string condition, FieldState field, FieldSeason now, CropConfig cfg)
        {
            if (condition.StartsWith("stage:", StringComparison.Ordinal))
                return condition["stage:".Length..].Split('|').Any(s => Enum.TryParse<FieldStage>(s, ignoreCase: false, out var stage) && field.Stage == stage);

            return condition switch
            {
                "pending" => field.PendingUnits > 0,
                "no_pending" => field.PendingUnits <= 0,
                "workable" => now.TemperatureCelsius >= cfg.MinWorkTemperatureCelsius,
                "rested" => field.ReapedYear is not { } reaped || now.Year >= reaped + (int)Math.Round(cfg.FallowYears),
                "sowing_weather" => CropMath.IsSowingWeather(now.TemperatureCelsius, now.TemperatureTrendPerDay, cfg),
                "seed_enough" => now.SeedInStore >= CropMath.SeedUnits(field.AreaHectares, cfg) * cfg.MinSowingShare,
                _ => throw new ArgumentException($"Unknown field condition '{condition}'.", nameof(condition)),
            };
        }
    }
}
