// WTimeSpan.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje časový interval v jednotkách <c>worldTicks</c> — stejná jednotka jako
    /// <see cref="WDateTime.WorldTicks"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ.</b> Struct neobsahuje žádnou závislost na <c>WorldTimeSpec</c>
    /// ani jiném externím stavu. Jediným zdrojem pravdy je <see cref="Ticks"/>.
    /// </para>
    /// <para>
    /// Operace vyžadující znalost světového kalendáře nebo časové soustavy
    /// (konverze na hodiny/dny, formátování, factory z hodin/dní) patří do
    /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
    /// </para>
    /// <para>
    /// Interval může být záporný — vyjadřuje zpětný posun v čase.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(WTimeSpanJsonConverter))]
    public readonly struct WTimeSpan :
        IEquatable<WTimeSpan>, IComparable<WTimeSpan>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nový interval s přesným počtem <c>worldTicks</c>.
        /// </summary>
        /// <param name="ticks">
        /// Počet worldTicks. Záporná hodnota reprezentuje zpětný posun v čase.
        /// </param>
        public WTimeSpan(long ticks) => Ticks = ticks;

        #endregion

        #region Vlastnosti

        /// <summary>
        /// Počet worldTicks reprezentovaných tímto intervalem.
        /// Jediný zdroj pravdy — veškeré ostatní hodnoty se dopočítávají přes
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
        /// </summary>
        public long Ticks { get; }

        #endregion

        #region Konstanty

        /// <summary>Interval nulové délky.</summary>
        public static WTimeSpan Zero => new(0);

        #endregion

        #region Factory metody

        /// <summary>
        /// Vytvoří interval přímo z počtu worldTicks.
        /// Sémanticky ekvivalentní konstruktoru — slouží pro čitelnější call sites.
        /// </summary>
        /// <param name="ticks">Počet worldTicks.</param>
        /// <returns>Nový interval o délce <paramref name="ticks"/> worldTicks.</returns>
        public static WTimeSpan FromTicks(long ticks) => new(ticks);

        #endregion

        #region Utility (čistá matematika)

        /// <summary>
        /// Vrátí absolutní hodnotu intervalu.
        /// Pokud je interval kladný nebo nulový, vrátí jej beze změny.
        /// </summary>
        /// <param name="x">Zdrojový interval.</param>
        public static WTimeSpan Abs(WTimeSpan x) => x.Ticks >= 0 ? x : new(-x.Ticks);

        /// <summary>
        /// Vrátí znaménko intervalu:
        /// <c>1</c> pro kladný, <c>-1</c> pro záporný, <c>0</c> pro nulový.
        /// </summary>
        /// <param name="x">Zdrojový interval.</param>
        public static int Sign(WTimeSpan x) => x.Ticks == 0 ? 0 : (x.Ticks > 0 ? 1 : -1);

        /// <summary>Vrátí delší ze dvou intervalů.</summary>
        public static WTimeSpan Max(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks ? a : b;

        /// <summary>Vrátí kratší ze dvou intervalů.</summary>
        public static WTimeSpan Min(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks ? a : b;

        /// <summary>
        /// Ořízne interval do rozsahu [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        /// <param name="min">Dolní hranice (včetně).</param>
        /// <param name="max">Horní hranice (včetně).</param>
        /// <exception cref="ArgumentException">
        /// Pokud je <paramref name="min"/> větší než <paramref name="max"/>.
        /// </exception>
        public WTimeSpan Clamp(WTimeSpan min, WTimeSpan max)
        {
            if (min.Ticks > max.Ticks)
            {
                throw new ArgumentException("min > max");
            }

            if (Ticks < min.Ticks)
            {
                return min;
            }

            if (Ticks > max.Ticks)
            {
                return max;
            }

            return this;
        }

        #endregion

        #region Aritmetické operátory

        /// <summary>Součet dvou intervalů.</summary>
        public static WTimeSpan operator +(WTimeSpan a, WTimeSpan b) => new(a.Ticks + b.Ticks);

        /// <summary>Rozdíl dvou intervalů.</summary>
        public static WTimeSpan operator -(WTimeSpan a, WTimeSpan b) => new(a.Ticks - b.Ticks);

        /// <summary>Negace intervalu — otočí směr časového posunu.</summary>
        public static WTimeSpan operator -(WTimeSpan a) => new(-a.Ticks);

        /// <summary>
        /// Škálování intervalu koeficientem <paramref name="k"/>.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public static WTimeSpan operator *(WTimeSpan a, double k) => new(checked((long)(a.Ticks * k)));

        /// <inheritdoc cref="operator *(WTimeSpan, double)"/>
        public static WTimeSpan operator *(double k, WTimeSpan a) => new(checked((long)(a.Ticks * k)));

        /// <summary>
        /// Dělení intervalu koeficientem <paramref name="k"/>.
        /// </summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public static WTimeSpan operator /(WTimeSpan a, double k) => new(checked((long)(a.Ticks / k)));

        /// <summary>
        /// Poměr dvou intervalů — vrací <c>a / b</c> jako bezrozměrné <c>double</c>.
        /// Užitečné např. pro výpočet procenta uplynulého času.
        /// </summary>
        /// <exception cref="DivideByZeroException">Pokud je <paramref name="b"/> nulový.</exception>
        public static double operator /(WTimeSpan a, WTimeSpan b)
        {
            if (b.Ticks == 0)
            {
                throw new DivideByZeroException();
            }

            return (double)a.Ticks / b.Ticks;
        }

        #endregion

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud jsou oba intervaly stejně dlouhé.</summary>
        public static bool operator ==(WTimeSpan a, WTimeSpan b) => a.Ticks == b.Ticks;

        /// <summary>Vrátí <c>true</c> pokud se intervaly liší.</summary>
        public static bool operator !=(WTimeSpan a, WTimeSpan b) => a.Ticks != b.Ticks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> kratší než <paramref name="b"/>.</summary>
        public static bool operator <(WTimeSpan a, WTimeSpan b) => a.Ticks < b.Ticks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> kratší nebo stejně dlouhý jako <paramref name="b"/>.</summary>
        public static bool operator <=(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> delší než <paramref name="b"/>.</summary>
        public static bool operator >(WTimeSpan a, WTimeSpan b) => a.Ticks > b.Ticks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> delší nebo stejně dlouhý jako <paramref name="b"/>.</summary>
        public static bool operator >=(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks;

        #endregion

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WTimeSpan other) => Ticks.CompareTo(other.Ticks);

        /// <inheritdoc/>
        public bool Equals(WTimeSpan other) => Ticks == other.Ticks;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WTimeSpan d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => Ticks.GetHashCode();

        #endregion

        #region Formátování

        /// <summary>
        /// Vrátí raw počet worldTicks jako řetězec.
        /// <para>
        /// Pro čitelný formát (např. <c>01:30:00</c>) použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.Format(WTimeSpan)"/>.
        /// </para>
        /// </summary>
        public override string ToString() => Ticks.ToString();

        #endregion
    }
}
