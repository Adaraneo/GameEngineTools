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
    public sealed record InteractionConfig(double MisattributionRateBase = 0.15)
    {
        /// <summary>Bezparametrický konstruktor vyžadovaný Options patternem.</summary>
        public InteractionConfig() : this(0.15) { }
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
        string Location,
        bool HasPrivacy,
        double Noise,
        double Crowding,
        SurfaceKind Kind);

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
        string? Content) : IDomainEvent;

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
        SpeechAct Act = SpeechAct.SmallTalk) : IDomainEvent;
}
