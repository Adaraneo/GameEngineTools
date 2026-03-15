// DefaultNarrativeFormatter.cs
// Copyright (c) 50PSoftware
//
// Český narativní formatter — překládá doménové eventy na čitelné věty.
//
// Architektonická lekce:
//   Formatter je PURE FUNCTION — bere event, vrací větu. Žádný stav, žádné závislosti.
//   Tím splňujeme SRP (single responsibility) i OCP (přidáš event → přidáš větev switch).
//
// Gramatika:
//   Čeština vyžaduje gramatický rod. Pomocná metoda Conj() vybírá mužskou/ženskou formu
//   na základě NarrativeCharacterInfo.IsFemale.

namespace GameEngineTools.Narrative
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
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

        #endregion

        // ══════════════════════════════════════════════════════════════════════════
        // Sociální události
        // ══════════════════════════════════════════════════════════════════════════

        #region Sociální — FormatFirstImpression

        /// <summary>
        /// První dojem — postava A potkala B a zformovala si o ní názor.
        /// </summary>
        private static NarrativeEntry FormatFirstImpression(
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
                _     => "negativní dojem"
            };

            // Přitažlivost — jen pokud je výrazná
            var attractionText = fi.Attraction >= 60
                ? $" {Conj(a, "Cítil k ní přitažlivost.", "Cítila k němu přitažlivost.")}"
                : string.Empty;

            var text = $"{a.Name} {Conj(a, "poznal", "poznala")} {b.Name} " +
                       $"— {likeText}.{attractionText}";

            return new NarrativeEntry(fi.OccurredAt, fi.A, text, NarrativePriority.High);
        }

        #endregion

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
        private static NarrativeEntry FormatInteractionOutcome(
            InteractionOutcome io,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var initiator = resolve(io.From);
            var recipient = resolve(io.To);

            // Překlad SpeechAct do přirozeného jazyka (objekt věty)
            var actText = io.Act switch
            {
                SpeechAct.SmallTalk      => "nezávazný hovor",
                SpeechAct.Question       => "zvídavou otázku",
                SpeechAct.SelfDisclosure => "osobní sdělení",
                SpeechAct.Validation     => "slova podpory",
                SpeechAct.Boundary       => "nastavení hranic",
                SpeechAct.Humor          => "vtip",
                SpeechAct.Meta           => "komentář o vztahu",
                SpeechAct.Invite         => "pozvání",
                _                        => "interakci"
            };

            // Reakce příjemce — přijal/odmítl s gramatickým rodem
            var reactionText = io.Accepted
                ? $"{recipient.Name} {Conj(recipient, "přijal", "přijala")} {actText}."
                : $"{recipient.Name} {Conj(recipient, "odmítl", "odmítla")} {actText}.";

            var text = $"{initiator.Name} {Conj(initiator, "nabídl", "nabídla")} " +
                       $"{recipient.Name} {actText}. {reactionText}";

            // Odmítnutí je narativně stejně zajímavé jako přijetí — oba jsou Medium
            return new NarrativeEntry(io.OccurredAt, io.From, text, NarrativePriority.Medium);
        }

        #endregion

        #region Sociální — FormatMicroPositive / FormatMicroNegative

        /// <summary>
        /// Mikromoment — postava A udělala radost postavě B (pohlazení, kompliment…).
        /// </summary>
        private static NarrativeEntry FormatMicroPositive(
            MicroPositive mp,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mp.A);
            var b = resolve(mp.B);

            var text = $"{a.Name} {Conj(a, "udělal", "udělala")} {b.Name} radost: {mp.What}.";
            return new NarrativeEntry(mp.OccurredAt, mp.A, text, NarrativePriority.Medium);
        }

        /// <summary>
        /// Mikromoment — postava A (neúmyslně) zranila city postavy B.
        /// </summary>
        private static NarrativeEntry FormatMicroNegative(
            MicroNegative mn,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(mn.A);
            var b = resolve(mn.B);

            var text = $"{a.Name} {Conj(a, "zranil", "zranila")} city {b.Name}: {mn.What}.";
            return new NarrativeEntry(mn.OccurredAt, mn.A, text, NarrativePriority.Medium);
        }

        #endregion

        #region Sociální — FormatRepairAttempt

        /// <summary>
        /// Pokus o smíření — A se pokusil/a o nápravu vztahu s B.
        /// </summary>
        private static NarrativeEntry FormatRepairAttempt(
            RepairAttempt ra,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var a = resolve(ra.A);
            var b = resolve(ra.B);

            var text = ra.Accepted
                ? $"{a.Name} {Conj(a, "se smířil", "se smířila")} s {b.Name}. Vztah se hojí."
                : $"{a.Name} {Conj(a, "se pokusil", "se pokusila")} o smír s {b.Name}, " +
                  $"ale {b.Name} {Conj(b, "odmítl", "odmítla")}.";

            // Smíření nebo odmítnutí smíru — vždy High (vztahový zlom)
            return new NarrativeEntry(ra.OccurredAt, ra.A, text, NarrativePriority.High);
        }

        #endregion

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
                SharedSleepType.Romantic   => "romanticky",
                SharedSleepType.Protective => "ochranitelsky",
                SharedSleepType.Emergency  => "z nutnosti",
                _                          => "spolu"
            };

            // "usnuli" — mužský rod plurálu pro smíšenou dvojici (gramaticky korektní)
            var text = $"{who.Name} a {companion.Name} usnuli {typeText}.";
            return new NarrativeEntry(ssb.OccurredAt, ssb.Who, text, NarrativePriority.High);
        }

        #endregion

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
                Sleep          => ($"{actor.Name} {Conj(actor, "šel", "šla")} spát.",
                                   NarrativePriority.Low),

                Eat            => ($"{actor.Name} {Conj(actor, "šel", "šla")} se najíst.",
                                   NarrativePriority.Low),

                Drink          => ($"{actor.Name} {Conj(actor, "šel", "šla")} se napít.",
                                   NarrativePriority.Low),

                SelfCare       => ($"{actor.Name} {Conj(actor, "se věnoval", "se věnovala")} péči o sebe.",
                                   NarrativePriority.Low),

                ReachOut       => ($"{actor.Name} {Conj(actor, "se rozhlédl", "se rozhlédla")} po okolí — hledá společnost.",
                                   NarrativePriority.Medium),

                InviteIntimacy => ($"{actor.Name} {Conj(actor, "projevil", "projevila")} zájem o intimitu.",
                                   NarrativePriority.High),

                // Neznámá akce — ticho je lepší než nesmyslný text
                _              => (null!, NarrativePriority.Low)
            };

            return text is null ? null : new NarrativeEntry(ac.OccurredAt, ac.Human, text, priority);
        }

        #endregion

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
                _     => Conj(actor, "vyčerpaný", "vyčerpaná")
            };

            var interruptedText = se.WasInterrupted
                ? " Spánek byl přerušen před plánovaným koncem."
                : string.Empty;

            var text = $"{actor.Name} {Conj(actor, "se probudil", "se probudila")} " +
                       $"po {se.TotalHoursSlept:F1} hodinách — cítí se {qualityText}.{interruptedText}";

            return new NarrativeEntry(se.OccurredAt, se.Human, text, NarrativePriority.Low);
        }

        #endregion

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
                _     => "Přesto ji postihla noční můra."
            };

            var text = $"{actor.Name} {Conj(actor, "trpěl", "trpěla")} noční můrou. {stressContext}";
            return new NarrativeEntry(nt.OccurredAt, nt.Human, text, NarrativePriority.High);
        }

        #endregion

        #region Spánek — FormatSleepInterrupted

        /// <summary>
        /// Spánek byl přerušen — přepadení, bolest nebo vnější vyrušení.
        /// </summary>
        private static NarrativeEntry FormatSleepInterrupted(
            SleepInterrupted si,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(si.Human);

            // Příčina přerušení — přeložíme enum do věty
            // Poznámka: gramatický rod je v minulém pasivním tvaru ("byl přerušen" / "byla přerušena")
            var causeText = si.Cause switch
            {
                InterruptCause.Ambush   => Conj(actor, "byl přepaden", "byla přepadena"),
                InterruptCause.Pain     => Conj(actor, "byl probuzen bolestí", "byla probuzena bolestí"),
                InterruptCause.External => Conj(actor, "byl vyrušen zvenku", "byla vyrušena zvenku"),
                _                       => Conj(actor, "byl přerušen", "byla přerušena")
            };

            var phase = si.PhaseAtInterrupt.ToString();
            var text = $"Spánek {actor.Name} {causeText} ve fázi {phase}.";

            return new NarrativeEntry(si.OccurredAt, si.Human, text, NarrativePriority.High);
        }

        #endregion

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
        private static NarrativeEntry FormatMemoryEncoded(
            MemoryEncoded me,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(me.Human);

            // Síla vzpomínky — kontextualizace pro hráče
            var strengthHint = me.Strength switch
            {
                >= 0.8 => " (silná vzpomínka)",
                >= 0.4 => string.Empty,
                _      => " (slabá vzpomínka)"
            };

            var text = $"{actor.Name} {Conj(actor, "si zapamatoval", "si zapamatovala")}: " +
                       $"\"{me.What}\"{strengthHint}.";

            return new NarrativeEntry(me.OccurredAt, me.Human, text, NarrativePriority.Low);
        }

        #endregion

        #region Paměť — FormatMemoryConsolidated

        /// <summary>
        /// Konsolidace paměti po spánku — spánek posílil N vzpomínek.
        /// </summary>
        private static NarrativeEntry FormatMemoryConsolidated(
            MemoryConsolidated mc,
            Func<HumanId, NarrativeCharacterInfo> resolve)
        {
            var actor = resolve(mc.Human);

            // Plurál — "vzpomínku" vs. "vzpomínky" (gramatika číslovek v češtině)
            var countText = mc.Count switch
            {
                1  => "1 vzpomínku",
                _  => $"{mc.Count} vzpomínek"
            };

            var text = $"Spánek {Conj(actor, "upevnil", "upevnila")} {actor.Name} {countText} " +
                       $"v paměti.";

            return new NarrativeEntry(mc.OccurredAt, mc.Human, text, NarrativePriority.Low);
        }

        #endregion

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

        #endregion
    }
}
