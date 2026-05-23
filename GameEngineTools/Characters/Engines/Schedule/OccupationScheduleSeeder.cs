// OccupationScheduleSeeder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Generates a list of <see cref="ScheduleSlot"/> entries by looking up an occupation
    /// definition in an <see cref="IOccupationRegistry"/>, then modulating the slots based
    /// on the character's personality (chronotype and motivations).
    /// </summary>
    public static class OccupationScheduleSeeder
    {
        #region Public API

        /// <summary>
        /// Builds the slot list for <paramref name="occupationId"/>, applies personality
        /// modulation, and returns the result.
        /// Returns an empty list when <paramref name="occupationId"/> is null, empty,
        /// or not found in <paramref name="registry"/>.
        /// </summary>
        /// <param name="occupationId">
        /// Occupation identifier. Use <see cref="OccupationIds"/> constants for built-ins,
        /// or a custom ID for modded occupations.
        /// </param>
        /// <param name="personality">Personality used for chronotype shift and motivation boost.</param>
        /// <param name="registry">Registry that resolves the occupation definition.</param>
        /// <param name="locationOverrides">
        /// Optional mapping of symbolic location keys (e.g. <c>"WorkLocation"</c>) to
        /// game-specific location IDs. When <c>null</c>, all location IDs are stripped so
        /// no location bias is applied by the modifier.
        /// </param>
        public static IReadOnlyList<ScheduleSlot> Seed(
            string? occupationId,
            Personality personality,
            IOccupationRegistry registry,
            IDictionary<string, string>? locationOverrides = null)
        {
            if (string.IsNullOrEmpty(occupationId))
                return Array.Empty<ScheduleSlot>();

            var definition = registry.TryGet(occupationId);
            if (definition is null || definition.Slots.Count == 0)
                return Array.Empty<ScheduleSlot>();

            var slots = TemplatesToSlots(definition.Slots);
            slots = ApplyPersonalityModulation(slots, personality);
            slots = ApplyLocationOverrides(slots, locationOverrides);
            return slots;
        }

        #endregion Public API

        #region Private helpers

        private static List<ScheduleSlot> TemplatesToSlots(IReadOnlyList<ScheduleSlotTemplate> templates)
        {
            var result = new List<ScheduleSlot>(templates.Count);
            foreach (var t in templates)
            {
                result.Add(new ScheduleSlot(
                    t.SlotId,
                    t.HourOfDay,
                    t.Action,
                    t.LocationKey,
                    t.BiasStrength,
                    t.CanSkipWhenStressed));
            }
            return result;
        }

        private static List<ScheduleSlot> ApplyPersonalityModulation(List<ScheduleSlot> slots, Personality personality)
        {
            var hoursPerDay = WWorld.IsConfigured ? WWorld.Spec.HoursPerDay : 24;

            // Chronotype shift
            var hourOffset = personality.Chronotype switch
            {
                Chronotype.Lark => -1,
                Chronotype.Owl => 2,
                _ => 0
            };

            // Motivation boosts
            var boostReachOut = personality.Motivation.Affiliation > 0.75 ? 0.1 : 0.0;
            var boostWork = personality.Motivation.Competence > 0.75 ? 0.1 : 0.0;

            var result = new List<ScheduleSlot>(slots.Count);
            foreach (var slot in slots)
            {
                var hour = Math.Clamp(slot.HourOfDay + hourOffset, 0, hoursPerDay - 1);
                var strength = slot.BiasStrength;

                if (slot.PreferredAction == ReachOut)
                    strength += boostReachOut;
                else if (slot.PreferredAction == Work || slot.PreferredAction == Create)
                    strength += boostWork;

                strength = Math.Clamp(strength, 0.1, 1.0);
                result.Add(slot with { HourOfDay = hour, BiasStrength = strength });
            }

            return result;
        }

        private static List<ScheduleSlot> ApplyLocationOverrides(
            List<ScheduleSlot> slots,
            IDictionary<string, string>? overrides)
        {
            if (overrides is null || overrides.Count == 0)
            {
                // Without overrides, strip location IDs so no location bias fires
                var stripped = new List<ScheduleSlot>(slots.Count);
                foreach (var slot in slots)
                    stripped.Add(slot with { PreferredLocationId = null });
                return stripped;
            }

            var result = new List<ScheduleSlot>(slots.Count);
            foreach (var slot in slots)
            {
                var locationId = slot.PreferredLocationId is not null
                    && overrides.TryGetValue(slot.PreferredLocationId, out var mapped)
                    ? mapped
                    : null;
                result.Add(slot with { PreferredLocationId = locationId });
            }
            return result;
        }

        #endregion Private helpers
    }
}
