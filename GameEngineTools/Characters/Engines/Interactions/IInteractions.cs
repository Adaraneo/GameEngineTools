// IInteractions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Konfigurace <see cref="IInteractionEngine"/>.
    /// </summary>
    /// <param name="MisattributionRateBase">
    /// Základní pravděpodobnost špatné interpretace záměru.
    /// Škáluje se stresem postavy: čím větší stres, tím více chybné čtení záměrů druhých.
    /// Výchozí: 0.15.
    /// </param>
    public sealed record InteractionConfig(
        double MisattributionRateBase = 0.15,
        /// <summary>
        /// How much ambient noise amplifies misattribution of social intent.
        /// At noise=1.0, the misattribution rate is multiplied by (1 + NoiseAttributionAmplifier).
        /// Default 0.4 → up to 40% extra misattribution at maximum noise.
        /// </summary>
        double NoiseAttributionAmplifier = 0.40)
    {
        /// <summary>Bezparametrický konstruktor vyžadovaný Options patternem.</summary>
        public InteractionConfig() : this(0.15, 0.40) { }
    }

    /// <summary>Událost — postava vstoupila do nového kontextu (lokace, soukromí, hluk, dav).</summary>
    public sealed record ContextChanged(
        WDateTime OccurredAt,
        HumanId Human,
        string Location,
        bool HasPrivacy,
        double Noise,
        double Crowding,
        SurfaceKind Kind) : IDomainEvent;

    /// <summary>
    /// Popis aktuálního prostředí interakce — co je "po ruce".
    /// Ovlivňuje pravděpodobnost přijetí interakce.
    /// </summary>
    public sealed record InteractionSurface(
        string? Location,
        bool HasPrivacy,
        double Noise,
        double Crowding,
        SurfaceKind Kind,
        /// <summary>
        /// Characters present at this location who can witness interactions.
        /// When non-null and non-empty, the RelationshipsEngine emits
        /// <see cref="GameEngineTools.Characters.Engines.Relationships.ThirdPartyActionObserved"/>
        /// events for each observer after processing MicroPositive / MicroNegative.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<HumanId>? Observers = null,

        /// <summary>
        /// Distance to the nearest person at this location, in metres.
        /// Used for Altman (1975) proxemics zone calculation.
        /// <c>null</c> = distance not measured / not relevant.
        /// </summary>
        double? ProxemicDistanceMeters = null);

    public enum SurfaceKind
    {
        Unknown = 0,
        Social,
        Private,
        Work,
        Rest,
        Public
    }

    /// <summary>Rozhraní pro engine řídící sociální interakce.</summary>
    public interface IInteractionEngine : IEngine<InteractionSurface, InteractionConfig>
    { }

    /// <summary>
    /// Typ řečového aktu — určuje charakter interakce a jaké domény vztahu ovlivní.
    /// </summary>
    public enum SpeechAct
    {
        /// <summary>Nezávazný hovor — buduje Humor.</summary>
        SmallTalk,

        /// <summary>Otázka — projevuje zájem, buduje Intellect doménu.</summary>
        Question,

        /// <summary>Sebeodhalení — sdílení osobního — buduje Values a Closeness.</summary>
        SelfDisclosure,

        /// <summary>Validace — potvrzení a podpora druhého — buduje Values a Comfort.</summary>
        Validation,

        /// <summary>Hranice — nastavení limitu v interakci.</summary>
        Boundary,

        /// <summary>Humor — vtip, odlehčení — silně buduje Humor doménu.</summary>
        Humor,

        /// <summary>Meta — komentář o samotném vztahu nebo interakci — buduje Intellect.</summary>
        Meta,

        /// <summary>Pozvání — sociální iniciativa — jemně buduje Physical doménu.</summary>
        Invite
    }

    /// <summary>Úroveň fyzického kontaktu při <see cref="TouchAttempted"/>.</summary>
    public enum TouchLevel
    {
        /// <summary>Žádný dotyk.</summary>
        None,

        /// <summary>Lehký dotyk (rameno, paže) — buduje Physical doménu.</summary>
        Light,

        /// <summary>Přátelský dotyk (obejmutí) — silněji buduje Physical a Comfort.</summary>
        Friendly,

        /// <summary>Intimní dotyk — vyžaduje vysokou Closeness a Attraction.</summary>
        Intimate
    }

    /// <summary>Událost — postava A navrhuje interakci postavě B.</summary>
    public sealed record InteractionProposed(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        SpeechAct Act,
        string? Content,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent;

    /// <summary>Událost — postava A se pokouší o fyzický kontakt s postavou B.</summary>
    public sealed record TouchAttempted(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        TouchLevel Level) : IDomainEvent;

    /// <summary>
    /// Událost — interakce byla vyhodnocena (přijata nebo odmítnuta).
    /// </summary>
    /// <param name="Act">
    /// Typ řečového aktu z původního <see cref="InteractionProposed"/>.
    /// Přenášíme ho sem, aby <c>RelationshipsEngine</c> věděl, jakou doménu aktualizovat
    /// bez nutnosti korelovat s původní událostí.
    /// </param>
    public sealed record InteractionOutcome(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        bool Accepted,
        string Reason,
        SpeechAct Act = SpeechAct.SmallTalk,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null,
        double? PeakValence = null,
        double? EndValence = null) : IDomainEvent;

    /// <summary>Záměr vůči případnému těhotenství u abstraktního sexuálního setkání.</summary>
    public enum ReproductiveIntent
    {
        AvoidPregnancy = 0,
        Indifferent = 1,
        OpenToPregnancy = 2,
        TryingForChild = 3
    }

    /// <summary>Hrubá úroveň antikoncepční ochrany pro reprodukční výpočet.</summary>
    public enum ContraceptionLevel
    {
        Unspecified = 0,
        None = 1,
        Low = 2,
        Moderate = 3,
        High = 4
    }

    /// <summary>Událost — intimní setkání bylo navrženo po přijaté vztahové iniciativě.</summary>
    public sealed record SexualEncounterProposed(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        ReproductiveIntent Intent = ReproductiveIntent.Indifferent,
        ContraceptionLevel Contraception = ContraceptionLevel.Unspecified,
        bool ReproductivePotential = false,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent;

    /// <summary>Událost — abstraktní intimní setkání bylo přijato nebo odmítnuto.</summary>
    public sealed record SexualEncounterOutcome(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        bool Accepted,
        string Reason,
        ReproductiveIntent Intent = ReproductiveIntent.Indifferent,
        ContraceptionLevel Contraception = ContraceptionLevel.Unspecified,
        bool ReproductivePotential = false,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent;
}
