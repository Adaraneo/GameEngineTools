// TestClock.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using System.Timers;
using Timer = System.Timers.Timer;

namespace EngineTests.Utils
{
    /// <summary>
    /// Testovací implementace <see cref="IClock"/>.
    /// Startuje na světovém čase z <see cref="IWorldClock"/> a automaticky postupuje
    /// vpřed s rychlostí <see cref="IWorldClock.TimeScale"/> — ale pouze pokud
    /// explicitně zavoláš <see cref="Start"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Doporučené použití v testech:</b> nevolej <see cref="Start"/> — nechej
    /// hodiny stát a posouvej čas ručně přes <see cref="Advance"/>.
    /// Tím získáš plně deterministický test bez závislosti na reálném čase.
    /// </para>
    /// <para>
    /// Implementuje <see cref="IDisposable"/> — timer musí být uvolněn po testu.
    /// Stačí <c>ServiceProvider.Dispose()</c> pokud je provider disposable.
    /// </para>
    /// </remarks>
    internal sealed class TestClock : IClock, IDisposable
    {
        #region Soukromá pole

        private readonly Timer _timer;
        private readonly double _timeScale;

        // Předpočítaný posun za jednu reálnou sekundu (= TimeScale × TicksPerSecond).
        // Stejný důvod jako v SystemClock: vyhneme se závislosti na WorldTimeContext.
        private readonly long _ticksPerRealSecond;

        // long nelze označit volatile (64-bit není atomický na 32-bit platformách).
        // Místo toho používáme Interlocked pro všechny přístupy — čtení, zápis i přičtení.
        private long _nowTicks;

        private bool _disposed;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje testovací hodiny.
        /// Timer je vytvořen, ale <b>nespuštěn</b> — zavolej <see cref="Start"/>
        /// pokud potřebuješ automatický posun. V testech typicky nevolej.
        /// </summary>
        /// <param name="worldClock">Zdroj počátečního worldTick času a TimeScale.</param>
        /// <param name="spec">
        /// Specifikace světového času — potřebná pro výpočet posunu timeru.
        /// Bere se přímo spec (ne WorldTimeContext) kvůli předejití kruhové závislosti.
        /// </param>
        public TestClock(IWorldClock worldClock, WorldTimeSpec spec)
        {
            _timeScale = worldClock.TimeScale;
            _ticksPerRealSecond = spec.TicksPerSecond;

            // Počáteční čas bereme přímo z worldClock
            _nowTicks = worldClock.NowWorldTicks();

            // Interval 1000 ms — v testech timer nespouštíme, používáme Advance().
            _timer = new Timer { Interval = 1000 };
            _timer.Elapsed += Timer_Elapsed;
        }

        #endregion Konstrukce

        #region IClock

        /// <inheritdoc/>
        public WDateTime Now => new WDateTime(Interlocked.Read(ref _nowTicks));

        /// <inheritdoc/>
        /// <remarks>
        /// V testech typicky nevolej — místo toho používej <see cref="Advance"/>
        /// pro deterministické řízení světového času.
        /// </remarks>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer.Start();
        }

        /// <inheritdoc/>
        public void Stop() => _timer.Stop();

        #endregion IClock

        #region Testovací utility

        /// <summary>
        /// Posune aktuální herní čas o zadaný interval.
        /// Používej v testech pro deterministické řízení světového času.
        /// </summary>
        /// <param name="timeSpan">Interval o který se čas posune dopředu.</param>
        /// <example>
        /// <code>
        /// var clock = ServiceProvider.GetRequiredService&lt;IClock&gt;() as TestClock;
        /// clock.Advance(WTimeSpan.FromHours(8));   // posun o 8 světových hodin
        /// </code>
        /// </example>
        public void Advance(WTimeSpan timeSpan)
            => Interlocked.Add(ref _nowTicks, timeSpan.Ticks);

        /// <summary>
        /// Nastaví aktuální herní čas na konkrétní hodnotu.
        /// Užitečné pro skok na specifický bod v čase (např. test jarní rovnodennosti).
        /// </summary>
        /// <param name="now">Nový aktuální čas.</param>
        public void SetNow(WDateTime now)
            => Interlocked.Exchange(ref _nowTicks, now.WorldTicks);

        #endregion Testovací utility

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }

        #endregion IDisposable

        #region Privátní

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
            => Interlocked.Add(ref _nowTicks, (long)(_timeScale * _ticksPerRealSecond));

        #endregion Privátní
    }
}
