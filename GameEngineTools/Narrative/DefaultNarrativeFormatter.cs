// DefaultNarrativeFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
    using Grammar.Core.Enums;
    using Grammar.Czech;
    using Grammar.Czech.Models;
    using Grammar.Czech.Services;
    using Microsoft.Extensions.DependencyInjection;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Default <see cref="INarrativeFormatter"/> implementation producing Czech text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Covers these event categories:
    /// <list type="bullet">
    ///   <item><b>Social</b> — <see cref="FirstImpressionFormed"/>, <see cref="InteractionOutcome"/>,
    ///     <see cref="MicroPositive"/>, <see cref="MicroNegative"/>, <see cref="RepairAttempt"/>,
    ///     <see cref="SharedSleepBegan"/></item>
    ///   <item><b>Behaviour</b> — <see cref="ActionCommitted"/></item>
    ///   <item><b>Sleep</b> — <see cref="SleepEnded"/>, <see cref="NightmareTriggered"/>,
    ///     <see cref="SleepInterrupted"/></item>
    ///   <item><b>Memory</b> — <see cref="MemoryEncoded"/> (only when it has a <c>Kind</c>),
    ///     <see cref="MemoryConsolidated"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// Deliberately ignores: <c>SleepPhaseChanged</c> (debug info), <c>ActionProposed</c>
    /// (just a proposal — the result is <c>ActionCommitted</c>), <c>DreamOccurred</c>
    /// (the game layer handles it on its own).
    /// </para>
    /// </remarks>
    public sealed class DefaultNarrativeFormatter : INarrativeFormatter
    {
        private readonly CzechWordFormComposer _wordComposer;

        /// <summary>Creates the formatter and initialises the Czech grammar services.</summary>
        public DefaultNarrativeFormatter()
        {
            _wordComposer = CzechGrammarServiceFactory.AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider()
                .GetRequiredService<CzechWordFormComposer>();
        }

        #region Veřejné API — Format

        /// <inheritdoc/>
        /// <remarks>
        /// Main entry point — pattern matching on the concrete event type.
        /// Each branch delegates to a private method for clarity.
        /// </remarks>
        public NarrativeEntry? Format(IDomainEvent ev, Func<HumanId, NarrativeCharacterInfo> resolveCharacter)
        {
            return ev switch
            {
                // ── Social interactions ────────────────────────────────────────────
                FirstImpressionFormed fi => FormatFirstImpression(fi, resolveCharacter),
                InteractionOutcome io => FormatInteractionOutcome(io, resolveCharacter),
                MicroPositive mp => FormatMicroPositive(mp, resolveCharacter),
                MicroNegative mn => FormatMicroNegative(mn, resolveCharacter),
                RepairAttempt ra => FormatRepairAttempt(ra, resolveCharacter),
                SharedSleepBegan ssb => FormatSharedSleepBegan(ssb, resolveCharacter),

                // ── Behaviour ────────────────────────────────────────────
                ActionCommitted ac => FormatActionCommitted(ac, resolveCharacter),

                // ── Sleep ────────────────────────────────────────────────────────
                SleepEnded se => FormatSleepEnded(se, resolveCharacter),
                NightmareTriggered nt => FormatNightmare(nt, resolveCharacter),
                SleepInterrupted si => FormatSleepInterrupted(si, resolveCharacter),

                // ── Memory ─────────────────────────────────────────────────────────
                // Property pattern: handle only when Kind is not null.
                // Why? MemoryEncoded without a Kind is narratively blind — we only have a GUID.
                MemoryEncoded { What: not null } me => FormatMemoryEncoded(me, resolveCharacter),
                MemoryConsolidated mc => FormatMemoryConsolidated(mc, resolveCharacter),

                // ── Deliberately ignored events ─────────────────────────────────────
                // SleepPhaseChanged  → debug info, not story
                // ActionProposed     → just a proposal; the result is ActionCommitted
                // DreamOccurred      → the game layer handles it on its own (custom hook)
                // MemoryRecalled     → internal; the character remembered, but the player does not see it directly
                _ => null
            };
        }

        #endregion Veřejné API — Format

        // ══════════════════════════════════════════════════════════════════════════
        // Social events
        // ══════════════════════════════════════════════════════════════════════════

        #region Sociální — FormatFirstImpression

        /// <summary>
        /// First impression — character A met B and formed an opinion of them.
        /// </summary>
        private NarrativeEntry FormatFirstImpression(
            FirstImpressionFormed fi,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(fi.A);
            var b = resolve(fi.B);

            // Interpret the Like number into understandable text
            var likeText = fi.Like switch
            {
                >= 70 => "kladný dojem",
                >= 45 => "neutrální dojem",
                _ => "negativní dojem"
            };

            var text = $"{a.Name} {Conj(a, "poznal", "poznala")} {Decl(b, Grammar.Core.Enums.Case.Accusative)} " +
                       $"— {likeText}.";

            return new NarrativeEntry(fi.OccurredAt, fi.A, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatFirstImpression

        #region Sociální — FormatInteractionOutcome

        /// <summary>
        /// Interaction outcome — character A reached out to B; B accepted or declined.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Why is Subject = <c>io.From</c> (the initiator)?<br/>
        /// The initiator is the actor of the event. "Anna oslovila Petra" — the heroine is Anna, not Petr.
        /// </para>
        /// <para>
        /// Kde se tento event vyskytuje?<br/>
        /// In the <c>LastOutbox</c> of character B (the recipient), because B generates the outcome.
        /// <c>SimulationScene.RouteOutcomes()</c> delivers it back to A,
        /// but in the narrative we capture it from B's outbox — so the default handling here is correct.
        /// </para>
        /// </remarks>
        private NarrativeEntry FormatInteractionOutcome(
            InteractionOutcome io,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var initiator = resolve(io.From);
            var recipient = resolve(io.To);

            // Translate the RelationalActKind into natural language (the sentence object)
            var actText = io.Act switch
            {
                RelationalActKind.SmallTalk => "nezávazný hovor",
                RelationalActKind.Question => "zvídavou otázku",
                RelationalActKind.SelfDisclosure => "osobní sdělení",
                RelationalActKind.Validation => "slova podpory",
                RelationalActKind.Boundary => "nastavení hranic",
                RelationalActKind.Humor => "vtip",
                RelationalActKind.Meta => "komentář o vztahu",
                RelationalActKind.Invite => "pozvání",
                _ => "interakci"
            };

            // Recipient's reaction — accepted/declined with grammatical gender
            var reactionText = io.Accepted
                ? $"{recipient.Name} {Conj(recipient, "přijal", "přijala")} {actText}."
                : $"{recipient.Name} {Conj(recipient, "odmítl", "odmítla")} {actText}.";

            var text = $"{initiator.Name} {Conj(initiator, "nabídl", "nabídla")} " +
                       $"{Decl(recipient, Grammar.Core.Enums.Case.Dative)} {actText}. {reactionText}";

            // A rejection is narratively as interesting as acceptance — both are Medium
            return new NarrativeEntry(io.OccurredAt, io.From, text, NarrativePriority.Medium);
        }

        #endregion Sociální — FormatInteractionOutcome

        #region Sociální — FormatMicroPositive / FormatMicroNegative

        /// <summary>
        /// Micro-moment — character A pleased character B (a caress, a compliment…).
        /// </summary>
        private NarrativeEntry FormatMicroPositive(
            MicroPositive mp,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mp.A);
            var b = resolve(mp.B);

            var text = $"{a.Name} {Conj(a, "udělal", "udělala")} {Decl(b, Grammar.Core.Enums.Case.Dative)} radost: {mp.Kind}.";
            return new NarrativeEntry(mp.OccurredAt, mp.A, text, NarrativePriority.Medium);
        }

        /// <summary>
        /// Micro-moment — character A (unintentionally) hurt character B's feelings.
        /// </summary>
        private NarrativeEntry FormatMicroNegative(
            MicroNegative mn,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mn.A);
            var b = resolve(mn.B);

            var text = $"{a.Name} {Conj(a, "zranil", "zranila")} {Decl(b, Grammar.Core.Enums.Case.Accusative)} city: {mn.Kind}.";
            return new NarrativeEntry(mn.OccurredAt, mn.A, text, NarrativePriority.Medium);
        }

        #endregion Sociální — FormatMicroPositive / FormatMicroNegative

        #region Sociální — FormatRepairAttempt

        /// <summary>
        /// Repair attempt — A tried to mend the relationship with B.
        /// </summary>
        private NarrativeEntry FormatRepairAttempt(
            RepairAttempt ra,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(ra.A);
            var b = resolve(ra.B);

            var text = ra.Accepted
                ? $"{a.Name} {Conj(a, "se smířil", "se smířila")} s {Decl(b, Grammar.Core.Enums.Case.Instrumental)}. Vztah se hojí."
                : $"{a.Name} {Conj(a, "se pokusil", "se pokusila")} o smír s {Decl(b, Grammar.Core.Enums.Case.Instrumental)}, " +
                  $"ale {b.Name} {Conj(b, "odmítl", "odmítla")}.";

            // Reconciliation or its rejection — always High (a relationship turning point)
            return new NarrativeEntry(ra.OccurredAt, ra.A, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatRepairAttempt

        #region Sociální — FormatSharedSleepBegan

        /// <summary>
        /// Two characters began sleeping together — romantically, as friends, or out of necessity.
        /// </summary>
        private static NarrativeEntry FormatSharedSleepBegan(
            SharedSleepBegan ssb,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var who = resolve(ssb.Who);
            var companion = resolve(ssb.Companion);

            // Shared-sleep context — convert the enum into natural language
            var typeText = ssb.Type switch
            {
                SharedSleepType.Romantic => "romanticky",
                SharedSleepType.Protective => "ochranitelsky",
                SharedSleepType.Emergency => "z nutnosti",
                _ => "spolu"
            };

            // "usnuli" — masculine plural for a mixed pair (grammatically correct)
            var text = $"{who.Name} a {companion.Name} usnuli {typeText}.";
            return new NarrativeEntry(ssb.OccurredAt, ssb.Who, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatSharedSleepBegan

        // ══════════════════════════════════════════════════════════════════════════
        // Behaviour
        // ══════════════════════════════════════════════════════════════════════════

        #region Chování — FormatActionCommitted

        /// <summary>
        /// The character chose an action — "šel spát", "šla se najíst"…
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why null for unknown actions?</b><br/>
        /// An unknown action has no meaningful narrative translation. Silence is better than nonsense.
        /// Add a new action → add a switch branch.
        /// </para>
        /// </remarks>
        private static NarrativeEntry? FormatActionCommitted(
            ActionCommitted ac,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(ac.Human);

            // Each action → (sentence, priority). Null text = ignored.
            var (text, priority) = ac.ActionName switch
            {
                Sleep => ($"{actor.Name} {Conj(actor, "šel", "šla")} spát.",
                                   NarrativePriority.Low),

                Eat => ($"{actor.Name} se {Conj(actor, "šel", "šla")} najíst.",
                                   NarrativePriority.Low),

                Drink => ($"{actor.Name} se {Conj(actor, "šel", "šla")} napít.",
                                   NarrativePriority.Low),

                SelfCare => ($"{actor.Name} {Conj(actor, "se věnoval", "se věnovala")} péči o sebe.",
                                   NarrativePriority.Low),

                ReachOut => ($"{actor.Name} {Conj(actor, "se rozhlédl", "se rozhlédla")} po okolí — hledá společnost.",
                                   NarrativePriority.Medium),

                InviteIntimacy => ($"{actor.Name} {Conj(actor, "projevil", "projevila")} zájem o intimitu.",
                                   NarrativePriority.High),

                // Unknown action — silence is better than nonsensical text
                _ => (null!, NarrativePriority.Low)
            };

            return text is null ? null : new NarrativeEntry(ac.OccurredAt, ac.Human, text, priority);
        }

        #endregion Chování — FormatActionCommitted

        // ══════════════════════════════════════════════════════════════════════════
        // Sleep
        // ══════════════════════════════════════════════════════════════════════════

        #region Spánek — FormatSleepEnded

        /// <summary>
        /// Sleep ended — the character woke up with the given quality and rest duration.
        /// </summary>
        private static NarrativeEntry FormatSleepEnded(
            SleepEnded se,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(se.Human);

            // Sleep quality interpreted as how it felt on waking
            var qualityText = se.Quality switch
            {
                >= 80 => Conj(actor, "osvěžen", "osvěžena"),
                >= 55 => Conj(actor, "odpočatý", "odpočatá"),
                >= 30 => Conj(actor, "unavený", "unavená"),
                _ => Conj(actor, "vyčerpaný", "vyčerpaná")
            };

            var interruptedText = se.WasInterrupted
                ? " Spánek byl přerušen před plánovaným koncem."
                : string.Empty;

            var text = $"{actor.Name} {Conj(actor, "se probudil", "se probudila")} " +
                       $"po {se.TotalHoursSlept:F1} hodinách — cítí se {qualityText}.{interruptedText}";

            return new NarrativeEntry(se.OccurredAt, se.Human, text, NarrativePriority.Low);
        }

        #endregion Spánek — FormatSleepEnded

        #region Spánek — FormatNightmare

        /// <summary>
        /// Nightmare — a turning point in the sleep cycle that raises stress.
        /// </summary>
        private static NarrativeEntry FormatNightmare(
            NightmareTriggered nt,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(nt.Human);

            // Contextual info — higher stress → a more likely cause of the nightmare
            var stressContext = nt.StressAtSleepStart switch
            {
                >= 70 => "Vysoký stres před usnutím se projevil.",
                >= 40 => "Napětí dne přišlo ve snech.",
                _ => "Přesto ji postihla noční můra."
            };

            var text = $"{actor.Name} {Conj(actor, "trpěl", "trpěla")} noční můrou. {stressContext}";
            return new NarrativeEntry(nt.OccurredAt, nt.Human, text, NarrativePriority.High);
        }

        #endregion Spánek — FormatNightmare

        #region Spánek — FormatSleepInterrupted

        /// <summary>
        /// Sleep was interrupted — an ambush, pain, or external disturbance.
        /// </summary>
        private NarrativeEntry FormatSleepInterrupted(
            SleepInterrupted si,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(si.Human);

            // Interruption cause — translate the enum into a sentence
            // Note: grammatical gender is in the past passive form ("byl přerušen" / "byla přerušena")
            var causeText = si.Cause switch
            {
                InterruptCause.Ambush => Conj(actor, "byl přepaden", "byla přepadena"),
                InterruptCause.Pain => Conj(actor, "byl probuzen bolestí", "byla probuzena bolestí"),
                InterruptCause.External => Conj(actor, "byl vyrušen zvenku", "byla vyrušena zvenku"),
                _ => Conj(actor, "byl přerušen", "byla přerušena")
            };

            var phase = si.PhaseAtInterrupt.ToString();
            var gen = Decl(actor, Grammar.Core.Enums.Case.Genitive);
            var text = $"Spánek {gen} {causeText} ve fázi {phase}.";

            return new NarrativeEntry(si.OccurredAt, si.Human, text, NarrativePriority.High);
        }

        #endregion Spánek — FormatSleepInterrupted

        // ══════════════════════════════════════════════════════════════════════════
        // Memory
        // ══════════════════════════════════════════════════════════════════════════

        #region Paměť — FormatMemoryEncoded

        /// <summary>
        /// The character memorised a new experience (or reinforced an existing one).
        /// </summary>
        /// <remarks>
        /// Called only when <see cref="MemoryEncoded.What"/> is not null —
        /// ensured by the pattern matching in <see cref="Format"/>.
        /// </remarks>
        private NarrativeEntry FormatMemoryEncoded(
            MemoryEncoded me,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(me.Human);

            // ── Parse the header — determine what kind of event it is ──────────────────────
            var header = MemoryWhatParser.GetHeader(me.What);

            // ── Translate the schema into a sentence ─────────────────────────────────────────────
            var memoryText = header switch
            {
                // Interaction:SmallTalk:Accepted|from=a3f2c1|to=b7e9a2
                var h when h.StartsWith("Interaction:") => FormatInteractionMemory(me.What, resolve, actor),

                // Relation:FirstImpression:Positive|of=b7e9a2
                var h when h.StartsWith("Relation:FirstImpression") => FormatFirstImpressionMemory(me.What, resolve),

                // Relation:MicroPositive|from=a3f2c1|what=compliment
                var h when h.StartsWith("Relation:MicroPositive") => FormatMicroMemory(me.What, resolve, positive: true),
                var h when h.StartsWith("Relation:MicroNegative") => FormatMicroMemory(me.What, resolve, positive: false),

                // Relation:Repair:Accepted|with=b7e9a2
                var h when h.StartsWith("Relation:Repair") => FormatRepairMemory(me.What, resolve),

                // Sleep:Ended:High|hours=7.5
                var h when h.StartsWith("Sleep:Ended") => FormatSleepEndedMemory(me.What),

                // Sleep:Nightmare|stress=High
                "Sleep:Nightmare" => FormatNightmareMemory(me.What),

                // Action:Sleep, Action:Eat...
                var h when h.StartsWith("Action:") => FormatActionMemory(me.What, actor),

                // Unknown format — fallback; silence is better than nonsense
                _ => null
            };

            if (memoryText is null) return null!;

            // Memory strength — contextual info for the player
            var strengthHint = me.Strength switch
            {
                >= 0.8 => " (silná vzpomínka)",
                >= 0.4 => string.Empty,
                _ => " (slabá vzpomínka)"
            };

            var text = $"{actor.Name} {Conj(actor, "si vzpomněl", "si vzpomněla")}: {memoryText}{strengthHint}.";
            return new NarrativeEntry(me.OccurredAt, me.Human, text, NarrativePriority.Low);
        }

        #region HelperMethods

        private const string somebody = "někdo";

        private string? FormatInteractionMemory(string what, Func<HumanId, NarrativeCharacterInfo> resolve, NarrativeCharacterInfo actor)
        {
            // Interaction:Humor:Accepted|from=a3f2c1|to=b7e9a2
            var header = MemoryWhatParser.GetHeader(what);   // "Interaction:Humor:Accepted"
            var parts = header.Split(':');                   // ["Interaction", "Humor", "Accepted"]

            if (parts.Length < 3) return null;

            var act = parts[1];                           // "Humor"
            var outcome = parts[2];                           // "Accepted" / "Rejected"

            // Translate the act into readable text
            var actText = act switch
            {
                "SmallTalk" => "nezávazný hovor",
                "Humor" => "vtip",
                "Question" => "otázku",
                "SelfDisclosure" => "osobní sdělení",
                "Validation" => "podporu",
                "Invite" => "pozvání",
                "Boundary" => "nastavení hranic",
                "Meta" => "komentář o vztahu",
                _ => act
            };

            var fromParam = MemoryWhatParser.GetParam(what, "from");
            var fromPerson = fromParam is not null && Guid.TryParse(fromParam, out var fromGuid) ? resolve(new HumanId(fromGuid)) : null;

            var gen = fromPerson is null ? DeclString(somebody, Grammar.Core.Enums.Case.Genitive) : Decl(fromPerson, Grammar.Core.Enums.Case.Genitive);
            var acceptVerb = Conj("přijmout", VerbAspect.Perfective, Tense.Past, VerbClass.Class2, actor.IsFemale);
            var declineVerb = Conj("odmítnout", VerbAspect.Perfective, Tense.Past, VerbClass.Class2, actor.IsFemale);

            var acceptedText = actor.Name == fromPerson?.Name ? $"{acceptVerb} {actText}" : $"{acceptVerb} {actText} od {gen}";
            var declinedText = actor.Name == fromPerson?.Name ? $"{declineVerb} {actText}" : $"{declineVerb} {actText} od {gen}";

            return outcome == "Accepted" ? acceptedText : declinedText;
        }

        private string? FormatFirstImpressionMemory(string what, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            // Relation:FirstImpression:Positive|of=b7e9a2
            var sentiment = MemoryWhatParser.GetHeader(what).Split(':')[2]; // "Positive"
            var ofParam = MemoryWhatParser.GetParam(what, "of");
            var ofPerson = ofParam is not null && Guid.TryParse(ofParam, out var g)
                ? resolve(new HumanId(g)) : null;

            string gen = ofPerson is null ? DeclString(somebody, Grammar.Core.Enums.Case.Genitive) : Decl(ofPerson, Grammar.Core.Enums.Case.Genitive);
            string inst = ofPerson is null ? DeclString(somebody, Grammar.Core.Enums.Case.Instrumental) : Decl(ofPerson, Grammar.Core.Enums.Case.Instrumental);

            return sentiment switch
            {
                "Positive" => $"první dojem z {gen} byl dobrý",
                "Neutral" => $"první setkání s {inst}",
                "Negative" => $"první dojem z {gen} byl špatný",
                _ => $"setkání s {inst}"
            };
        }

        private static string? FormatSleepEndedMemory(string what)
        {
            // Sleep:Ended:High|hours=7.5
            var quality = MemoryWhatParser.GetHeader(what).Split(':')[2]; // "High"
            var hours = MemoryWhatParser.GetParam(what, "hours");

            var qualityText = quality switch
            {
                "High" => "výborný",
                "Medium" => "dobrý",
                "Low" => "špatný",
                "Poor" => "velmi špatný",
                _ => "?"
            };

            return hours is not null
                ? $"{qualityText} spánek ({hours} h)"
                : $"{qualityText} spánek";
        }

        private static string? FormatNightmareMemory(string what)
        {
            // Sleep:Nightmare|stress=High
            var stress = MemoryWhatParser.GetParam(what, "stress");
            return stress == "High"
                ? "noční můra — silný stres"
                : "noční můra";
        }

        private string? FormatActionMemory(string what, NarrativeCharacterInfo actor)
        {
            // Action:ReachOut, Action:Sleep...
            var action = MemoryWhatParser.GetHeader(what).Split(':')[1];
            return action switch
            {
                "ReachOut" => $"{Conj("hledat", VerbAspect.Perfective, Tense.Past, VerbClass.Class5, actor.IsFemale)} společnost",
                "InviteIntimacy" => $"{Conj("projevit", VerbAspect.Perfective, Tense.Past, VerbClass.Class4, actor.IsFemale)} zájem o intimitu",
                // Routine actions (Eat, Drink, Sleep, SelfCare) — low narrative value
                // → null = silence; we do not write them into the memory journal
                _ => null
            };
        }

        private string? FormatRepairMemory(string what, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            // Relation:Repair:Accepted|with=b7e9a2
            // Relation:Repair:Rejected|with=b7e9a2
            var outcome = MemoryWhatParser.GetHeader(what).Split(':')[2]; // "Accepted" / "Rejected"
            var withParam = MemoryWhatParser.GetParam(what, "with");
            var withPerson = withParam is not null && Guid.TryParse(withParam, out var g)
                ? resolve(new HumanId(g))
                : null;

            var inst = withPerson is null ? DeclString(somebody, Grammar.Core.Enums.Case.Instrumental) : Decl(withPerson, Grammar.Core.Enums.Case.Instrumental);

            return outcome == "Accepted"
                ? $"smíření s {inst} proběhlo"
                : $"pokus o smír s {inst} byl odmítnut";
        }

        private string? FormatMicroMemory(string what, Func<HumanId, NarrativeCharacterInfo> resolve, bool positive)
        {
            // Relation:MicroPositive|from=a3f2c1|what=compliment
            // Relation:MicroNegative|from=a3f2c1|what=interruption
            var fromParam = MemoryWhatParser.GetParam(what, "from");
            var fromPerson = fromParam is not null && Guid.TryParse(fromParam, out var g) ? resolve(new HumanId(g)) : null;

            var whatParam = MemoryWhatParser.GetParam(what, "what");

            var inst = fromPerson is null ? DeclString(somebody, Grammar.Core.Enums.Case.Instrumental) : Decl(fromPerson, Grammar.Core.Enums.Case.Instrumental);

            // If we have a concrete event description, use it — otherwise a generic fallback
            return positive
                ? whatParam is not null
                    ? $"příjemný moment s {inst}: {whatParam}"
                    : $"příjemný moment s {inst}"
                : whatParam is not null
                    ? $"nepříjemný moment s {inst}: {whatParam}"
                    : $"nepříjemný moment s {inst}";
        }

        #endregion HelperMethods

        #endregion Paměť — FormatMemoryEncoded

        #region Paměť — FormatMemoryConsolidated

        /// <summary>
        /// Memory consolidation after sleep — sleep reinforced N memories.
        /// </summary>
        private NarrativeEntry FormatMemoryConsolidated(
            MemoryConsolidated mc,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(mc.Human);

            // Plural — "vzpomínku" vs. "vzpomínky" (Czech numeral grammar)
            var countText = mc.Count switch
            {
                1 => $"1 {DeclString("vzpomínka", Case.Accusative, Number.Singular, WordCategory.Noun, Gender.Feminine, "žena")}",
                2 or 3 or 4 => $"{mc.Count} {DeclString("vzpomínka", Case.Accusative, Number.Plural, WordCategory.Noun, Gender.Feminine, "žena")}",
                _ => $"{mc.Count} {DeclString("vzpomínka", Case.Genitive, Number.Plural, WordCategory.Noun, Gender.Feminine, "žena")}"
            };

            var dat = Decl(actor, Grammar.Core.Enums.Case.Dative);
            var text = $"Spánek upevnil {dat} {countText} v paměti.";

            return new NarrativeEntry(mc.OccurredAt, mc.Human, text, NarrativePriority.Low);
        }

        #endregion Paměť — FormatMemoryConsolidated

        // ══════════════════════════════════════════════════════════════════════════
        // Private helper methods — grammar
        // ══════════════════════════════════════════════════════════════════════════

        #region Pomocné — gramatika

        /// <summary>
        /// Selects the correct verb form based on the character's grammatical gender.
        /// </summary>
        /// <param name="actor">Postava — zdroj rodu.</param>
        /// <param name="male">Form for masculine gender (e.g. "šel", "přijal").</param>
        /// <param name="female">Form for feminine gender (e.g. "šla", "přijala").</param>
        /// <returns>The correct verb form.</returns>
        /// <example>
        /// <code>
        /// // "Anna šla spát." vs. "Petr šel spát."
        /// $"{actor.Name} {Conj(actor, "šel", "šla")} spát."
        /// </code>
        /// </example>
        private static string Conj(NarrativeCharacterInfo actor, string male, string female)
            => actor.IsFemale ? female : male;

        private string Decl(NarrativeCharacterInfo actor, Case @case)
        {
            string malePattern = string.Empty;
            string femalePattern = string.Empty;

            var pattern = actor.IsFemale switch
            {
                true when actor.Name.EndsWith("a") || actor.Name.EndsWith("ia") => "žena",
                true when actor.Name.EndsWith("e") => "růže",
                false when actor.Name.EndsWith("us") => "muž",
                false when actor.Name.EndsWith("tor") => "pán",
                _ => "pán"
            };

            var request = new CzechWordRequest
            {
                Lemma = actor.Name,
                WordCategory = WordCategory.Noun,
                Case = @case,
                Number = Number.Singular,
                Gender = actor.IsFemale ? Gender.Feminine : Gender.Masculine,
                IsAnimate = actor.IsFemale ? false : true,
                Pattern = pattern,
                HasMobileE = false
            };

            return _wordComposer.GetFullForm(request).Form;
        }

        private string DeclString(string lemma, Case @case, Number? number = null, WordCategory wordCategory = WordCategory.Pronoun, Gender? gender = null, string? pattern = null)
        {
            var request = new CzechWordRequest
            {
                Lemma = lemma,
                WordCategory = wordCategory,
                Case = @case,
                Number = number,
                Gender = gender,
                Pattern = pattern
            };

            return _wordComposer.GetFullForm(request).Form;
        }

        private string Conj(string lemma, VerbAspect aspect, Tense tense, VerbClass verbClass, bool isFemale, Person person = Person.Third, string? pattern = null)
        {
            var request = new CzechWordRequest
            {
                Lemma = lemma,
                WordCategory = WordCategory.Verb,
                Person = person,
                Aspect = aspect,
                Modus = Modus.Conjunctive,
                Tense = tense,
                VerbClass = verbClass,
                Pattern = pattern,
                Gender = isFemale ? Gender.Feminine : Gender.Masculine,
                Number = Number.Singular
            };

            return _wordComposer.GetFullForm(request).Form;
        }

        #endregion Pomocné — gramatika
    }
}
