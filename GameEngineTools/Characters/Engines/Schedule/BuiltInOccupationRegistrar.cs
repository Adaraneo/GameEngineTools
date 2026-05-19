// BuiltInOccupationRegistrar.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Registers all built-in occupation definitions into an <see cref="IOccupationRegistry"/>.
    /// Called automatically by the DI setup in <c>AddCharactersCore()</c>.
    /// </summary>
    public static class BuiltInOccupationRegistrar
    {
        /// <summary>Registers every built-in occupation into <paramref name="registry"/>.</summary>
        public static void RegisterAll(IOccupationRegistry registry)
        {
            registry.Register(new OccupationDefinition(OccupationIds.Craftsperson, new[]
            {
                new ScheduleSlotTemplate("craftsperson_work_morning",   7,  Work,     "WorkLocation",  0.8, true),
                new ScheduleSlotTemplate("craftsperson_work_afternoon", 13, Work,     "WorkLocation",  0.7, true),
                new ScheduleSlotTemplate("craftsperson_social_evening", 19, ReachOut, "HomeLocation",  0.5, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Merchant, new[]
            {
                new ScheduleSlotTemplate("merchant_move_morning",   6,  MoveToPublic, "MarketLocation", 0.9, true),
                new ScheduleSlotTemplate("merchant_work_day",       8,  Work,         "MarketLocation", 0.8, true),
                new ScheduleSlotTemplate("merchant_social_evening", 17, ReachOut,     "TavernLocation", 0.6, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Scholar, new[]
            {
                new ScheduleSlotTemplate("scholar_work_morning",     8,  Work,     "LibraryLocation", 0.8, true),
                new ScheduleSlotTemplate("scholar_create_afternoon", 14, Create,   "LibraryLocation", 0.7, true),
                new ScheduleSlotTemplate("scholar_care_evening",     20, SelfCare, "HomeLocation",    0.5, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Farmer, new[]
            {
                new ScheduleSlotTemplate("farmer_work_morning",   5,  Work, "FieldLocation", 0.9, true),
                new ScheduleSlotTemplate("farmer_eat_midday",     12, Eat,  "HomeLocation",  0.8, false),
                new ScheduleSlotTemplate("farmer_work_afternoon", 14, Work, "FieldLocation", 0.7, true),
                new ScheduleSlotTemplate("farmer_rest_evening",   19, Idle, "HomeLocation",  0.6, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Guard, new[]
            {
                new ScheduleSlotTemplate("guard_work_day",   6,  Work, "GateLocation", 0.9, true),
                new ScheduleSlotTemplate("guard_work_night", 18, Work, "GateLocation", 0.9, true)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Healer, new[]
            {
                new ScheduleSlotTemplate("healer_work_day",      8,  Work,     "ClinicLocation", 0.8, true),
                new ScheduleSlotTemplate("healer_care_evening",  19, SelfCare, "HomeLocation",   0.7, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Artist, new[]
            {
                new ScheduleSlotTemplate("artist_create_morning",  9,  Create,  null,             0.8, true),
                new ScheduleSlotTemplate("artist_social_evening",  19, ReachOut, "TavernLocation", 0.7, false)
            }));

            registry.Register(new OccupationDefinition(OccupationIds.Laborer, new[]
            {
                new ScheduleSlotTemplate("laborer_work_morning", 6,  Work, null, 0.9, true),
                new ScheduleSlotTemplate("laborer_eat_midday",   12, Eat,  null, 0.8, false),
                new ScheduleSlotTemplate("laborer_rest_evening", 18, Idle, null, 0.6, false)
            }));
        }
    }
}
