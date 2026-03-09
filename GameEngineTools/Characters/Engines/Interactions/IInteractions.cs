// IInteractions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record InteractionConfig(double MisattributionRateBase = 0.15)
    {
        public InteractionConfig() : this(0.15) { }
    }

    public sealed record ContextChanged(
        WDateTime OccurredAt,
        HumanId Human,
        string Location,
        bool HasPrivacy,
        double Noise,
        double Crowding) : IDomainEvent;

    public sealed record InteractionSurface( // co je „po ruce“
        string Location, bool HasPrivacy, double Noise, double Crowding);

    public interface IInteractionEngine : IEngine<InteractionSurface, InteractionConfig>
    { }

    public enum SpeechAct
    { SmallTalk, Question, SelfDisclosure, Validation, Boundary, Humor, Meta, Invite }

    public enum TouchLevel
    { None, Light, Friendly, Intimate }

    public sealed record InteractionProposed(WDateTime OccurredAt, HumanId From, HumanId To, SpeechAct Act, string? Content) : IDomainEvent;
    public sealed record TouchAttempted(WDateTime OccurredAt, HumanId From, HumanId To, TouchLevel Level) : IDomainEvent;
    public sealed record InteractionOutcome(WDateTime OccurredAt, HumanId From, HumanId To, bool Accepted, string Reason) : IDomainEvent;
}
