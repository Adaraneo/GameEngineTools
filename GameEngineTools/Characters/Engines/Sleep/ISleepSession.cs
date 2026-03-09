// ISleepSession.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Kontrakt pro spánkovou session jedné postavy.
    /// Session vzniká při <see cref="SleepConfirmed"/> a zaniká při <see cref="SleepEnded"/>.
    /// </summary>
    /// <remarks>
    /// Session je oddělená od <see cref="IEngine{TState,TConfig}"/> záměrně —
    /// spánek má vlastní životní cyklus (Begin → Tick → End/Interrupt),
    /// který neodpovídá průběžnému tickování ostatních enginů.
    /// <br/><br/>
    /// BehaviorEngine drží referenci na aktivní session a předává jí tick,
    /// dokud <see cref="IsActive"/> vrací <c>true</c>.
    /// </remarks>
    public interface ISleepSession
    {
        #region Stav

        /// <summary>
        /// Aktuální fáze spánkového cyklu.
        /// </summary>
        SleepPhase CurrentPhase { get; }

        /// <summary>
        /// True pokud session stále běží (postava spí).
        /// False znamená, že session skončila — buď přirozeně nebo přerušením.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Plánovaný čas probuzení.
        /// Může být dřívější než skutečný konec, pokud dojde k přerušení.
        /// </summary>
        WDateTime PlannedWakeUp { get; }

        /// <summary>
        /// Volitelný společník sdíleného spánku.
        /// <c>null</c> pokud postava spí sama.
        /// </summary>
        HumanId? Companion { get; }

        /// <summary>
        /// Celkový počet hodin, které postava v této session prospala.
        /// Průběžně roste s každým tickem.
        /// </summary>
        double HoursSlept { get; }

        #endregion Stav

        #region Lifecycle

        /// <summary>
        /// Zahájí spánkovou session.
        /// Přepne fázi na <see cref="SleepPhase.Falling"/> a publikuje
        /// <see cref="SleepPhaseChanged"/> a volitelně <see cref="SharedSleepBegan"/>.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="plannedWakeUp">Plánovaný čas probuzení.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Sběrač eventů pro tento tick.</param>
        /// <param name="companion">Volitelný společník sdíleného spánku.</param>
        /// <param name="sharedType">Typ sdíleného spánku — musí být vyplněn pokud je <paramref name="companion"/> != null.</param>
        void Begin(
            WDateTime now,
            WDateTime plannedWakeUp,
            IHumanContext ctx,
            IEventCollector outbox,
            HumanId? companion = null,
            SharedSleepType? sharedType = null);

        /// <summary>
        /// Průběžný tick — posouvá čas v session, přepíná fáze,
        /// generuje rizikové a narrative eventy.
        /// Pokud session skončí přirozeně, nastaví <see cref="IsActive"/> na <c>false</c>
        /// a publikuje <see cref="SleepEnded"/>.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka uplynulého herního intervalu.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Sběrač eventů pro tento tick.</param>
        void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

        /// <summary>
        /// Přeruší spánek před plánovaným koncem.
        /// Publikuje <see cref="SleepInterrupted"/> a <see cref="SleepEnded"/>,
        /// nastaví <see cref="IsActive"/> na <c>false</c>.
        /// </summary>
        /// <param name="now">Aktuální herní čas přerušení.</param>
        /// <param name="cause">Příčina přerušení.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Sběrač eventů pro tento tick.</param>
        void Interrupt(WDateTime now, InterruptCause cause, IHumanContext ctx, IEventCollector outbox);

        #endregion Lifecycle
    }
}
