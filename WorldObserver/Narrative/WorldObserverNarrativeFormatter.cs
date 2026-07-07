// WorldObserverNarrativeFormatter.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Narrative
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Narrative;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Grammar.Czech;
    using Grammar.Czech.Models;
    using Grammar.Czech.Services;
    using Microsoft.Extensions.DependencyInjection;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// WorldObserver's own <see cref="INarrativeFormatter"/> — a richer, dashboard-oriented Czech
    /// narrator. Inspired by <c>DefaultNarrativeFormatter</c>, but it leans much harder on the
    /// <see cref="CzechWordFormComposer"/> from <c>50PSoftware.GrammarModular.Czech</c>: every verb is
    /// conjugated through the composer (gender-correct past tense), character names and object nouns are
    /// declined into the right case, and counts get numeral agreement — instead of hand-written
    /// "-l/-la" string pairs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Extra coverage over the default:</b> the food-economy Tier 1 actions
    /// (<see cref="ActionNames.Produce"/>/<see cref="ActionNames.Process"/>/<see cref="ActionNames.EatStored"/>)
    /// plus <see cref="ActionNames.Work"/>/<see cref="ActionNames.Create"/> are narrated, so production
    /// finally shows up in the live feed.
    /// </para>
    /// <para>
    /// <b>Robustness:</b> a live feed must never crash on a single odd lemma, so every composer call goes
    /// through <see cref="SafeForm"/>, which falls back to the raw lemma if the composer throws or returns
    /// nothing. All verbs are deliberately kept in classes 2/4/5 (the classes the default formatter proves
    /// the composer handles); irregular forms (jít, usnout, "si vzpomněl", passive participles) stay as
    /// explicit gendered pairs.
    /// </para>
    /// <para>
    /// Deliberately returns <c>null</c> (silence) for movement spam and for <c>MemoryEncoded</c> — the
    /// map view already shows movement, and per-memory encoding is too noisy for a world feed.
    /// </para>
    /// </remarks>
    public sealed class WorldObserverNarrativeFormatter : INarrativeFormatter
    {
        private readonly CzechWordFormComposer _composer;

        /// <summary>Creates the formatter and boots the Czech grammar services.</summary>
        public WorldObserverNarrativeFormatter()
        {
            _composer = CzechGrammarServiceFactory.AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider()
                .GetRequiredService<CzechWordFormComposer>();
        }

        #region INarrativeFormatter

        /// <inheritdoc/>
        public NarrativeEntry? Format(IDomainEvent ev, Func<HumanId, NarrativeCharacterInfo> resolve)
            => ev switch
            {
                FirstImpressionFormed fi => FirstImpression(fi, resolve),
                InteractionOutcome io => Interaction(io, resolve),
                MicroPositive mp => Micro(mp.OccurredAt, mp.A, mp.B, mp.Kind, resolve, positive: true),
                MicroNegative mn => Micro(mn.OccurredAt, mn.A, mn.B, mn.Kind, resolve, positive: false),
                RepairAttempt ra => Repair(ra, resolve),
                SharedSleepBegan ssb => SharedSleep(ssb, resolve),
                ActionCommitted ac => Action(ac, resolve),
                SleepEnded se => SleepEnded(se, resolve),
                NightmareTriggered nt => Nightmare(nt, resolve),
                SleepInterrupted si => SleepInterrupted(si, resolve),
                MemoryConsolidated mc => Consolidated(mc, resolve),
                _ => null,
            };

        #endregion INarrativeFormatter

        // ══════════════════════════════════════════════════════════════════════════
        // Social
        // ══════════════════════════════════════════════════════════════════════════

        private NarrativeEntry FirstImpression(FirstImpressionFormed fi, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(fi.A);
            var b = resolve(fi.B);
            var dojem = fi.Like switch { >= 70 => "kladný dojem", >= 45 => "smíšený dojem", _ => "chladný dojem" };
            var text = $"{a.Name} {Past("poznat", VerbClass.Class5, a)} {Name(b, Case.Accusative)} — {dojem}.";
            return new NarrativeEntry(fi.OccurredAt, fi.A, text, NarrativePriority.High);
        }

        private NarrativeEntry Interaction(InteractionOutcome io, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var from = resolve(io.From);
            var to = resolve(io.To);
            var act = io.Act switch
            {
                SpeechAct.SmallTalk => "nezávazný hovor",
                SpeechAct.Question => "zvídavou otázku",
                SpeechAct.SelfDisclosure => "osobní sdělení",
                SpeechAct.Validation => "slova podpory",
                SpeechAct.Boundary => "nastavení hranic",
                SpeechAct.Humor => "vtip",
                SpeechAct.Meta => "komentář o vztahu",
                SpeechAct.Invite => "pozvání",
                _ => "interakci",
            };
            var reaction = io.Accepted
                ? $"{to.Name} ho {Past("přijmout", VerbClass.Class2, to)}."
                : $"{to.Name} ho {Past("odmítnout", VerbClass.Class2, to)}.";
            var text = $"{from.Name} {Past("nabídnout", VerbClass.Class2, from)} {Name(to, Case.Dative)} {act}. {reaction}";
            return new NarrativeEntry(io.OccurredAt, io.From, text, NarrativePriority.Medium);
        }

        private NarrativeEntry Micro(
            WDateTime at, HumanId aId, HumanId bId, string kind,
            Func<HumanId, NarrativeCharacterInfo> resolve, bool positive)
        {
            var a = resolve(aId);
            var b = resolve(bId);
            var text = positive
                ? $"{a.Name} {Past("udělat", VerbClass.Class5, a)} {Name(b, Case.Dative)} radost: {kind}."
                : $"{a.Name} {Past("zranit", VerbClass.Class4, a)} {Name(b, Case.Accusative)} — {kind}.";
            return new NarrativeEntry(at, aId, text, NarrativePriority.Medium);
        }

        private NarrativeEntry Repair(RepairAttempt ra, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(ra.A);
            var b = resolve(ra.B);
            var text = ra.Accepted
                ? $"{a.Name} se {Past("smířit", VerbClass.Class4, a)} s {Name(b, Case.Instrumental)}. Vztah se hojí."
                : $"{a.Name} se {Past("pokusit", VerbClass.Class4, a)} o smír s {Name(b, Case.Instrumental)}, ale {b.Name} ho {Past("odmítnout", VerbClass.Class2, b)}.";
            return new NarrativeEntry(ra.OccurredAt, ra.A, text, NarrativePriority.High);
        }

        private static NarrativeEntry SharedSleep(SharedSleepBegan ssb, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var who = resolve(ssb.Who);
            var comp = resolve(ssb.Companion);
            var how = ssb.Type switch
            {
                SharedSleepType.Romantic => "romanticky",
                SharedSleepType.Protective => "ochranitelsky",
                SharedSleepType.Emergency => "z nutnosti",
                _ => "spolu",
            };
            // "usnuli" — masculine plural for a mixed pair; irregular, so kept as a literal.
            var text = $"{who.Name} a {comp.Name} vedle sebe {how} usnuli.";
            return new NarrativeEntry(ssb.OccurredAt, ssb.Who, text, NarrativePriority.High);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Behaviour (incl. food economy)
        // ══════════════════════════════════════════════════════════════════════════

        private NarrativeEntry? Action(ActionCommitted ac, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(ac.Human);
            var (text, prio) = ac.ActionName switch
            {
                // ── Food economy Tier 1 ───────────────────────────────────────────
                Produce => ($"{a.Name} {Past("sklidit", VerbClass.Class4, a)} {Noun("úroda", Case.Accusative, Gender.Feminine, "žena")}.",
                            NarrativePriority.Medium),
                // "zpracovat" = 3rd verb class (vzor kupuje, "zpracuje") → "zpracoval"; Class4 gives garbage.
                Process => ($"{a.Name} {Past("zpracovat", VerbClass.Class3, a)} {Noun("zásoba", Case.Accusative, Gender.Feminine, "žena", Number.Plural)}.",
                            NarrativePriority.Medium),
                EatStored => ($"{a.Name} se {Past("posilnit", VerbClass.Class4, a)} ze {Noun("zásoba", Case.Genitive, Gender.Feminine, "žena", Number.Plural)}.",
                            NarrativePriority.Low),

                // ── Everyday needs ─────────────────────────────────────────────────
                Eat => ($"{a.Name} {Past("utišit", VerbClass.Class4, a)} hlad.", NarrativePriority.Low),
                // "žízeň" accusative == nominative; the composer mangles the ě, so keep it literal.
                Drink => ($"{a.Name} {Past("uhasit", VerbClass.Class4, a)} žízeň.", NarrativePriority.Low),
                Sleep => ($"{a.Name} se {Past("uložit", VerbClass.Class4, a)} ke spánku.", NarrativePriority.Low),
                SelfCare => ($"{a.Name} si {Past("odpočinout", VerbClass.Class2, a)}.", NarrativePriority.Low),
                Work => ($"{a.Name} se {Past("pustit", VerbClass.Class4, a)} do práce.", NarrativePriority.Low),
                Create => ($"{a.Name} {Past("tvořit", VerbClass.Class4, a)}.", NarrativePriority.Low),
                ReachOut => ($"{a.Name} {Past("hledat", VerbClass.Class5, a)} {Noun("společnost", Case.Accusative, Gender.Feminine, "kost")}.", NarrativePriority.Medium),
                InviteIntimacy => ($"{a.Name} {Past("projevit", VerbClass.Class4, a)} zájem o intimitu.", NarrativePriority.High),

                _ => (null!, NarrativePriority.Low),
            };
            return text is null ? null : new NarrativeEntry(ac.OccurredAt, ac.Human, text, prio);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Sleep
        // ══════════════════════════════════════════════════════════════════════════

        private NarrativeEntry SleepEnded(SleepEnded se, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(se.Human);
            var feel = se.Quality switch
            {
                >= 80 => a.IsFemale ? "osvěžená" : "osvěžený",
                >= 55 => a.IsFemale ? "odpočatá" : "odpočatý",
                >= 30 => a.IsFemale ? "unavená" : "unavený",
                _ => a.IsFemale ? "vyčerpaná" : "vyčerpaný",
            };
            var tail = se.WasInterrupted ? " Spánek skončil dřív, než měl." : string.Empty;
            var text = $"{a.Name} se {Past("probudit", VerbClass.Class4, a)} po {se.TotalHoursSlept:F1} h — cítí se {feel}.{tail}";
            return new NarrativeEntry(se.OccurredAt, se.Human, text, NarrativePriority.Low);
        }

        private NarrativeEntry Nightmare(NightmareTriggered nt, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(nt.Human);
            var ctx = nt.StressAtSleepStart switch
            {
                >= 70 => "Vysoký stres před usnutím se projevil.",
                >= 40 => "Napětí dne přišlo ve snech.",
                _ => "Přišla nečekaně.",
            };
            var text = $"{a.Name} {Past("trpět", VerbClass.Class4, a)} noční {Noun("můra", Case.Instrumental, Gender.Feminine, "žena")}. {ctx}";
            return new NarrativeEntry(nt.OccurredAt, nt.Human, text, NarrativePriority.High);
        }

        private NarrativeEntry SleepInterrupted(SleepInterrupted si, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(si.Human);
            // Passive participles are gendered pairs — kept literal (composer passive voice is out of scope).
            var cause = si.Cause switch
            {
                InterruptCause.Ambush => a.IsFemale ? "byla přepadena" : "byl přepaden",
                InterruptCause.Pain => a.IsFemale ? "byla probuzena bolestí" : "byl probuzen bolestí",
                InterruptCause.External => a.IsFemale ? "byla vyrušena zvenku" : "byl vyrušen zvenku",
                _ => a.IsFemale ? "byla vyrušena" : "byl vyrušen",
            };
            var text = $"{a.Name} {cause} ve fázi {si.PhaseAtInterrupt}.";
            return new NarrativeEntry(si.OccurredAt, si.Human, text, NarrativePriority.High);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Memory
        // ══════════════════════════════════════════════════════════════════════════

        private NarrativeEntry Consolidated(MemoryConsolidated mc, Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mc.Human);
            // Czech numeral agreement: 1 → acc sg, 2–4 → nom/acc pl, 5+ → gen pl.
            var count = mc.Count switch
            {
                1 => $"1 {Noun("vzpomínka", Case.Accusative, Gender.Feminine, "žena")}",
                >= 2 and <= 4 => $"{mc.Count} {Noun("vzpomínka", Case.Accusative, Gender.Feminine, "žena", Number.Plural)}",
                _ => $"{mc.Count} {Noun("vzpomínka", Case.Genitive, Gender.Feminine, "žena", Number.Plural)}",
            };
            var text = $"Spánek {a.Name} {Past("upevnit", VerbClass.Class4, a)} {count} v paměti.";
            return new NarrativeEntry(mc.OccurredAt, mc.Human, text, NarrativePriority.Low);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Grammar helpers — every one routes through the Czech composer (SafeForm)
        // ══════════════════════════════════════════════════════════════════════════

        #region Grammar

        /// <summary>Conjugates a verb into gender-correct past tense via the composer.</summary>
        private string Past(string lemma, VerbClass verbClass, NarrativeCharacterInfo actor)
        {
            var req = new CzechWordRequest
            {
                Lemma = lemma,
                WordCategory = WordCategory.Verb,
                Person = Person.Third,
                Aspect = VerbAspect.Perfective,
                Modus = Modus.Conjunctive,
                Tense = Tense.Past,
                VerbClass = verbClass,
                Gender = actor.IsFemale ? Gender.Feminine : Gender.Masculine,
                Number = Number.Singular,
            };
            return SafeForm(req, lemma);
        }

        /// <summary>Declines a character's first name into the requested case.</summary>
        private string Name(NarrativeCharacterInfo actor, Case @case)
        {
            var pattern = actor.IsFemale switch
            {
                true when actor.Name.EndsWith("a") || actor.Name.EndsWith("ia") => "žena",
                true when actor.Name.EndsWith("e") => "růže",
                false when actor.Name.EndsWith("us") => "muž",
                _ => "pán",
            };
            var req = new CzechWordRequest
            {
                Lemma = actor.Name,
                WordCategory = WordCategory.Noun,
                Case = @case,
                Number = Number.Singular,
                Gender = actor.IsFemale ? Gender.Feminine : Gender.Masculine,
                IsAnimate = !actor.IsFemale,
                Pattern = pattern,
                HasMobileE = false,
            };
            return SafeForm(req, actor.Name);
        }

        /// <summary>Declines a common noun (object word) into the requested case/number.</summary>
        private string Noun(string lemma, Case @case, Gender gender, string pattern, Number number = Number.Singular)
        {
            var req = new CzechWordRequest
            {
                Lemma = lemma,
                WordCategory = WordCategory.Noun,
                Case = @case,
                Number = number,
                Gender = gender,
                Pattern = pattern,
            };
            return SafeForm(req, lemma);
        }

        /// <summary>
        /// Runs the composer but never lets a bad lemma/pattern crash the live feed —
        /// falls back to the raw lemma on any failure or empty result.
        /// </summary>
        private string SafeForm(CzechWordRequest request, string fallback)
        {
            try
            {
                var form = _composer.GetFullForm(request)?.Form;
                return string.IsNullOrWhiteSpace(form) ? fallback : form;
            }
            catch
            {
                return fallback;
            }
        }

        #endregion Grammar
    }
}
