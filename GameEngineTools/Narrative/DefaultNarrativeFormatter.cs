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
    /// Výchozí implementace <see cref="INarrativeFormatter"/> v češtině.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pokrývá tyto kategorie událostí:
    /// <list type="bullet">
    ///   <item><b>Sociální</b> — <see cref="FirstImpressionFormed"/>, <see cref="InteractionOutcome"/>,
    ///     <see cref="MicroPositive"/>, <see cref="MicroNegative"/>, <see cref="RepairAttempt"/>,
    ///     <see cref="SharedSleepBegan"/></item>
    ///   <item><b>Chování</b> — <see cref="ActionCommitted"/></item>
    ///   <item><b>Spánek</b> — <see cref="SleepEnded"/>, <see cref="NightmareTriggered"/>,
    ///     <see cref="SleepInterrupted"/></item>
    ///   <item><b>Paměť</b> — <see cref="MemoryEncoded"/> (jen pokud má <c>What</c>),
    ///     <see cref="MemoryConsolidated"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// Záměrně ignoruje: <c>SleepPhaseChanged</c> (debug info), <c>ActionProposed</c>
    /// (jen návrh — výsledek je <c>ActionCommitted</c>), <c>DreamOccurred</c>
    /// (herní vrstva si ho zpracuje po svém).
    /// </para>
    /// </remarks>
    public sealed class DefaultNarrativeFormatter : INarrativeFormatter
    {
        private readonly CzechWordFormComposer _wordComposer;

        public DefaultNarrativeFormatter()
        {
            _wordComposer = CzechGrammarServiceFactory.AddCzechGrammarServices(new ServiceCollection())
                .BuildServiceProvider()
                .GetRequiredService<CzechWordFormComposer>();
        }

        #region Veřejné API — Format

        /// <inheritdoc/>
        /// <remarks>
        /// Hlavní vstupní bod — pattern matching na konkrétní typ eventu.
        /// Každá větev deleguje na privátní metodu pro přehlednost.
        /// </remarks>
        public NarrativeEntry? Format(IDomainEvent ev, Func<HumanId, NarrativeCharacterInfo> resolveCharacter)
        {
            return ev switch
            {
                // ── Sociální interakce ────────────────────────────────────────────
                FirstImpressionFormed fi => FormatFirstImpression(fi, resolveCharacter),
                InteractionOutcome io => FormatInteractionOutcome(io, resolveCharacter),
                MicroPositive mp => FormatMicroPositive(mp, resolveCharacter),
                MicroNegative mn => FormatMicroNegative(mn, resolveCharacter),
                RepairAttempt ra => FormatRepairAttempt(ra, resolveCharacter),
                SharedSleepBegan ssb => FormatSharedSleepBegan(ssb, resolveCharacter),

                // ── Chování (Behavior) ────────────────────────────────────────────
                ActionCommitted ac => FormatActionCommitted(ac, resolveCharacter),

                // ── Spánek ────────────────────────────────────────────────────────
                SleepEnded se => FormatSleepEnded(se, resolveCharacter),
                NightmareTriggered nt => FormatNightmare(nt, resolveCharacter),
                SleepInterrupted si => FormatSleepInterrupted(si, resolveCharacter),

                // ── Paměť ─────────────────────────────────────────────────────────
                // Property pattern: zpracujeme jen pokud What není null.
                // Proč? MemoryEncoded bez What je narativně slepý — máme jen GUID.
                MemoryEncoded { What: not null } me => FormatMemoryEncoded(me, resolveCharacter),
                MemoryConsolidated mc => FormatMemoryConsolidated(mc, resolveCharacter),

                // ── Záměrně ignorované eventy ─────────────────────────────────────
                // SleepPhaseChanged  → debug info, ne příběh
                // ActionProposed     → jen návrh; výsledek je ActionCommitted
                // DreamOccurred      → herní vrstva si ho zpracuje po svém (vlastní hook)
                // MemoryRecalled     → interní; postava si vzpomněla, ale hráč to nevidí přímo
                _ => null
            };
        }

        #endregion Veřejné API — Format

        // ══════════════════════════════════════════════════════════════════════════
        // Sociální události
        // ══════════════════════════════════════════════════════════════════════════

        #region Sociální — FormatFirstImpression

        /// <summary>
        /// První dojem — postava A potkala B a zformovala si o ní názor.
        /// </summary>
        private NarrativeEntry FormatFirstImpression(
            FirstImpressionFormed fi,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(fi.A);
            var b = resolve(fi.B);

            // Interpretujeme číslo Like do srozumitelného textu
            var likeText = fi.Like switch
            {
                >= 70 => "kladný dojem",
                >= 45 => "neutrální dojem",
                _ => "negativní dojem"
            };

            // Přitažlivost — jen pokud je výrazná
            var attractionText = fi.Attraction >= 60
                ? $" {Conj(a, "Cítil k ní přitažlivost.", "Cítila k němu přitažlivost.")}"
                : string.Empty;

            var text = $"{a.Name} {Conj(a, "poznal", "poznala")} {Decl(b, Grammar.Core.Enums.Case.Accusative)} " +
                       $"— {likeText}.{attractionText}";

            return new NarrativeEntry(fi.OccurredAt, fi.A, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatFirstImpression

        #region Sociální — FormatInteractionOutcome

        /// <summary>
        /// Výsledek interakce — postava A oslovila B, B přijala nebo odmítla.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Proč je Subject = <c>io.From</c> (iniciátor)?<br/>
        /// Iniciátor je aktér děje. "Anna oslovila Petra" — hrdinka je Anna, ne Petr.
        /// </para>
        /// <para>
        /// Kde se tento event vyskytuje?<br/>
        /// V <c>LastOutbox</c> postavy B (příjemce), protože B generuje outcome.
        /// <c>SimulationScene.RouteOutcomes()</c> ho doručí zpět A,
        /// ale v narativu ho zachytíme z B's outboxu — proto výchozí zpracování je zde správné.
        /// </para>
        /// </remarks>
        private NarrativeEntry FormatInteractionOutcome(
            InteractionOutcome io,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var initiator = resolve(io.From);
            var recipient = resolve(io.To);

            // Překlad SpeechAct do přirozeného jazyka (objekt věty)
            var actText = io.Act switch
            {
                SpeechAct.SmallTalk => "nezávazný hovor",
                SpeechAct.Question => "zvídavou otázku",
                SpeechAct.SelfDisclosure => "osobní sdělení",
                SpeechAct.Validation => "slova podpory",
                SpeechAct.Boundary => "nastavení hranic",
                SpeechAct.Humor => "vtip",
                SpeechAct.Meta => "komentář o vztahu",
                SpeechAct.Invite => "pozvání",
                _ => "interakci"
            };

            // Reakce příjemce — přijal/odmítl s gramatickým rodem
            var reactionText = io.Accepted
                ? $"{recipient.Name} {Conj(recipient, "přijal", "přijala")} {actText}."
                : $"{recipient.Name} {Conj(recipient, "odmítl", "odmítla")} {actText}.";

            var text = $"{initiator.Name} {Conj(initiator, "nabídl", "nabídla")} " +
                       $"{Decl(recipient, Grammar.Core.Enums.Case.Dative)} {actText}. {reactionText}";

            // Odmítnutí je narativně stejně zajímavé jako přijetí — oba jsou Medium
            return new NarrativeEntry(io.OccurredAt, io.From, text, NarrativePriority.Medium);
        }

        #endregion Sociální — FormatInteractionOutcome

        #region Sociální — FormatMicroPositive / FormatMicroNegative

        /// <summary>
        /// Mikromoment — postava A udělala radost postavě B (pohlazení, kompliment…).
        /// </summary>
        private NarrativeEntry FormatMicroPositive(
            MicroPositive mp,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mp.A);
            var b = resolve(mp.B);

            var text = $"{a.Name} {Conj(a, "udělal", "udělala")} {Decl(b, Grammar.Core.Enums.Case.Dative)} radost: {mp.What}.";
            return new NarrativeEntry(mp.OccurredAt, mp.A, text, NarrativePriority.Medium);
        }

        /// <summary>
        /// Mikromoment — postava A (neúmyslně) zranila city postavy B.
        /// </summary>
        private NarrativeEntry FormatMicroNegative(
            MicroNegative mn,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mn.A);
            var b = resolve(mn.B);

            var text = $"{a.Name} {Conj(a, "zranil", "zranila")} {Decl(b, Grammar.Core.Enums.Case.Accusative)} city: {mn.What}.";
            return new NarrativeEntry(mn.OccurredAt, mn.A, text, NarrativePriority.Medium);
        }

        #endregion Sociální — FormatMicroPositive / FormatMicroNegative

        #region Sociální — FormatRepairAttempt

        /// <summary>
        /// Pokus o smíření — A se pokusil/a o nápravu vztahu s B.
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

            // Smíření nebo odmítnutí smíru — vždy High (vztahový zlom)
            return new NarrativeEntry(ra.OccurredAt, ra.A, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatRepairAttempt

        #region Sociální — FormatSharedSleepBegan

        /// <summary>
        /// Dvě postavy začaly spát společně — romanticky, přátelsky nebo z nutnosti.
        /// </summary>
        private static NarrativeEntry FormatSharedSleepBegan(
            SharedSleepBegan ssb,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var who = resolve(ssb.Who);
            var companion = resolve(ssb.Companion);

            // Kontext sdíleného spánku — převeď enum na přirozený jazyk
            var typeText = ssb.Type switch
            {
                SharedSleepType.Romantic => "romanticky",
                SharedSleepType.Protective => "ochranitelsky",
                SharedSleepType.Emergency => "z nutnosti",
                _ => "spolu"
            };

            // "usnuli" — mužský rod plurálu pro smíšenou dvojici (gramaticky korektní)
            var text = $"{who.Name} a {companion.Name} usnuli {typeText}.";
            return new NarrativeEntry(ssb.OccurredAt, ssb.Who, text, NarrativePriority.High);
        }

        #endregion Sociální — FormatSharedSleepBegan

        // ══════════════════════════════════════════════════════════════════════════
        // Chování (Behavior)
        // ══════════════════════════════════════════════════════════════════════════

        #region Chování — FormatActionCommitted

        /// <summary>
        /// Postava se rozhodla pro akci — "šel spát", "šla se najíst"…
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Proč null pro neznámé akce?</b><br/>
        /// Neznámá akce nemá smysluplný narativní překlad. Raději ticho než nesmysl.
        /// Přidáš novou akci → přidáš větev switch.
        /// </para>
        /// </remarks>
        private static NarrativeEntry? FormatActionCommitted(
            ActionCommitted ac,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(ac.Human);

            // Každá akce → (věta, priorita). Null text = ignorujeme.
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

                // Neznámá akce — ticho je lepší než nesmyslný text
                _ => (null!, NarrativePriority.Low)
            };

            return text is null ? null : new NarrativeEntry(ac.OccurredAt, ac.Human, text, priority);
        }

        #endregion Chování — FormatActionCommitted

        // ══════════════════════════════════════════════════════════════════════════
        // Spánek
        // ══════════════════════════════════════════════════════════════════════════

        #region Spánek — FormatSleepEnded

        /// <summary>
        /// Spánek skončil — postava se probudila s danou kvalitou a délkou odpočinku.
        /// </summary>
        private static NarrativeEntry FormatSleepEnded(
            SleepEnded se,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(se.Human);

            // Kvalita spánku interpretovaná jako pocit při probuzení
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
        /// Noční můra — zlomový moment spánkového cyklu, zvyšuje stres.
        /// </summary>
        private static NarrativeEntry FormatNightmare(
            NightmareTriggered nt,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(nt.Human);

            // Kontextová informace — vyšší stres → pravděpodobnější důvod noční můry
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
        /// Spánek byl přerušen — přepadení, bolest nebo vnější vyrušení.
        /// </summary>
        private NarrativeEntry FormatSleepInterrupted(
            SleepInterrupted si,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(si.Human);

            // Příčina přerušení — přeložíme enum do věty
            // Poznámka: gramatický rod je v minulém pasivním tvaru ("byl přerušen" / "byla přerušena")
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
        // Paměť
        // ══════════════════════════════════════════════════════════════════════════

        #region Paměť — FormatMemoryEncoded

        /// <summary>
        /// Postava si zapamatovala nový zážitek (nebo posílila existující).
        /// </summary>
        /// <remarks>
        /// Volá se jen pokud <see cref="MemoryEncoded.What"/> není null —
        /// zajistí to pattern matching v <see cref="Format"/>.
        /// </remarks>
        private NarrativeEntry FormatMemoryEncoded(
            MemoryEncoded me,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(me.Human);

            // ── Parsuj hlavičku — zjisti co za událost to je ──────────────────────
            var header = MemoryWhatParser.GetHeader(me.What);

            // ── Přelož schema na větu ─────────────────────────────────────────────
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

                // Neznámý formát — fallback, ticho je lepší než nesmysl
                _ => null
            };

            if (memoryText is null) return null!;

            // Síla vzpomínky — kontextová informace pro hráče
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

            // Přelož act na čitelný text
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
                "InviteIntimacy" => $"{Conj("projevit", VerbAspect.Perfective, Tense.Past, VerbClass.Class3, actor.IsFemale)} zájem o intimitu",
                // Rutinní akce (Eat, Drink, Sleep, SelfCare) — nízká narativní hodnota
                // → null = ticho, do deníku vzpomínek je nevypíšeme
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

            // Pokud máme konkrétní popis události, použijeme ho — jinak generický fallback
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
        /// Konsolidace paměti po spánku — spánek posílil N vzpomínek.
        /// </summary>
        private NarrativeEntry FormatMemoryConsolidated(
            MemoryConsolidated mc,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(mc.Human);

            // Plurál — "vzpomínku" vs. "vzpomínky" (gramatika číslovek v češtině)
            var countText = mc.Count switch
            {
                1 => "1 vzpomínku",
                _ => $"{mc.Count} vzpomínek"
            };

            var dat = Decl(actor, Grammar.Core.Enums.Case.Dative);
            var text = $"Spánek upevnil {dat} {countText} v paměti.";

            return new NarrativeEntry(mc.OccurredAt, mc.Human, text, NarrativePriority.Low);
        }

        #endregion Paměť — FormatMemoryConsolidated

        // ══════════════════════════════════════════════════════════════════════════
        // Privátní pomocné metody — gramatika
        // ══════════════════════════════════════════════════════════════════════════

        #region Pomocné — gramatika

        /// <summary>
        /// Vybere správný tvar slovesa podle gramatického rodu postavy.
        /// </summary>
        /// <param name="actor">Postava — zdroj rodu.</param>
        /// <param name="male">Tvar pro mužský rod (např. "šel", "přijal").</param>
        /// <param name="female">Tvar pro ženský rod (např. "šla", "přijala").</param>
        /// <returns>Správný tvar slovesa.</returns>
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
            var request = new CzechWordRequest
            {
                Lemma = actor.Name,
                WordCategory = WordCategory.Noun,
                Case = @case,
                Number = Number.Singular,
                Gender = actor.IsFemale ? Gender.Feminine : Gender.Masculine,
                IsAnimate = actor.IsFemale ? false : true,
                Pattern = actor.IsFemale ? "žena" : "pán"
            };

            return _wordComposer.GetFullForm(request).Form;
        }

        private string DeclString(string lemma, Case @case)
        {
            var request = new CzechWordRequest
            {
                Lemma = lemma,
                WordCategory = WordCategory.Pronoun,
                Case = @case
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
