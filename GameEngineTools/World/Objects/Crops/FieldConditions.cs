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

        /// <summary>Whether the condition is part of the vocabulary (a <c>stage:</c> condition must name at least one real stage, by name, none empty).</summary>
        public static bool IsKnown(string condition)
        {
            if (condition.StartsWith("stage:", StringComparison.Ordinal))
                return Stages(condition) is { Length: > 0 } stages && stages.All(s => s is not null);
            return Plain.Contains(condition);
        }

        // The stages a condition names; an entry is null when it is not the name of a declared stage (empty, numeric text such as "5", or a typo). Numbers are refused because
        // Enum.TryParse would accept them and define stages that the file never names.
        private static FieldStage?[] Stages(string condition)
            => condition["stage:".Length..].Split('|')
                .Select(s => Enum.GetNames<FieldStage>().Contains(s, StringComparer.Ordinal) ? (FieldStage?)Enum.Parse<FieldStage>(s) : null)
                .ToArray();

        /// <summary>Whether the condition holds for the field in the season.</summary>
        /// <exception cref="ArgumentException">The condition is not in the vocabulary.</exception>
        public static bool Holds(string condition, FieldState field, FieldSeason now, CropConfig cfg)
        {
            if (condition.StartsWith("stage:", StringComparison.Ordinal))
                return IsKnown(condition) ? Stages(condition).Any(s => s == field.Stage) : throw new ArgumentException($"Unknown field condition '{condition}'.", nameof(condition));

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
