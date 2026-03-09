// SystemClock.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System;
    using System.Timers;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Produkční implementace <see cref="IClock"/> — automaticky postupuje v čase
    /// na základě <see cref="IWorldClock.TimeScale"/> a <see cref="WorldTimeSpec.TicksPerSecond"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Timer tiká každou reálnou sekundu a posune <see cref="Now"/> o
    /// <c>TimeScale × TicksPerSecond</c> worldTicků. Při <c>TimeScale = 1.0</c>
    /// tedy 1 reálná sekunda = 1 světová sekunda.
    /// </para>
    /// <para>
    /// <b>Proč <see cref="WorldTimeSpec"/> místo <see cref="WorldTimeContext"/>?</b><br/>
    /// <c>WorldTimeContext</c> závisí na <c>IClock</c>, <c>IClock</c> by zpětně závisel
    /// na <c>WorldTimeContext</c> — kruhová závislost. <c>WorldTimeSpec</c> je čistý
    /// datový objekt bez závislostí, takže kruh nevzniká.
    /// </para>
    /// </remarks>
    public sealed class SystemClock : IClock, IDisposable
    {
        #region Soukromá pole

        private readonly Timer _timer;
        private readonly double _timeScale;

        // Počet ticků za reálnou sekundu přepočítaný na rychlost světa.
        // Předpočítáme jednou v konstruktoru — nemusíme sahat na spec v každém tiknutí.
        private readonly long _ticksPerRealSecond;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje hodiny. Počáteční <see cref="Now"/> je nastaveno na aktuální
        /// světový čas z <paramref name="worldClock"/>.
        /// </summary>
        /// <param name="worldClock">
        /// Zdroj počátečního worldTick času a <see cref="IWorldClock.TimeScale"/>.
        /// </param>
        /// <param name="spec">
        /// Specifikace světového času — potřebná pro výpočet posunu v <c>Timer_Elapsed</c>.
        /// Nepoužíváme <see cref="WorldTimeContext"/> kvůli předejití kruhové závislosti.
        /// </param>
        public SystemClock(IWorldClock worldClock, WorldTimeSpec spec)
        {
            _timeScale = worldClock.TimeScale;
            _ticksPerRealSecond = spec.TicksPerSecond;

            Now = new WDateTime(worldClock.NowWorldTicks());

            _timer = new Timer { Interval = 1000 };
            _timer.Elapsed += Timer_Elapsed;
        }

        #endregion Konstrukce

        #region IClock

        /// <inheritdoc/>
        public WDateTime Now { get; private set; }

        /// <inheritdoc/>
        public void Start() => _timer.Start();

        /// <inheritdoc/>
        public void Stop() => _timer.Stop();

        #endregion IClock

        #region Veřejné utility

        /// <summary>
        /// Ručně nastaví aktuální čas. Používej v herní smyčce nebo testech.
        /// </summary>
        /// <param name="now">Nový aktuální herní čas.</param>
        public void SetNow(WDateTime now) => Now = now;

        /// <summary>
        /// Posune aktuální čas o zadaný interval.
        /// </summary>
        /// <param name="dt">Interval o který se čas posune.</param>
        public void Advance(WTimeSpan dt) => Now = Now + dt;

        #endregion Veřejné utility

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }

        #endregion IDisposable

        #region Privátní

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // Posun o TimeScale světových sekund za každou reálnou sekundu.
            // Předpočítané _ticksPerRealSecond zabraňuje přístupu na spec v hot-path.
            Now = new WDateTime(Now.WorldTicks + (long)(_timeScale * _ticksPerRealSecond));
        }

        #endregion Privátní
    }
}
