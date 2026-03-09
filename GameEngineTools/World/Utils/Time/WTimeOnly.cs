// WTimeOnly.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje čas dne bez datové složky, uložený jako počet worldTicks od půlnoci.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ + ambient properties.</b>
    /// Jediným zdrojem pravdy je <see cref="TicksOfDay"/>.
    /// Properties jako <see cref="Hour"/>, <see cref="Minute"/>, <see cref="Second"/>
    /// a metody jako <see cref="AddHours"/> vyžadují nakonfigurovaný <see cref="WWorld"/>.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Factory (vyžaduje WWorld.Configure)
    /// var time = WTimeOnly.New(6, 30, 0);
    ///
    /// // Ambient properties (vyžadují WWorld.Configure)
    /// int hour = time.Hour;     // 6
    /// int min  = time.Minute;   // 30
    ///
    /// // Čistá matematika — nevyžaduje WWorld
    /// var diff  = timeA.Diff(timeB);   // WTimeSpan (bez wrapu)
    /// bool late = time > WTimeOnly.New(22, 0, 0);
    ///
    /// // Aritmetika s wraparoundem (vyžaduje WWorld.Configure)
    /// var later = time.AddHours(3);    // wrap přes půlnoc automaticky
    /// </code>
    /// </para>
    /// </remarks>
    public readonly struct WTimeOnly :
        IEquatable<WTimeOnly>, IComparable<WTimeOnly>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nový čas dne z počtu worldTicks od půlnoci.
        /// </summary>
        /// <param name="ticksOfDay">
        /// Počet worldTicks od začátku dne (0 = půlnoc). Musí být v rozsahu [0, TicksPerDay).
        /// Rozsah je validován v <see cref="New"/> a <see cref="WWorld"/>-závislých metodách.
        /// </param>
        public WTimeOnly(long ticksOfDay) => TicksOfDay = ticksOfDay;

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Počet worldTicks od začátku dne (půlnoci). Jediný zdroj pravdy.
        /// Platný rozsah je [0, TicksPerDay) kde TicksPerDay závisí na <see cref="WWorld.Spec"/>.
        /// </summary>
        public long TicksOfDay { get; }

        #endregion Vlastnosti — raw data

        #region Ambient vlastnosti — složky času (vyžadují WWorld.Configure)

        /// <summary>Hodina tohoto času dne (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Hour
        {
            get
            {
                var spec = WWorld.Spec;
                return (int)(TicksOfDay / spec.TicksPerHour);
            }
        }

        /// <summary>Minuta tohoto času dne (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Minute
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = TicksOfDay % spec.TicksPerHour;
                return (int)(rem / spec.TicksPerMinute);
            }
        }

        /// <summary>Sekunda tohoto času dne (0-based).</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public int Second
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = TicksOfDay % spec.TicksPerMinute;
                return (int)(rem / spec.TicksPerSecond);
            }
        }

        #endregion Ambient vlastnosti — složky času (vyžadují WWorld.Configure)

        #region Static factory

        /// <summary>
        /// Vytvoří čas dne ze složek (hodina, minuta, sekunda).
        /// Validuje rozsah vůči <see cref="WWorld.Spec"/>.
        /// </summary>
        /// <param name="hour">Hodina (0..HoursPerDay-1).</param>
        /// <param name="minute">Minuta (0..MinutesPerHour-1).</param>
        /// <param name="second">Sekunda (0..SecondsPerMinute-1).</param>
        /// <param name="subTick">Subtiky pod sekundou (0..TicksPerSecond-1). Výchozí 0.</param>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Pokud je složka mimo platný rozsah.</exception>
        public static WTimeOnly New(int hour, int minute, int second, long subTick = 0)
        {
            var spec = WWorld.Spec;

            if (hour < 0 || hour >= spec.HoursPerDay) throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute >= spec.MinutesPerHour) throw new ArgumentOutOfRangeException(nameof(minute));
            if (second < 0 || second >= spec.SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));
            if (subTick < 0 || subTick >= spec.TicksPerSecond) throw new ArgumentOutOfRangeException(nameof(subTick));

            return new WTimeOnly(
                hour * spec.TicksPerHour
              + minute * spec.TicksPerMinute
              + second * spec.TicksPerSecond
              + subTick);
        }

        #endregion Static factory

        #region Aritmetika — čistá matematika (nevyžaduje WWorld)

        /// <summary>
        /// Vrátí hrubý rozdíl dvou časů dne jako <see cref="WTimeSpan"/> (bez wrapu přes půlnoc).
        /// </summary>
        /// <remarks>
        /// Pro nejkratší vzdálenost s wraparoundem přes půlnoc použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.TimeDiff"/>.
        /// </remarks>
        public WTimeSpan Diff(WTimeOnly other) => new(TicksOfDay - other.TicksOfDay);

        #endregion Aritmetika — čistá matematika (nevyžaduje WWorld)

        #region Aritmetika s wraparoundem (vyžadují WWorld.Configure)

        /// <summary>
        /// Přičte interval k času dne s automatickým wraparoundem přes půlnoc.
        /// </summary>
        /// <param name="span">Interval (může být záporný).</param>
        /// <returns>Nový čas dne v rozsahu [0, TicksPerDay).</returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WTimeOnly Add(WTimeSpan span)
        {
            long tpd = WWorld.Spec.TicksPerDay;
            long t = (TicksOfDay + span.Ticks) % tpd;
            if (t < 0) t += tpd;
            return new WTimeOnly(t);
        }

        /// <summary>Přičte hodiny k času dne s wraparoundem přes půlnoc.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WTimeOnly AddHours(double hours) => Add(WTimeSpan.FromHours(hours));

        /// <summary>Přičte minuty k času dne s wraparoundem přes půlnoc.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WTimeOnly AddMinutes(double minutes) => Add(WTimeSpan.FromMinutes(minutes));

        /// <summary>Přičte sekundy k času dne s wraparoundem přes půlnoc.</summary>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
        public WTimeOnly AddSeconds(double seconds) => Add(WTimeSpan.FromSeconds(seconds));

        #endregion Aritmetika s wraparoundem (vyžadují WWorld.Configure)

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud oba časy reprezentují stejný okamžik dne.</summary>
        public static bool operator ==(WTimeOnly a, WTimeOnly b) => a.TicksOfDay == b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud časy reprezentují různé okamžiky dne.</summary>
        public static bool operator !=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay != b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WTimeOnly a, WTimeOnly b) => a.TicksOfDay < b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejnou chvíli jako <paramref name="b"/>.</summary>
        public static bool operator <=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay <= b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po <paramref name="b"/>.</summary>
        public static bool operator >(WTimeOnly a, WTimeOnly b) => a.TicksOfDay > b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> po nebo ve stejnou chvíli jako <paramref name="b"/>.</summary>
        public static bool operator >=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay >= b.TicksOfDay;

        #endregion Porovnávací operátory

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WTimeOnly other) => TicksOfDay.CompareTo(other.TicksOfDay);

        /// <inheritdoc/>
        public bool Equals(WTimeOnly other) => TicksOfDay == other.TicksOfDay;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WTimeOnly t && Equals(t);

        /// <inheritdoc/>
        public override int GetHashCode() => TicksOfDay.GetHashCode();

        #endregion Rovnost a hashování

        #region Formátování

        /// <summary>
        /// Vrátí čas dne jako čitelný řetězec ve formátu <c>HH:MM:SS</c>.
        /// Vyžaduje nakonfigurovaný <see cref="WWorld"/>. Fallback na TicksOfDay pokud není.
        /// </summary>
        public override string ToString()
        {
            if (!WWorld.IsConfigured) return TicksOfDay.ToString();

            var spec = WWorld.Spec;
            long rem = TicksOfDay;
            int hh = (int)(rem / spec.TicksPerHour); rem %= spec.TicksPerHour;
            int mm = (int)(rem / spec.TicksPerMinute); rem %= spec.TicksPerMinute;
            int ss = (int)(rem / spec.TicksPerSecond);

            return $"{hh:00}:{mm:00}:{ss:00}";
        }

        #endregion Formátování
    }
}
