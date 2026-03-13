// DefaultSleepSession.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Výchozí implementace spánkové session.
    /// Řídí průchod fázemi, generuje rizikové a narrative eventy.
    /// </summary>
    /// <remarks>
    /// Session se vytváří jako nová instance pro každý spánek —
    /// není registrována v DI kontejneru jako singleton.
    /// Vytváří ji <c>DefaultBehaviorEngine</c> při zpracování <see cref="SleepConfirmed"/>.
    /// </remarks>
    internal sealed class DefaultSleepSession : ISleepSession
    {
        #region Privátní pole

        private readonly SleepConfig _cfg;
        private readonly ILogger _log;
        private readonly IRandomSource _rng;

        /// <summary>Čas, kdy session začala (= čas usnutí).</summary>
        private WDateTime _sleepStart;

        /// <summary>Čas, kdy aktuální fáze začala.</summary>
        private WDateTime _phaseStart;

        /// <summary>Hodnota stresu v okamžiku usnutí — ovlivňuje pravděpodobnost noční můry.</summary>
        private double _stressAtSleepStart;

        /// <summary>True pokud byl v průběhu REM fáze již vygenerován dream event.</summary>
        private bool _dreamFiredThisRem;

        #endregion Privátní pole

        #region Veřejné vlastnosti (ISleepSession)

        /// <inheritdoc/>
        public SleepPhase CurrentPhase { get; private set; }

        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        /// <inheritdoc/>
        public WDateTime PlannedWakeUp { get; private set; }

        /// <inheritdoc/>
        public HumanId? Companion { get; private set; }

        /// <inheritdoc/>
        public double HoursSlept { get; private set; }

        #endregion Veřejné vlastnosti (ISleepSession)

        #region Konstruktor

        /// <summary>
        /// Vytvoří instanci session. Volej <see cref="Begin"/> pro zahájení.
        /// </summary>
        /// <param name="cfg">Konfigurace spánkového subsystému.</param>
        /// <param name="loggerFactory">Factory pro logger.</param>
        /// <param name="rng">Deterministický generátor náhodných čísel postavy.</param>
        public DefaultSleepSession(SleepConfig cfg, ILoggerFactory loggerFactory, IRandomSource rng)
        {
            _cfg = cfg;
            _log = loggerFactory.CreateLogger<DefaultSleepSession>();
            _rng = rng;
        }

        #endregion Konstruktor

        #region Lifecycle

        /// <inheritdoc/>
        public void Begin(
            WDateTime now,
            WDateTime plannedWakeUp,
            IHumanContext ctx,
            IEventCollector outbox,
            HumanId? companion = null,
            SharedSleepType? sharedType = null)
        {
            _sleepStart = now;
            _phaseStart = now;
            _stressAtSleepStart = ctx.Snapshot.Psychology.Stress;
            _dreamFiredThisRem = false;
            HoursSlept = 0;
            IsActive = true;
            Companion = companion;
            PlannedWakeUp = plannedWakeUp;

            EnterPhase(SleepPhase.Falling, now, ctx, outbox);

            // Sdílený spánek — publikuj kontext
            if (companion.HasValue && sharedType.HasValue)
            {
                outbox.Add(new SharedSleepBegan(now, ctx.Id, companion.Value, sharedType.Value));
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
                {
                    _log.SharedSleepStarted(ctx.Id.Value.ToString(), sharedType.Value.ToString(), companion.Value.ToString());
                }
            }
            else
            {
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
                {
                    _log.SleepStarted(ctx.Id.Value.ToString(), (plannedWakeUp - now).TotalHours);
                }
            }
        }

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsActive) return;

            var h = Math.Max(0, dt.TotalHours);
            HoursSlept += h;

            // Přirozené probuzení — čas vypršel
            if (now >= PlannedWakeUp)
            {
                EndSession(now, wasInterrupted: false, ctx, outbox);
                return;
            }

            // Rizikový check pro aktuální fázi
            CheckAmbush(now, h, ctx, outbox);

            // Fázový průchod
            var timeInPhase = now - _phaseStart;
            switch (CurrentPhase)
            {
                case SleepPhase.Falling:
                    if (timeInPhase.TotalHours >= _cfg.FallingDurationHours)
                        EnterPhase(SleepPhase.Light, now, ctx, outbox);
                    break;

                case SleepPhase.Light:
                    if (timeInPhase.TotalHours >= _cfg.LightDurationHours)
                        EnterPhase(SleepPhase.Deep, now, ctx, outbox);
                    break;

                case SleepPhase.Deep:
                    if (timeInPhase.TotalHours >= _cfg.DeepDurationHours)
                        EnterPhase(SleepPhase.Rem, now, ctx, outbox);
                    break;

                case SleepPhase.Rem:
                    // Narrative: dream a nightmare — každý jen jednou za REM cyklus
                    FireRemEvents(now, ctx, outbox);

                    if (timeInPhase.TotalHours >= _cfg.RemDurationHours)
                    {
                        _dreamFiredThisRem = false; // reset pro příští REM
                        EnterPhase(SleepPhase.Light, now, ctx, outbox); // cyklus znovu
                    }
                    break;

                case SleepPhase.Waking:
                    // Fáze probouzení — session skončí v příštím Ticku nebo hned
                    EndSession(now, wasInterrupted: false, ctx, outbox);
                    break;
            }
        }

        /// <inheritdoc/>
        public void Interrupt(WDateTime now, InterruptCause cause, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsActive) return;

            outbox.Add(new SleepInterrupted(now, ctx.Id, cause, CurrentPhase));
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
            {
                _log.SleepInterrupted(ctx.Id.Value.ToString(), cause.ToString(), CurrentPhase.ToString());
            }

            EndSession(now, wasInterrupted: true, ctx, outbox);
        }

        #endregion Lifecycle

        #region Privátní pomocné metody

        /// <summary>
        /// Přejde do zadané fáze a publikuje <see cref="SleepPhaseChanged"/>.
        /// </summary>
        private void EnterPhase(SleepPhase phase, WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            CurrentPhase = phase;
            _phaseStart = now;
            outbox.Add(new SleepPhaseChanged(now, ctx.Id, phase));
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
            {
                _log.SleepPhaseEntered(ctx.Id.Value.ToString(), phase.ToString());
            }
        }

        /// <summary>
        /// Zkontroluje pravděpodobnost přepadení pro aktuální tick.
        /// Riziko je modulováno fází spánku a přítomností společníka.
        /// </summary>
        private void CheckAmbush(WDateTime now, double h, IHumanContext ctx, IEventCollector outbox)
        {
            // Deep fáze snižuje riziko přepadení (postava slyší méně, ale útok je těžší)
            // REM fáze — postava je v klidu, ale těžko reaguje
            var phaseModifier = CurrentPhase switch
            {
                SleepPhase.Falling => 1.2,  // snadno přepadnutelný, ještě nespí
                SleepPhase.Light => 1.0,
                SleepPhase.Deep => 0.6,  // hluboko spí — útočník má výhodu, ale postava hůře reaguje
                SleepPhase.Rem => 0.4,  // tělesně uvolněná, psychicky aktivní
                SleepPhase.Waking => 0.8,
                _ => 1.0
            };

            var companionModifier = Companion.HasValue ? _cfg.CompanionGuardModifier : 1.0;
            var chance = _cfg.AmbushBaseChancePerHour * h * phaseModifier * companionModifier;

            if (_rng.Chance(chance))
            {
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
                {
                    _log.SleepAmbush(ctx.Id.Value.ToString(), CurrentPhase.ToString());
                }

                Interrupt(now, InterruptCause.Ambush, ctx, outbox);
            }
        }

        /// <summary>
        /// Generuje narrative eventy v průběhu REM fáze (sen nebo noční můra).
        /// Každý event je vygenerován nejvýše jednou za jeden REM průchod.
        /// </summary>
        private void FireRemEvents(WDateTime now, IHumanContext ctx, IEventCollector outbox)
        {
            if (_dreamFiredThisRem) return;

            // Pravděpodobnost noční můry závisí na stresu v okamžiku usnutí
            var nightmareChance = _stressAtSleepStart > 50
                ? _cfg.NightmareChanceHighStress
                : _cfg.NightmareChanceNormal;

            if (_rng.Chance(nightmareChance))
            {
                outbox.Add(new NightmareTriggered(now, ctx.Id, _stressAtSleepStart));
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
                {
                    _log.SleepNightmare(ctx.Id.Value.ToString(), _stressAtSleepStart);
                }

                Interrupt(now, InterruptCause.Nightmare, ctx, outbox);
            }
            else
            {
                // Deterministický seed ze seedy postavy + času — aby sny byly konzistentní
                var dreamSeed = ctx.Random.Next(0, int.MaxValue);
                outbox.Add(new DreamOccurred(now, ctx.Id, dreamSeed));
                using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
                {
                    _log.SleepDream(ctx.Id.Value.ToString(), dreamSeed);
                }
            }

            _dreamFiredThisRem = true;
        }

        /// <summary>
        /// Ukončí session, vypočítá kvalitu spánku a publikuje <see cref="SleepEnded"/>.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="wasInterrupted">True pokud byl spánek přerušen.</param>
        private void EndSession(WDateTime now, bool wasInterrupted, IHumanContext ctx, IEventCollector outbox)
        {
            IsActive = false;

            var quality = ComputeSleepQuality(wasInterrupted);

            outbox.Add(new SleepEnded(now, ctx.Id, HoursSlept, quality, wasInterrupted));
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultSleepSession))))
            {
                _log.SleepWokeUp(ctx.Id.Value.ToString(), HoursSlept, quality, wasInterrupted);
            }
        }

        /// <summary>
        /// Vypočítá kvalitu spánku (0–100) na základě délky a průběhu.
        /// <br/>
        /// Ideální spánek = <see cref="SleepConfig.BaseSleepHours"/> bez přerušení = 100.
        /// </summary>
        private double ComputeSleepQuality(bool wasInterrupted)
        {
            // Základ: poměr prospané doby k ideální délce
            // Ideál je v BehaviorConfig.BaseSleepHours (předáváme přes plannedWakeUp délku)
            var plannedHours = (PlannedWakeUp - _sleepStart).TotalHours;
            var completionRatio = plannedHours > 0
                ? Math.Min(1.0, HoursSlept / plannedHours)
                : 0.5;

            var quality = completionRatio * 100.0;

            // Penalizace za přerušení — záleží na tom, v jaké fázi k přerušení došlo
            if (wasInterrupted)
            {
                quality *= CurrentPhase switch
                {
                    SleepPhase.Falling => 0.2,  // skoro nic
                    SleepPhase.Light => 0.4,
                    SleepPhase.Deep => 0.6,
                    SleepPhase.Rem => 0.75, // REM je cenný, přerušení bolí
                    SleepPhase.Waking => 0.9,  // skoro dospáno
                    _ => 0.5
                };
            }

            return Math.Clamp(quality, 0, 100);
        }

        #endregion Privátní pomocné metody
    }
}
