// TestClock.cs
// Copyright (c) 50PSoftware

using System.Timers;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
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
    /// V testech stačí <c>ServiceProvider.Dispose()</c> (pokud je provider disposable),
    /// nebo zavolej <c>Dispose()</c> přímo na instanci.
    /// </para>
    /// </remarks>
    internal sealed class TestClock : IClock, IDisposable
    {
        #region Soukromá pole

        private readonly Timer            _timer;
        private readonly double           _timeScale;
        private readonly WorldTimeContext _wtctx;

        // long nelze označit volatile (64-bit není atomický na 32-bit platformách).
        // Místo toho používáme Interlocked pro všechny přístupy — čtení, zápis i přičtení.
        private long _nowTicks;

        private bool _disposed;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje testovací hodiny.
        /// Timer je vytvořen, ale <b>nespuštěn</b> — zavolej <see cref="Start"/>
        /// pokud potřebuješ automatický posun. V testech typicky nevolej.
        /// </summary>
        /// <param name="worldClock">Zdroj počátečního worldTick času a TimeScale.</param>
        /// <param name="ctx">
        /// Kontext světového času — potřebný pro převod TimeScale (double sekund)
        /// na <see cref="WTimeSpan"/> v tiscích.
        /// </param>
        public TestClock(IWorldClock worldClock, WorldTimeContext ctx)
        {
            _timeScale = worldClock.TimeScale;
            _wtctx       = ctx;

            // Počáteční čas bereme přímo z worldClock
            // WDateTime.Now (statická property) bylo odstraněno — nepoužíváme
            _nowTicks = worldClock.NowWorldTicks();

            // Interval 1000 ms je rozumný pro produkční simulaci.
            // V testech timer nespouštíme — používáme Advance() pro determinismus.
            _timer          = new Timer { Interval = 1000 };
            _timer.Elapsed += Timer_Elapsed;
        }

        #endregion

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
        public void Stop()
        {
            _timer.Stop();
        }

        #endregion

        #region Testovací utility

        /// <summary>
        /// Posune aktuální herní čas o zadaný interval.
        /// Používej v testech pro deterministické řízení světového času.
        /// </summary>
        /// <param name="timeSpan">Interval o který se čas posune dopředu.</param>
        /// <example>
        /// <code>
        /// var clock = ServiceProvider.GetRequiredService&lt;IClock&gt;() as TestClock;
        /// clock.Advance(ctx.Hours(8));   // posun o 8 světových hodin
        /// </code>
        /// </example>
        public void Advance(WTimeSpan timeSpan)
        {
            // Atomic přičtení přes Interlocked — thread-safe i bez lock
            Interlocked.Add(ref _nowTicks, timeSpan.Ticks);
        }

        /// <summary>
        /// Nastaví aktuální herní čas na konkrétní hodnotu.
        /// Užitečné pro skok na specifický bod v čase (např. test jarní rovnodennosti).
        /// </summary>
        /// <param name="now">Nový aktuální čas.</param>
        public void SetNow(WDateTime now) => Interlocked.Exchange(ref _nowTicks, now.WorldTicks);

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        /// <remarks>
        /// Timer je <see cref="IDisposable"/> — pokud ho neuvolníš, zůstane vlákno
        /// timeru aktivní i po skončení testu a může způsobit falešné zápimy do
        /// <see cref="Now"/> po úklidu DI kontejneru.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }

        #endregion

        #region Privátní

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            var ticks = _wtctx.Seconds(_timeScale).Ticks;
            Interlocked.Add(ref _nowTicks, ticks);
        }

        #endregion
    }
}
