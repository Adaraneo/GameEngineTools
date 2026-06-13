// IMemory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A spatial memory of where a specific world object was last seen.
    /// </summary>
    public sealed record ObjectLocationFact(
        string ObjectId,
        string LocationId,
        WDateTime SeenAt,
        double Confidence,
        PickupItemKind ItemKind);

    /// <summary>
    /// Tuning constants for <see cref="IMemoryEngine"/>: encoding strength, Ebbinghaus
    /// forgetting, sleep consolidation, reconsolidation drift, the System-1/2 cognitive
    /// burden threshold, and knowledge/object-fact confidence decay. Bound from <c>Characters:Memory</c>.
    /// </summary>
    public sealed record MemoryConfig(
        double BaseEncoding = 0.5,
        double SleepConsolidationBoost = 0.12,
        double ForgettingRate = 0.06,
        double PruneThreshold = 0.01,
        double ReinforcementBoost = 0.15,
        double EmotionDecayMod = 0.5,
        double StressDistortionWeight = 0.35,
        double ReconsolidationDriftRate = 0.04,
        double CognitiveBurdenThreshold = 0.65,
        /// <summary>
        /// Initial confidence assigned to a directly witnessed fact.
        /// </summary>
        double DirectWitnessConfidence = 0.90,
        /// <summary>
        /// Initial confidence assigned to a fact learned via gossip (ThirdPartyActionObserved).
        /// Gossip is discounted ~30-50% vs. direct evidence (Feinberg et al. 2014).
        /// </summary>
        double GossipConfidence = 0.35,
        /// <summary>
        /// Confidence decay per day for knowledge facts. Much slower than episodic decay.
        /// Default 0.005 → a witnessed fact at 0.9 takes ~180 days to drop to 0.0.
        /// </summary>
        double KnowledgeConfidenceDecayPerDay = 0.005,
        /// <summary>
        /// Facts below this confidence are pruned (forgotten).
        /// </summary>
        double KnowledgePruneThreshold = 0.05,
        /// <summary>
        /// Confidence decay per day for object location facts.
        /// Default 0.05 → a directly-seen object's confidence drops from 0.85 to 0.0 in ~17 days.
        /// </summary>
        double ObjectLocationDecayPerDay = 0.05,
        /// <summary>
        /// Small additive weight for valence-congruent episodic recall. A non-depressed character
        /// recalls mood-congruent (positive) episodes slightly more (healthy positivity bias, d≈0.15);
        /// the bias reverses to negative-congruent recall once mood drops below
        /// <see cref="DepressionNegativeBiasThreshold"/>. Kept small so salience/recency dominate.
        /// Episodic/self-referential only — not applied to semantic memory.
        /// Source: Matt, Vázquez &amp; Campbell 1992; Faul &amp; LaBar 2023.
        /// </summary>
        double MoodCongruenceWeight = 0.04,
        /// <summary>
        /// Valence threshold below which the normative positivity bias reverses toward negative-congruent
        /// recall (the robust clinical depression finding). Source: Matt, Vázquez &amp; Campbell 1992;
        /// Faul &amp; LaBar 2023. Default −0.4.
        /// </summary>
        double DepressionNegativeBiasThreshold = -0.4)
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public MemoryConfig() : this(0.5, 0.12, 0.06, 0.01, 0.15, 0.5, 0.35, 0.04, 0.65, 0.90, 0.35, 0.005, 0.05, 0.05, 0.04, -0.4) { }
    }

    /// <summary>
    /// The character's memory store: episodic memories plus structured knowledge facts and
    /// spatial object-location facts. Persisted in the engines snapshot.
    /// </summary>
    /// <param name="Episodes">Episodic memories.</param>
    public sealed record MemoryIndex(IReadOnlyList<EpisodicMemory> Episodes)
    {
        /// <summary>
        /// Structured facts this character knows about other characters' actions.
        /// Populated from witnessed events and gossip (ThirdPartyActionObserved).
        /// Confidence decays over time; forgotten when confidence drops below threshold.
        /// </summary>
        public IReadOnlyList<KnowledgeFact> Knowledge { get; init; }
            = Array.Empty<KnowledgeFact>();

        /// <summary>
        /// Spatial memories of where world objects were last seen.
        /// Populated passively during observation and actively via object interaction events.
        /// Confidence decays over time; pruned when below 0.01.
        /// </summary>
        public IReadOnlyList<ObjectLocationFact> KnownObjects { get; init; }
            = Array.Empty<ObjectLocationFact>();
    }

    /// <summary>
    /// A single episodic memory: what happened, when, its salience and emotional tag, the
    /// current Ebbinghaus strength, plus distortion/peak-end fields used for recall and reconsolidation.
    /// </summary>
    public sealed record EpisodicMemory(
        Guid Id, WDateTime When, string What, double Salience, EmotionalTag Emotion, double Strength,
        string? PerceivedWhat = null, double RecallConfidence = 1.0, double Distortion = 0.0,
        HumanId? OtherPerson = null, PersonBeliefEvidence? BeliefEvidence = null,
        EmotionalTag? PeakEmotion = null,
        EmotionalTag? EndEmotion = null);

    /// <summary>Emotional valence tag attached to a memory.</summary>
    public enum EmotionalTag
    {
        /// <summary>Emotionally neutral.</summary>
        Neutral,

        /// <summary>Positive valence.</summary>
        Positive,

        /// <summary>Negative valence.</summary>
        Negative,

        /// <summary>Mixed positive and negative valence.</summary>
        Mixed
    }

    /// <summary>
    /// How a <see cref="KnowledgeFact"/> was acquired.
    /// Direct witnessing carries full confidence; gossip is discounted.
    /// </summary>
    public enum FactSource
    {
        /// <summary>NPC was directly present when the event occurred.</summary>
        DirectWitness,

        /// <summary>NPC learned about the event via ThirdPartyActionObserved (gossip).</summary>
        Gossip
    }

    /// <summary>
    /// A structured fact this character knows about another character's action.
    /// Theory of Mind — cognitive ToM: explicit tracking of who knows what
    /// (Baker, Jara-Ettinger, Saxe &amp; Tenenbaum 2017, Nature Human Behaviour).
    /// </summary>
    /// <param name="Id">Unique identifier.</param>
    /// <param name="LearnedAt">When this fact was acquired.</param>
    /// <param name="Subject">The character who performed the action.</param>
    /// <param name="Object">The character the action was directed at, if applicable.</param>
    /// <param name="ActionKind">Type of action (e.g. nameof(SexualEncounterOutcome), "Betrayal").</param>
    /// <param name="Source">Whether acquired directly or via gossip.</param>
    /// <param name="Confidence">
    /// Current confidence level [0–1].
    /// Starts at DirectWitnessConfidence (≈0.9) or GossipConfidence (≈0.35) and decays.
    /// </param>
    public sealed record KnowledgeFact(
        Guid Id,
        WDateTime LearnedAt,
        HumanId Subject,
        HumanId? Object,
        string ActionKind,
        FactSource Source,
        double Confidence,

        /// <summary>
        /// Level-2 Theory of Mind: <c>true</c> when this character knows that
        /// <see cref="KnownSharedWith"/> also holds this fact (common ground).
        /// Backward-compatible append (defaults to first-order knowledge only).
        /// </summary>
        bool IsMutuallyKnown = false,

        /// <summary>The other party with whom this fact is mutually known, when applicable.</summary>
        HumanId? KnownSharedWith = null);

    /// <summary>
    /// The memory engine — stores and recalls episodic memories with Ebbinghaus decay,
    /// spacing, peak-end salience and reconsolidation, and builds the decision working set.
    /// </summary>
    public interface IMemoryEngine : IEngine<MemoryIndex, MemoryConfig>
    {
        /// <summary>Encodes a new episodic memory (applying distortion and reinforcement).</summary>
        /// <param name="episode">The episode to encode.</param>
        /// <param name="ctx">Character context.</param>
        /// <param name="outbox">Collector for emitted events.</param>
        void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox);

        /// <summary>Recalls all episodes matching a predicate.</summary>
        /// <param name="predicate">Filter applied to each episode.</param>
        IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate);

        /// <summary>Recalls episodes for a structured query, applying decay as of <paramref name="now"/>.</summary>
        /// <param name="query">The recall query.</param>
        /// <param name="now">Current game time.</param>
        MemoryRecallResult Recall(MemoryRecallQuery query, WDateTime now);

        /// <summary>Builds the working set of recalled memories and reflections for a decision.</summary>
        /// <param name="query">The recall query.</param>
        /// <param name="now">Current game time.</param>
        DecisionWorkingSet BuildWorkingSet(MemoryRecallQuery query, WDateTime now);

        /// <summary>
        /// Builds the decision working set, using <paramref name="ctx"/> to apply System-1/2
        /// switching based on cognitive burden.
        /// </summary>
        /// <param name="query">The recall query.</param>
        /// <param name="now">Current game time.</param>
        /// <param name="ctx">Character context (for cognitive burden).</param>
        DecisionWorkingSet BuildWorkingSet(MemoryRecallQuery query, WDateTime now, IHumanContext ctx);

        /// <summary>
        /// Returns whether this character has knowledge about a specific action by the given subject,
        /// optionally directed at a specific object. Confidence must exceed the configured threshold.
        /// </summary>
        bool KnowsAbout(HumanId subject, string actionKind, HumanId? objectId = null);

        /// <summary>
        /// Returns the highest confidence [0–1] this character has about a specific action.
        /// Returns 0 if no relevant knowledge exists.
        /// </summary>
        double ConfidenceAbout(HumanId subject, string actionKind, HumanId? objectId = null);
    }

    // Events

    /// <summary>Event — a new episodic memory was encoded.</summary>
    public sealed record MemoryEncoded(
        WDateTime OccurredAt,
        HumanId Human,
        Guid EpisodeId,
        double Strength,
        string? What = null,
        string? PerceivedWhat = null,
        HumanId? OtherPerson = null,
        PersonBeliefEvidence? BeliefEvidence = null) : IDomainEvent;
    /// <summary>Event — a specific episode was recalled.</summary>
    public sealed record MemoryRecalled(WDateTime OccurredAt, HumanId Human, Guid EpisodeId) : IDomainEvent;
    /// <summary>Event — a recall query was evaluated, reporting how many episodes matched.</summary>
    public sealed record MemoryRecallEvaluated(
        WDateTime OccurredAt,
        HumanId Human,
        string? ActionName,
        HumanId? TargetHuman,
        int RecalledCount) : IDomainEvent;
    /// <summary>Event — a semantic reflection summary influenced a decision.</summary>
    public sealed record ReflectionApplied(
        WDateTime OccurredAt,
        HumanId Human,
        string? ActionName,
        HumanId? TargetHuman,
        ReflectionSummaryKind Kind,
        double Strength) : IDomainEvent;
    /// <summary>Event — memories were consolidated (typically after sleep).</summary>
    public sealed record MemoryConsolidated(WDateTime OccurredAt, HumanId Human, int Count) : IDomainEvent;
}
