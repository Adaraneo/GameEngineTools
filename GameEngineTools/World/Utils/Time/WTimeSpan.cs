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
    /// <b>Čistý datový typ + ambient properties.</b>
    /// Jediným zdrojem pravdy je <see cref="Ticks"/>. Vlastnosti jako
    /// <see cref="TotalHours"/>, <see cref="TotalDays"/> a factory metody jako
    /// <see cref="FromHours"/> vyžadují nakonfigurovaný <see cref="GameEngineTools.World.Core.Time.WWorld"/>.
    /// </para>
    /// <para>
    /// Interval může být záporný — vyjadřuje zpětný posun v čase.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Factory z lidských jednotek (vyžaduje WWorld.Configure)
    /// var twoHours  = WTimeSpan.FromHours(2);
    /// var halfDay   = WTimeSpan.FromHours(13);    // půl dne v 26h světě
    /// var threeWeeks = WTimeSpan.FromDays(21);
    ///
    /// // Properties (vyžadují WWorld.Configure)
    /// double h = twoHours.TotalHours;             // 2.0
    /// double d = threeWeeks.TotalDays;             // 21.0
    ///
    /// // Čistá matematika — nevyžaduje WWorld
    /// var longer = twoHours * 3;
    /// var diff   = WTimeSpan.Abs(a - b);
    /// </code>
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

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Počet worldTicks reprezentovaných tímto intervalem.
        /// Jediný zdroj pravdy — veškeré ostatní hodnoty se dopočítávají
        /// z <see cref="GameEngineTools.World.Core.Time.WWorld.Spec"/>.
        /// </summary>
        public long Ticks { get; }

        #endregion Vlastnosti — raw data

        #region Konstanty

        /// <summary>Interval nulové délky.</summary>
        public static WTimeSpan Zero => new(0);

        #endregion Konstanty

        #region Ambient vlastnosti — konverze na lidské jednotky

        // Tyto vlastnosti vyžadují WWorld.Configure. Výsledek může být desetinný i záporný.

        /// <summary>
        /// Celkový počet světových sekund tohoto intervalu.
        /// Může být desetinný i záporný.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public double TotalSeconds => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerSecond;

        /// <summary>
        /// Celkový počet světových minut tohoto intervalu.
        /// Může být desetinný i záporný.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public double TotalMinutes => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerMinute;

        /// <summary>
        /// Celkový počet světových hodin tohoto intervalu.
        /// Může být desetinný i záporný.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public double TotalHours => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerHour;

        /// <summary>
        /// Celkový počet světových dní tohoto intervalu.
        /// Může být desetinný i záporný.
        /// </summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public double TotalDays => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerDay;

        #endregion Ambient vlastnosti — konverze na lidské jednotky

        #region Static factory — z tiků

        /// <summary>
        /// Vytvoří interval přímo z počtu worldTicks.
        /// Sémanticky ekvivalentní konstruktoru — slouží pro čitelnější call sites.
        /// </summary>
        /// <param name="ticks">Počet worldTicks.</param>
        public static WTimeSpan FromTicks(long ticks) => new(ticks);

        #endregion Static factory — z tiků

        #region Static factory — z lidských jednotek (vyžadují WWorld.Configure)

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových sekund.
        /// </summary>
        /// <param name="seconds">Počet sekund (může být desetinný).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WTimeSpan FromSeconds(double seconds)
            => new((long)(seconds * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerSecond));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových minut.
        /// </summary>
        /// <param name="minutes">Počet minut (může být desetinný).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WTimeSpan FromMinutes(double minutes)
            => new((long)(minutes * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerMinute));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových hodin.
        /// </summary>
        /// <param name="hours">Počet hodin (může být desetinný).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WTimeSpan FromHours(double hours)
            => new((long)(hours * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerHour));

        /// <summary>
        /// Vytvoří interval odpovídající zadanému počtu světových dní.
        /// </summary>
        /// <param name="days">Počet dní (může být desetinný).</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public static WTimeSpan FromDays(double days)
            => new((long)(days * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerDay));

        #endregion Static factory — z lidských jednotek (vyžadují WWorld.Configure)

        #region Utility (čistá matematika)

        /// <summary>
        /// Vrátí absolutní hodnotu intervalu.
        /// Pokud je interval kladný nebo nulový, vrátí jej beze změny.
        /// </summary>
        public static WTimeSpan Abs(WTimeSpan x) => x.Ticks >= 0 ? x : new(-x.Ticks);

        /// <summary>
        /// Vrátí znaménko intervalu:
        /// <c>1</c> pro kladný, <c>-1</c> pro záporný, <c>0</c> pro nulový.
        /// </summary>
        public static int Sign(WTimeSpan x) => x.Ticks == 0 ? 0 : (x.Ticks > 0 ? 1 : -1);

        /// <summary>Vrátí delší ze dvou intervalů.</summary>
        public static WTimeSpan Max(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks ? a : b;

        /// <summary>Vrátí kratší ze dvou intervalů.</summary>
        public static WTimeSpan Min(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks ? a : b;

        /// <summary>
        /// Ořízne interval do rozsahu [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        /// <exception cref="ArgumentException">Pokud je min větší než max.</exception>
        public WTimeSpan Clamp(WTimeSpan min, WTimeSpan max)
        {
            if (min.Ticks > max.Ticks)
                throw new ArgumentException("min > max");

            if (Ticks < min.Ticks) return min;
            if (Ticks > max.Ticks) return max;
            return this;
        }

        #endregion Utility (čistá matematika)

        #region Aritmetické operátory

        /// <summary>Součet dvou intervalů.</summary>
        public static WTimeSpan operator +(WTimeSpan a, WTimeSpan b) => new(a.Ticks + b.Ticks);

        /// <summary>Rozdíl dvou intervalů.</summary>
        public static WTimeSpan operator -(WTimeSpan a, WTimeSpan b) => new(a.Ticks - b.Ticks);

        /// <summary>Negace intervalu — otočí směr časového posunu.</summary>
        public static WTimeSpan operator -(WTimeSpan a) => new(-a.Ticks);

        /// <summary>Škálování intervalu koeficientem <paramref name="k"/>.</summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public static WTimeSpan operator *(WTimeSpan a, double k) => new(checked((long)(a.Ticks * k)));

        /// <inheritdoc cref="operator *(WTimeSpan, double)"/>
        public static WTimeSpan operator *(double k, WTimeSpan a) => new(checked((long)(a.Ticks * k)));

        /// <summary>Dělení intervalu koeficientem <paramref name="k"/>.</summary>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public static WTimeSpan operator /(WTimeSpan a, double k) => new(checked((long)(a.Ticks / k)));

        /// <summary>
        /// Poměr dvou intervalů — vrací bezrozměrné <c>double</c>.
        /// Užitečné pro výpočet procenta uplynulého času.
        /// </summary>
        /// <exception cref="DivideByZeroException">Pokud je <paramref name="b"/> nulový.</exception>
        public static double operator /(WTimeSpan a, WTimeSpan b)
        {
            if (b.Ticks == 0) throw new DivideByZeroException();
            return (double)a.Ticks / b.Ticks;
        }

        #endregion Aritmetické operátory

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

        #endregion Porovnávací operátory

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WTimeSpan other) => Ticks.CompareTo(other.Ticks);

        /// <inheritdoc/>
        public bool Equals(WTimeSpan other) => Ticks == other.Ticks;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WTimeSpan d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => Ticks.GetHashCode();

        #endregion Rovnost a hashování

        #region Formátování

        /// <summary>
        /// Vrátí interval jako čitelný řetězec ve formátu <c>[-]d.hh:mm:ss</c> nebo <c>[-]hh:mm:ss</c>.
        /// Vyžaduje nakonfigurovaný <see cref="GameEngineTools.World.Core.Time.WWorld"/>.
        /// Fallback na raw ticky pokud WWorld není nakonfigurován.
        /// </summary>
        public override string ToString()
        {
            if (!GameEngineTools.World.Core.Time.WWorld.IsConfigured)
                return Ticks.ToString();

            var spec = GameEngineTools.World.Core.Time.WWorld.Spec;
            var sign = Ticks < 0 ? "-" : "";
            long at = Math.Abs(Ticks);
            long d = at / spec.TicksPerDay; at %= spec.TicksPerDay;
            int hh = (int)(at / spec.TicksPerHour); at %= spec.TicksPerHour;
            int mm = (int)(at / spec.TicksPerMinute); at %= spec.TicksPerMinute;
            int ss = (int)(at / spec.TicksPerSecond); at %= spec.TicksPerSecond;

            return d != 0
                ? $"{sign}{d}.{hh:00}:{mm:00}:{ss:00}"
                : $"{sign}{hh:00}:{mm:00}:{ss:00}";
        }

        #endregion Formátování
    }
}
