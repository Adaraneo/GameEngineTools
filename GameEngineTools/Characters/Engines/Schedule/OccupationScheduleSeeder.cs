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
    /// Generates a list of <see cref="ScheduleSlot"/> entries from an occupation template,
    /// then modulates them based on the character's personality (chronotype and motivations).
    /// </summary>
    public static class OccupationScheduleSeeder
    {
        #region Public API

        /// <summary>
        /// Builds the slot list for <paramref name="occupation"/>, applies personality
        /// modulation, and returns the result.
        /// </summary>
        /// <param name="occupation">Occupation that determines the base slot template.</param>
        /// <param name="personality">Personality used for chronotype and motivation modulation.</param>
        /// <param name="locationOverrides">
        /// Optional mapping of symbolic location names (e.g. <c>"WorkLocation"</c>) to
        /// game-specific location IDs. When <c>null</c>, symbolic names are preserved as-is
        /// but <see cref="ScheduleSlot.PreferredLocationId"/> is set to <c>null</c> so no
        /// location bias is applied by the modifier.
        /// </param>
        public static IReadOnlyList<ScheduleSlot> Seed(
            OccupationKind occupation,
            Personality personality,
            IDictionary<string, string>? locationOverrides = null)
        {
            var slots = BuildBaseSlots(occupation);
            if (slots.Count == 0) return slots;

            slots = ApplyPersonalityModulation(slots, personality);
            slots = ApplyLocationOverrides(slots, locationOverrides);

            return slots;
        }

        #endregion

        #region Base slot tables

        private static List<ScheduleSlot> BuildBaseSlots(OccupationKind occupation)
        {
            return occupation switch
            {
                OccupationKind.Craftsperson => new List<ScheduleSlot>
                {
                    Slot("craftsperson_work_morning",  7,  Work,      "WorkLocation",  0.8, true),
                    Slot("craftsperson_work_afternoon", 13, Work,     "WorkLocation",  0.7, true),
                    Slot("craftsperson_social_evening", 19, ReachOut, "HomeLocation",  0.5, false)
                },

                OccupationKind.Merchant => new List<ScheduleSlot>
                {
                    Slot("merchant_move_morning",    6,  MoveToPublic,  "MarketLocation", 0.9, true),
                    Slot("merchant_work_day",        8,  Work,          "MarketLocation", 0.8, true),
                    Slot("merchant_social_evening",  17, ReachOut,      "TavernLocation", 0.6, false)
                },

                OccupationKind.Scholar => new List<ScheduleSlot>
                {
                    Slot("scholar_work_morning",     8,  Work,     "LibraryLocation", 0.8, true),
                    Slot("scholar_create_afternoon", 14, Create,   "LibraryLocation", 0.7, true),
                    Slot("scholar_care_evening",     20, SelfCare, "HomeLocation",    0.5, false)
                },

                OccupationKind.Farmer => new List<ScheduleSlot>
                {
                    Slot("farmer_work_morning",    5,  Work, "FieldLocation", 0.9, true),
                    Slot("farmer_eat_midday",      12, Eat,  "HomeLocation",  0.8, false),
                    Slot("farmer_work_afternoon",  14, Work, "FieldLocation", 0.7, true),
                    Slot("farmer_rest_evening",    19, Idle, "HomeLocation",  0.6, false)
                },

                OccupationKind.Guard => new List<ScheduleSlot>
                {
                    Slot("guard_work_day",         6,  Work, "GateLocation", 0.9, true),
                    Slot("guard_work_night",       18, Work, "GateLocation", 0.9, true)
                },

                OccupationKind.Healer => new List<ScheduleSlot>
                {
                    Slot("healer_work_day",        8,  Work,     "ClinicLocation", 0.8, true),
                    Slot("healer_care_evening",    19, SelfCare, "HomeLocation",   0.7, false)
                },

                OccupationKind.Artist => new List<ScheduleSlot>
                {
                    Slot("artist_create_morning",  9,  Create,  null,            0.8, true),
                    Slot("artist_social_evening",  19, ReachOut, "TavernLocation", 0.7, false)
                },

                OccupationKind.Laborer => new List<ScheduleSlot>
                {
                    Slot("laborer_work_morning",   6,  Work, null, 0.9, true),
                    Slot("laborer_eat_midday",     12, Eat,  null, 0.8, false),
                    Slot("laborer_rest_evening",   18, Idle, null, 0.6, false)
                },

                _ => new List<ScheduleSlot>()  // OccupationKind.None
            };
        }

        private static ScheduleSlot Slot(
            string id,
            int hour,
            string action,
            string? location,
            double strength,
            bool canSkip)
            => new(id, hour, action, location, strength, canSkip);

        #endregion

        #region Personality modulation

        private static List<ScheduleSlot> ApplyPersonalityModulation(List<ScheduleSlot> slots, Personality personality)
        {
            var hoursPerDay = WWorld.IsConfigured ? WWorld.Spec.HoursPerDay : 24;

            // Chronotype shift
            var hourOffset = personality.Chronotype switch
            {
                Chronotype.Lark => -1,
                Chronotype.Owl  =>  2,
                _               =>  0
            };

            // Motivation boosts
            var boostReachOut = personality.Motivation.Affiliation > 0.75 ? 0.1 : 0.0;
            var boostWork     = personality.Motivation.Competence  > 0.75 ? 0.1 : 0.0;

            var result = new List<ScheduleSlot>(slots.Count);

            foreach (var slot in slots)
            {
                var hour     = Math.Clamp(slot.HourOfDay + hourOffset, 0, hoursPerDay - 1);
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
                // Without overrides, clear location IDs so no location bias fires
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

        #endregion
    }
}
