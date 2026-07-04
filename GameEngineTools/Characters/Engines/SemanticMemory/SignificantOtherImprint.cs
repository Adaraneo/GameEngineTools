// SignificantOtherImprint.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A compact, persistent snapshot of a past significant relationship, retained independently
    /// of the source person's <see cref="PersonBeliefSet"/> so it survives <c>ForgetPerson</c> or
    /// relationship dissolution. Used to detect resemblance in newly-met people (transference).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ Single-research-group caution:</b> the transference construct rests primarily on
    /// Andersen and colleagues (Andersen &amp; Chen 2002, <i>Psychological Review</i> 109(4),
    /// 619–645; Andersen &amp; Cole 1990, <i>JPSP</i> 59, 384–399; Andersen &amp; Baum 1994,
    /// <i>Journal of Personality</i> 62, 459–497). Independent replication exists but is partial:
    /// Brumbaugh &amp; Fraley (2006, <i>PSPB</i> 32(4), 552–560; 2007, <i>Personal
    /// Relationships</i> 14(4), 513–530) and Leahy &amp; Chopik (2021, <i>Collabra: Psychology</i>
    /// 7(1), 24720) are genuinely independent groups; Kraus &amp; Chen (2010, <i>Psychological
    /// Science</i> 21(4)) and Günaydın, Zayas, Chen &amp; Hazan (2012, <i>Journal of Research in
    /// Personality</i>) include an Andersen-lab collaborator, so are only partially independent.
    /// Keep this subsystem's effect magnitudes conservative accordingly.
    /// </para>
    /// <para>
    /// <b>Complementary to, not redundant with, <see cref="AttachmentProfile"/>:</b> AttachmentProfile
    /// is a general, trait-like relational style applied to <i>every</i> new person.
    /// SignificantOtherImprint drives an <i>additional</i>, resemblance-gated perturbation that
    /// only activates for people who specifically resemble a past significant other. The two must
    /// never be merged into one number — see <see cref="TransferenceMath"/> for how the
    /// perturbation is applied strictly on top of (never instead of) the normal belief-seeding path.
    /// </para>
    /// </remarks>
    /// <param name="SourcePersonId">The (possibly now-forgotten) person this imprint was captured from.</param>
    /// <param name="CapturedAt">When the imprint was captured.</param>
    /// <param name="FaceSummary">Compact facial-resemblance descriptor — reuses <see cref="FacialMorphology"/>
    /// fields already present on <see cref="PhysicalAppearance"/>, no new appearance vector invented.</param>
    /// <param name="PersonalitySummary">Big Five snapshot at capture time.</param>
    /// <param name="DominantBeliefKind">
    /// The strongest <see cref="PersonBeliefKind"/> this relationship produced (e.g. Warm,
    /// Rejecting) — the pattern that gets partially transferred to resembling new people.
    /// </param>
    /// <param name="DominantBeliefStrength">Strength of <see cref="DominantBeliefKind"/> at capture time [0,1].</param>
    /// <param name="Significance">
    /// The <see cref="Relationships.RelationshipEdge.Commitment"/> value at capture time — reused
    /// directly as the "how significant was this relationship" measure (Topic A's Investment Model
    /// already IS this construct; no new significance metric is introduced).
    /// </param>
    public sealed record SignificantOtherImprint(
        HumanId SourcePersonId,
        WDateTime CapturedAt,
        FacialMorphology FaceSummary,
        BigFive PersonalitySummary,
        PersonBeliefKind DominantBeliefKind,
        double DominantBeliefStrength,
        double Significance);
}
