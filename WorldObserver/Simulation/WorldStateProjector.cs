// WorldStateProjector.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Status;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using WorldObserver.Dtos;

    /// <summary>
    /// Pure mapping from the live character snapshots to the flat <see cref="WorldStateDto"/>.
    /// No state of its own — safe to call from the simulation thread each tick.
    /// </summary>
    public static class WorldStateProjector
    {
        /// <summary>Edges weaker than this on both Like and Closeness are omitted to keep the graph readable.</summary>
        private const double EdgeThreshold = 1.0;

        /// <summary>Builds the full world snapshot for one push.</summary>
        public static WorldStateDto Project(
            WDateTime now,
            IReadOnlyList<IHuman> characters,
            ILocationService locationService,
            SimulationControl control,
            IReadOnlyDictionary<HumanId, List<string>> trails,
            System.Func<HumanId, string?>? travelDestinationOf = null,
            WDateTime startTime = default,
            string realElapsed = "",
            IReadOnlyList<string>? mapLocationIds = null,
            IReadOnlyList<(string From, string To, double Dist)>? mapConnections = null,
            System.Func<HumanId, (string Origin, string Destination, double Progress)?>? transitOf = null,
            IReadOnlyDictionary<string, string>? regions = null,
            IWorldObjectProvider? objectProvider = null,
            StatusLedger? statusLedger = null)
        {
            var idSet = characters.Select(c => c.Id).ToHashSet();
            var nameById = characters.ToDictionary(c => c.Id, c => c.Identity.FirstName.Original);
            // Hierarchy stability is scene-global; read once and use for all character status DTOs.
            var hierarchyStability = statusLedger?.HierarchyStability() ?? 1.0;

            var characterDtos = characters.Select(c =>
            {
                var snap = c.Snapshot;
                var psy = snap.Psychology;
                var phy = snap.Physiology;
                var mot = psy.Motivations;
                var beh = snap.Behavior;
                var self = snap.SelfConcept;
                var vals = snap.Values?.Current;
                var ints = snap.Interests?.Current;

                // Travel destination (display name), or null when the character is not in transit.
                var travelingTo = travelDestinationOf?.Invoke(c.Id);
                var transit = transitOf?.Invoke(c.Id);

                // Most recent committed action this tick — covers movement (MoveTo:*) and everything else.
                string? currentAction = null;
                for (var i = c.LastOutbox.Count - 1; i >= 0; i--)
                {
                    if (c.LastOutbox[i] is ActionCommitted ac) { currentAction = ac.ActionName; break; }
                }

                // Cause of death — read from the (persisted) death event once the character is dead.
                string? deathCause = null;
                if (phy.Status == StatusType.Dead)
                {
                    foreach (var ev in c.LastOutbox)
                    {
                        if (ev is CharacterDied died) { deathCause = died.Cause.ToString(); break; }
                    }
                }

                // Chosen interaction this tick — the latest one this character initiated.
                InteractionDto? interaction = null;
                for (var i = c.LastOutbox.Count - 1; i >= 0; i--)
                {
                    if (c.LastOutbox[i] is InteractionProposed ip && ip.From == c.Id)
                    {
                        interaction = new InteractionDto(ip.Act.ToString(), ip.To.Value.ToString(), ip.Content);
                        break;
                    }
                }

                // Biological cycles.
                var cyc = phy.Cycle;
                var nut = phy.Nutrition;
                var bio = new BioDto(
                    Testosterone: phy.Testosterone is { } t ? Round(t.Level) : null,
                    Cycle: cyc is null ? null : new CycleDto(
                        Phase: cyc.Phase.ToString(),
                        DayInCycle: cyc.DayInCycle,
                        OvulationWindow: cyc.OvulationWindow,
                        SymptomPain: Round(cyc.SymptomPain),
                        SymptomBreastTender: Round(cyc.SymptomBreastTender),
                        SymptomBloat: Round(cyc.SymptomBloat),
                        LibidoMod: Math.Round(cyc.LibidoMod, 2),
                        PmddActive: cyc.PmddActive,
                        CurrentCycleLength: cyc.CurrentCycleLength,
                        Estradiol: Round(cyc.Estradiol),
                        Progesterone: Round(cyc.Progesterone)),
                    Nutrition: nut is null ? null : new NutritionDto(
                        Calories: Round(nut.Calories),
                        VitaminD: Round(nut.VitaminD),
                        Iron: Round(nut.Iron),
                        Protein: Round(nut.Protein),
                        BloodGlucose: Round(nut.BloodGlucoseLevel),
                        PostMealHours: Math.Round(nut.PostMealHours, 1)));

                // Bereavement — map each active LossRecord to a flat LossDto.
                var bereavementState = snap.Bereavement;
                IReadOnlyList<LossDto>? losses = null;
                if (bereavementState is { Losses.Count: > 0 })
                {
                    losses = bereavementState.Losses
                        .Select(l => new LossDto(
                            DeceasedId: l.DeceasedId.Value.ToString(),
                            DeceasedName: nameById.TryGetValue(l.DeceasedId, out var dn) ? dn : l.DeceasedId.Value.ToString()[..8],
                            KinRole: l.KinRole.ToString(),
                            GriefIntensity: Round(l.GriefIntensity),
                            Trajectory: l.Trajectory.ToString(),
                            Bond: l.Bond.ToString(),
                            Buried: l.Buried))
                        .ToList();
                }

                // Social status — read from the ledger if available, fall back to snapshot.
                StatusDto? socialStatus = null;
                var snapStatus = statusLedger?.Get(c.Id) ?? snap.SocietalStatus;
                if (snapStatus is { } ss)
                {
                    socialStatus = new StatusDto(
                        Dominance: Round(ss.DominanceStatus),
                        Prestige: Round(ss.PrestigeStatus),
                        HierarchyStability: Math.Round(hierarchyStability, 3));
                }

                var preg = phy.Pregnancy;
                var pp = phy.Postpartum;
                var daysPreg = preg is null ? 0 : (int)preg.ConceivedOn.DaysUntil(now.Date);
                var reproduction = new ReproductionDto(
                    Pregnant: preg is not null,
                    DaysPregnant: daysPreg,
                    DueInDays: preg is null ? 0 : (int)now.Date.DaysUntil(preg.EstimatedDueDate),
                    Trimester: preg is null ? 0 : (daysPreg < 93 ? 1 : daysPreg < 186 ? 2 : 3),
                    Discovered: preg?.Discovered ?? false,
                    OtherParent: preg is null
                        ? null
                        : (nameById.TryGetValue(preg.OtherParent, out var pn) ? pn : preg.OtherParent.Value.ToString()[..8]),
                    Postpartum: pp is not null,
                    PostpartumPhase: pp?.Phase.ToString(),
                    PostpartumDays: pp?.DaysSinceBirth ?? 0,
                    Contraception: phy.CurrentContraception.ToString(),
                    FertileWindow: cyc?.OvulationWindow ?? false);

                return new CharacterDto(
                    Id: c.Id.Value.ToString(),
                    Name: c.Identity.FirstName.Original,
                    Surname: (c.Biology == SexBiology.Female
                        ? c.Identity.LastName.Female
                        : c.Identity.LastName.Male) ?? string.Empty,
                    Age: c.Age,
                    Sex: c.Biology.ToString(),
                    Orientation: c.AttractionProfile?.Orientation.ToString() ?? "Unknown",
                    Location: !string.IsNullOrEmpty(snap.InteractionSurface.Location)
                        ? snap.InteractionSurface.Location
                        : (travelingTo is not null ? "na cestě" : "Unknown"),
                    Emotion: psy.DominantEmotion.ToString(),
                    Status: phy.Status.ToString(),
                    Occupation: snap.Schedule?.Occupation,
                    Valence: Round(psy.Valence),
                    Arousal: Round(psy.Arousal),
                    Dominance: Round(psy.Dominance),
                    Stress: Round(psy.Stress),
                    MoodBaseline: Round(psy.MoodBaseline),
                    Energy: Round(phy.Energy),
                    Hunger: Round(phy.Hunger),
                    Thirst: Round(phy.Thirst),
                    CurrentAction: currentAction,
                    TravelingTo: travelingTo,
                    TravelFromId: transit?.Origin,
                    TravelToId: transit?.Destination,
                    TravelProgress: transit?.Progress,
                    Pain: Round(phy.Pain),
                    SleepDebtHours: Round(phy.SleepDebtHours),
                    ImmuneLoad: Round(phy.ImmuneLoad),
                    Cortisol: Round(phy.CortisolLevel),
                    AllostaticLoad: Round(phy.AllostaticLoad),
                    PhysicalFatigue: Round(phy.PhysicalFatigueLevel),
                    BodyTempDelta: Math.Round(phy.BodyTempDelta, 2),
                    CognitiveLoad: Round(psy.CognitiveLoad),
                    NeedSocial: Round(mot?.NeedSocial ?? 50),
                    NeedIntimacy: Round(mot?.NeedIntimacy ?? 50),
                    NeedAchievement: Round(mot?.NeedAchievement ?? 50),
                    NeedCare: Round(mot?.NeedCare ?? 50),
                    NeedSafety: Round(mot?.NeedSafety ?? 50),
                    SicknessWithdraw: mot?.SicknessWithdraw ?? false,
                    HomeLocation: c.Identity.HomeLocationId is { } homeId
                        ? (locationService.GetDescriptor(homeId)?.DisplayName ?? homeId)
                        : null,
                    Personality: new PersonalityDto(
                        Openness: Math.Round(c.Personality.BigFive.Openness, 2),
                        Conscientiousness: Math.Round(c.Personality.BigFive.Conscientiousness, 2),
                        Extraversion: Math.Round(c.Personality.BigFive.Extraversion, 2),
                        Agreeableness: Math.Round(c.Personality.BigFive.Agreeableness, 2),
                        Neuroticism: Math.Round(c.Personality.BigFive.Neuroticism, 2)),
                    // Copy the trail — the live list keeps mutating while SendAsync serializes.
                    Trail: trails.TryGetValue(c.Id, out var trail) ? trail.ToArray() : System.Array.Empty<string>(),
                    Physio: new PhysioDto(
                        AcuteArousal: Round(phy.AcuteArousalLevel),
                        RecoveryDebtHours: Round(phy.RecoveryDebtHours),
                        SleepInertiaHours: Math.Round(phy.SleepInertiaHours, 2),
                        ChronicPainDays: Round(phy.ChronicPainDays),
                        CircadianPhaseShiftHours: Math.Round(phy.CircadianPhaseShiftHours, 2),
                        ProcessS: Math.Round(phy.ProcessS ?? 0, 3)),
                    Drives: new DriveDto(
                        Rest: Round(beh.NeedRest),
                        Food: Round(beh.NeedFood),
                        Water: Round(beh.NeedWater),
                        Belonging: Round(beh.NeedBelonging),
                        Competence: Round(beh.NeedCompetence),
                        Intimacy: Round(beh.NeedIntimacy)),
                    Self: self is null ? null : new SelfDto(
                        SelfEsteem: Math.Round(self.SelfEsteem, 3),
                        SelfDiscrepancy: Math.Round(self.SelfDiscrepancy, 3),
                        PerceivedOpenness: Math.Round(self.PerceivedOpenness, 3),
                        PerceivedConscientiousness: Math.Round(self.PerceivedConscientiousness, 3),
                        PerceivedExtraversion: Math.Round(self.PerceivedExtraversion, 3),
                        PerceivedAgreeableness: Math.Round(self.PerceivedAgreeableness, 3),
                        PerceivedNeuroticism: Math.Round(self.PerceivedNeuroticism, 3)),
                    Values: vals is null ? null : new ValuesDto(
                        Benevolence: Math.Round(vals.Benevolence, 3),
                        Universalism: Math.Round(vals.Universalism, 3),
                        SelfDirection: Math.Round(vals.SelfDirection, 3),
                        Stimulation: Math.Round(vals.Stimulation, 3),
                        Hedonism: Math.Round(vals.Hedonism, 3),
                        Achievement: Math.Round(vals.Achievement, 3),
                        Power: Math.Round(vals.Power, 3),
                        Security: Math.Round(vals.Security, 3),
                        Conformity: Math.Round(vals.Conformity, 3),
                        Tradition: Math.Round(vals.Tradition, 3)),
                    Interests: ints is null ? null : new InterestsDto(
                        Realistic: Math.Round(ints.Realistic, 3),
                        Investigative: Math.Round(ints.Investigative, 3),
                        Artistic: Math.Round(ints.Artistic, 3),
                        Social: Math.Round(ints.Social, 3),
                        Enterprising: Math.Round(ints.Enterprising, 3),
                        Conventional: Math.Round(ints.Conventional, 3)),
                    DeathCause: deathCause,
                    Interaction: interaction,
                    Bio: bio,
                    Reproduction: reproduction,
                    Losses: losses,
                    SocialStatus: socialStatus);
            }).ToList();

            // Dynamic map: list exactly the locations characters currently occupy (grouped from their
            // own snapshot location, so the panel always matches where people actually are).
            var locationDtos = characters
                .Select(c => (Char: c, Loc: c.Snapshot.InteractionSurface.Location))
                .Where(x => !string.IsNullOrEmpty(x.Loc) && x.Loc != "Unknown")
                .GroupBy(x => x.Loc!)
                .Select(g => new LocationDto(
                    g.Key,
                    locationService.GetDescriptor(g.Key)?.DisplayName ?? g.Key,
                    g.Select(x => x.Char.Id.Value.ToString()).ToList()))
                .OrderBy(l => l.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            var edges = new List<EdgeDto>();
            foreach (var c in characters)
            {
                foreach (var (targetId, edge) in c.Snapshot.Relationships.Edges)
                {
                    if (!idSet.Contains(targetId))
                        continue;
                    if (edge.Like < EdgeThreshold && edge.Closeness < EdgeThreshold)
                        continue;

                    edges.Add(new EdgeDto(
                        From: c.Id.Value.ToString(),
                        To: targetId.Value.ToString(),
                        Like: Round(edge.Like),
                        Trust: Round(edge.Trust),
                        Closeness: Round(edge.Closeness),
                        Respect: Round(edge.Respect),
                        Comfort: Round(edge.Comfort),
                        Familiarity: Round(edge.Familiarity),
                        CommunalStrength: Round(edge.CommunalStrength),
                        IntimateAffinity: Round(edge.IntimateAffinity),
                        PositiveInteractions: edge.PositiveInteractionCount,
                        ExchangeStrength: Round(edge.ExchangeStrength),
                        AestheticAttraction: Round(edge.AestheticAttraction),
                        PhysicalAttraction: Round(edge.PhysicalAttraction),
                        SexualInterest: Round(edge.SexualInterest),
                        ResponsiveDesire: Round(edge.ResponsiveDesireLevel),
                        Commitment: Round(edge.Commitment),
                        InvestmentSize: Round(edge.InvestmentSize),
                        AlternativeQuality: Round(edge.AlternativeQuality),
                        TransgressionResidue: Round(edge.TransgressionResidue),
                        PerceivedDominance: Round(edge.PerceivedDominance),
                        PerceivedPrestige: Round(edge.PerceivedPrestige),
                        KinRole: edge.KinRole.ToString(),
                        ContemptuouslyDestroyed: edge.IsContemptuouslyDestroyed,
                        DissolutionConsidered: edge.DissolutionConsidered));
                }
            }

            // Collect grave and corpse markers from the world objects for rendering on the map.
            var graveMarkers = new List<GraveMarkerDto>();
            if (objectProvider is not null)
            {
                foreach (var obj in objectProvider.GetAllObjects())
                {
                    if (obj.Category != WorldObjectCategory.Grave && obj.Category != WorldObjectCategory.Corpse)
                        continue;
                    if (!BurialObjects.TryGetDeceased(obj, out var deceasedId))
                        continue;
                    var deceasedName = nameById.TryGetValue(deceasedId, out var dn) ? dn : deceasedId.Value.ToString()[..8];
                    graveMarkers.Add(new GraveMarkerDto(
                        ObjectId: obj.Id,
                        LocationId: obj.LocationId ?? "",
                        DeceasedId: deceasedId.Value.ToString(),
                        DeceasedName: deceasedName,
                        IsGrave: obj.Category == WorldObjectCategory.Grave));
                }
            }

            return new WorldStateDto(
                Time: now.ToString(),
                Characters: characterDtos,
                Locations: locationDtos,
                Edges: edges,
                Paused: control.Paused,
                DelayMs: control.DelayMs,
                TickMinutes: control.SimMinutesPerTick,
                Elapsed: FormatElapsed(now - startTime),
                RealElapsed: realElapsed,
                MapLocations: (mapLocationIds ?? System.Array.Empty<string>())
                    .Select(id => new MapLocationDto(
                        id,
                        locationService.GetDescriptor(id)?.DisplayName ?? id,
                        regions is not null && regions.TryGetValue(id, out var r) ? r : ""))
                    .ToList(),
                MapConnections: (mapConnections ?? System.Array.Empty<(string, string, double)>())
                    .Select(c => new MapConnectionDto(c.From, c.To, c.Dist))
                    .ToList(),
                Graves: graveMarkers);
        }

        /// <summary>Formats a game-time span since launch as "Xd Yh Zm" using the world calendar's units.</summary>
        private static string FormatElapsed(WTimeSpan ts)
        {
            var spec = GameEngineTools.World.Core.Time.WWorld.Spec;
            var ticks = ts.Ticks;
            if (ticks < 0) ticks = 0;

            var days = ticks / spec.TicksPerDay; ticks %= spec.TicksPerDay;
            var hours = ticks / spec.TicksPerHour; ticks %= spec.TicksPerHour;
            var minutes = ticks / spec.TicksPerMinute;

            return $"{days} d {hours} h {minutes} min";
        }

        private static double Round(double value) => Math.Round(value, 1);
    }
}
