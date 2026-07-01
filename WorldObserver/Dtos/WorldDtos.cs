// WorldDtos.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Dtos
{
    /// <summary>
    /// Lightweight projection of the whole observable world for one push to the browser.
    /// Deliberately flat and primitive-only so it serializes cheaply over SignalR — we never
    /// send raw engine records (<c>EnginesSnapshot</c> etc.).
    /// </summary>
    public sealed record WorldStateDto(
        string Time,
        IReadOnlyList<CharacterDto> Characters,
        IReadOnlyList<LocationDto> Locations,
        IReadOnlyList<EdgeDto> Edges,
        bool Paused,
        int DelayMs,
        int TickMinutes,
        string Elapsed,
        string RealElapsed,
        IReadOnlyList<MapLocationDto> MapLocations,
        IReadOnlyList<MapConnectionDto> MapConnections,
        IReadOnlyList<GraveMarkerDto> Graves);

    /// <summary>A grave or corpse world object placed in the world (cemetery or death site).</summary>
    public sealed record GraveMarkerDto(
        string ObjectId,
        string LocationId,
        string DeceasedId,
        string DeceasedName,
        bool IsGrave);

    /// <summary>A location on the spatial map (stable across ticks — used to lay out the movement view).</summary>
    public sealed record MapLocationDto(string Id, string DisplayName, string Region);

    /// <summary>An undirected road between two locations, with distance (for the realistic graph layout).</summary>
    public sealed record MapConnectionDto(string From, string To, double Dist);

    /// <summary>
    /// Per-character state projected from <c>IHuman.Snapshot</c>. The first block is the at-a-glance
    /// summary shown on every card; the rest powers the selected-character detail view.
    /// </summary>
    public sealed record CharacterDto(
        string Id,
        string Name,
        string Surname,
        int Age,
        string Sex,
        string Orientation,
        string Location,
        string Emotion,
        string Status,
        string? Occupation,
        double Valence,
        double Arousal,
        double Dominance,
        double Stress,
        double MoodBaseline,
        double Energy,
        double Hunger,
        double Thirst,
        // ── Movement ──────────────────────────────────────────────────────────
        string? CurrentAction,
        // Display name of the destination the character is currently walking to (null = not travelling).
        string? TravelingTo,
        // In-transit road animation: origin/destination location ids + progress [0..1] (null = not travelling).
        string? TravelFromId,
        string? TravelToId,
        double? TravelProgress,
        // ── Physiology (detail) ───────────────────────────────────────────────
        double Pain,
        double SleepDebtHours,
        double ImmuneLoad,
        double Cortisol,
        double AllostaticLoad,
        double PhysicalFatigue,
        double BodyTempDelta,
        // ── Psychology (detail) ───────────────────────────────────────────────
        double CognitiveLoad,
        double NeedSocial,
        double NeedIntimacy,
        double NeedAchievement,
        double NeedCare,
        double NeedSafety,
        bool SicknessWithdraw,
        // ── Hover detail ──────────────────────────────────────────────────────
        string? HomeLocation,
        PersonalityDto Personality,
        // ── Movement trail (oldest → newest distinct locations) ───────────────
        IReadOnlyList<string> Trail,
        // ── Deeper detail groups (selected-character view) ────────────────────
        PhysioDto Physio,
        DriveDto Drives,
        SelfDto? Self,
        ValuesDto? Values,
        InterestsDto? Interests,
        // ── Cause of death (when Status == Dead) ──────────────────────────────
        string? DeathCause,
        // ── Chosen interaction this tick (when initiating) ────────────────────
        InteractionDto? Interaction,
        // ── Biological cycles ─────────────────────────────────────────────────
        BioDto Bio,
        // ── Reproduction ──────────────────────────────────────────────────────
        ReproductionDto Reproduction,
        // ── Bereavement (active grief losses) ────────────────────────────────
        IReadOnlyList<LossDto>? Losses,
        // ── Emergent social status (dominance + prestige axes, orthogonal) ────
        StatusDto? SocialStatus);

    /// <summary>One active grief loss the character is currently carrying.</summary>
    public sealed record LossDto(
        string DeceasedId,
        string DeceasedName,
        string KinRole,
        double GriefIntensity,
        string Trajectory,
        string Bond,
        bool Buried);

    /// <summary>Emergent social status on both orthogonal axes (dominance and prestige are never collapsed).</summary>
    public sealed record StatusDto(
        double Dominance,
        double Prestige,
        double HierarchyStability);

    /// <summary>Reproductive state — pregnancy, postpartum, contraception, fertile window.</summary>
    public sealed record ReproductionDto(
        bool Pregnant,
        int DaysPregnant,
        int DueInDays,
        int Trimester,
        bool Discovered,
        string? OtherParent,
        bool Postpartum,
        string? PostpartumPhase,
        int PostpartumDays,
        string Contraception,
        bool FertileWindow);

    /// <summary>Big Five (OCEAN) personality, each [0..1].</summary>
    public sealed record PersonalityDto(
        double Openness,
        double Conscientiousness,
        double Extraversion,
        double Agreeableness,
        double Neuroticism);

    /// <summary>Extended physiology readings beyond the card summary.</summary>
    public sealed record PhysioDto(
        double AcuteArousal,
        double RecoveryDebtHours,
        double SleepInertiaHours,
        double ChronicPainDays,
        double CircadianPhaseShiftHours,
        double ProcessS);

    /// <summary>Behavior-engine drive levels [0..100].</summary>
    public sealed record DriveDto(
        double Rest,
        double Food,
        double Water,
        double Belonging,
        double Competence,
        double Intimacy);

    /// <summary>Self-concept: perceived Big Five, self-esteem and self-discrepancy.</summary>
    public sealed record SelfDto(
        double SelfEsteem,
        double SelfDiscrepancy,
        double PerceivedOpenness,
        double PerceivedConscientiousness,
        double PerceivedExtraversion,
        double PerceivedAgreeableness,
        double PerceivedNeuroticism);

    /// <summary>Schwartz basic human values (current, drifting profile).</summary>
    public sealed record ValuesDto(
        double Benevolence,
        double Universalism,
        double SelfDirection,
        double Stimulation,
        double Hedonism,
        double Achievement,
        double Power,
        double Security,
        double Conformity,
        double Tradition);

    /// <summary>RIASEC interest profile (current).</summary>
    public sealed record InterestsDto(
        double Realistic,
        double Investigative,
        double Artistic,
        double Social,
        double Enterprising,
        double Conventional);

    /// <summary>The interaction this character chose to initiate this tick.</summary>
    public sealed record InteractionDto(
        string Act,
        string? TargetId,
        string? Content);

    /// <summary>Biological cycle state — sub-objects are null when not applicable to the character.</summary>
    public sealed record BioDto(
        double? Testosterone,
        CycleDto? Cycle,
        NutritionDto? Nutrition);

    /// <summary>Menstrual-cycle sub-state.</summary>
    public sealed record CycleDto(
        string Phase,
        int DayInCycle,
        bool OvulationWindow,
        double SymptomPain,
        double SymptomBreastTender,
        double SymptomBloat,
        double LibidoMod,
        bool PmddActive,
        int CurrentCycleLength,
        double Estradiol,
        double Progesterone);

    /// <summary>Nutrition sub-state.</summary>
    public sealed record NutritionDto(
        double Calories,
        double VitaminD,
        double Iron,
        double Protein,
        double BloodGlucose,
        double PostMealHours);

    /// <summary>One directed relationship edge (A → B) above the display threshold.</summary>
    public sealed record EdgeDto(
        string From,
        string To,
        double Like,
        double Trust,
        double Closeness,
        double Respect,
        double Comfort,
        double Familiarity,
        double CommunalStrength,
        double IntimateAffinity,
        int PositiveInteractions,
        // ── Extended dimensions ───────────────────────────────────────────────
        double ExchangeStrength,
        double AestheticAttraction,
        double PhysicalAttraction,
        double SexualInterest,
        double ResponsiveDesire,
        double Commitment,
        double InvestmentSize,
        double AlternativeQuality,
        double TransgressionResidue,
        double PerceivedDominance,
        double PerceivedPrestige,
        string KinRole,
        bool ContemptuouslyDestroyed,
        bool DissolutionConsidered);

    /// <summary>One exported/imported character: the file name to use and its full CharacterData JSON.</summary>
    public sealed record CharacterFileDto(string FileName, string Json);

    /// <summary>Export payload: the world clock (portable WorldTicks) plus the characters.</summary>
    public sealed record ExportBundleDto(long WorldTimeTicks, IReadOnlyList<CharacterFileDto> Characters);

    /// <summary>A location and who is currently standing in it.</summary>
    public sealed record LocationDto(
        string Id,
        string DisplayName,
        IReadOnlyList<string> CharacterIds);

    /// <summary>A single narrative line (Czech), pushed as it happens.</summary>
    public sealed record NarrativeDto(
        string Time,
        string Subject,
        string Text,
        string Priority);
}
