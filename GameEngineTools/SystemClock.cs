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
    /// na základě <see cref="IWorldClock.TimeScale"/> a <see cref="WorldTimeContext.Spec"/>.
    /// </summary>
    /// <remarks>
    /// Timer tiká každou reálnou sekundu a posune <see cref="Now"/> o
    /// <c>TimeScale × TicksPerSecond</c> worldTicků. Při <c>TimeScale = 1.0</c>
    /// tedy 1 reálná sekunda = 1 světová sekunda.
    /// </remarks>
    public sealed class SystemClock : IClock, IDisposable
    {
        #region Soukromá pole

        private readonly Timer _timer;
        private readonly double _timeScale;
        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje hodiny. Počáteční <see cref="Now"/> je nastaveno na aktuální
        /// světový čas z <paramref name="worldClock"/>.
        /// </summary>
        /// <param name="worldClock">
        /// Zdroj aktuálního worldTick času a TimeScale.
        /// Používá se pro počáteční hodnotu <see cref="Now"/> a rychlost posunu.
        /// </param>
        /// <param name="ctx">
        /// Kontext světového času — potřebný pro výpočet posunu v Timer_Elapsed.
        /// Nahrazuje odstraněný přístup přes <c>WDateTime.AddSeconds</c>.
        /// </param>
        public SystemClock(IWorldClock worldClock, WorldTimeContext ctx)
        {
            _timeScale = worldClock.TimeScale;
            _ctx = ctx;

            // Počáteční čas bereme přímo z worldClock — WDateTime.Now (statika) bylo odstraněno
            Now = new WDateTime(worldClock.NowWorldTicks());

            _timer = new Timer { Interval = 1000 };
            _timer.Elapsed += Timer_Elapsed;
        }

        #endregion

        #region IClock

        /// <inheritdoc/>
        public WDateTime Now { get; private set; }

        /// <inheritdoc/>
        public void Start() => _timer.Start();

        /// <inheritdoc/>
        public void Stop() => _timer.Stop();

        #endregion

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

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }

        #endregion

        #region Privátní

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // Výpočet přes ticky přímo — WTimeSpan.FromSeconds (statika) bylo odstraněno.
            // TimeScale je double, takže násobení je přesné i pro zlomkové rychlosti.
            var ticksPerSecond = _ctx.Spec.TicksPerSecond;
            Now = new WDateTime(Now.WorldTicks + (long)(_timeScale * ticksPerSecond));
        }

        #endregion
    }
}
