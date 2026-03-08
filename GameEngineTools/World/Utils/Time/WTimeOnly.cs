// WTimeOnly.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje čas dne bez datové složky, uložený jako počet worldTicks
    /// od začátku dne (půlnoci).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ.</b> Struct neobsahuje žádnou závislost na <c>WorldTimeSpec</c>
    /// ani jiném externím stavu. Jediným zdrojem pravdy je <see cref="TicksOfDay"/>.
    /// </para>
    /// <para>
    /// Operace vyžadující znalost časové soustavy (rozklad na hodiny/minuty/sekundy,
    /// přičítání hodin, parsování, formátování, wrap přes půlnoc) patří do
    /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Vytvoření
    /// var time = _wtctx.CreateTime(hour: 6, minute: 30, second: 0);
    ///
    /// // Čistá matematika přímo na strukturách
    /// var diff     = timeA.Diff(timeB);   // WTimeSpan (bez wrapu)
    /// bool isEarly = time &lt; _wtctx.CreateTime(8, 0, 0);
    ///
    /// // Operace závislé na spec přes context
    /// var later        = _wtctx.AddTime(time, _wtctx.Hours(2));
    /// var (h, m, s, _) = _wtctx.GetTimeParts(time);
    /// string label     = _wtctx.Format(time);
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
        /// Počet worldTicks od začátku dne (0 = půlnoc).
        /// Musí být v rozsahu [0, TicksPerDay).
        /// Rozsah je validován v <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.CreateTime"/>.
        /// </param>
        /// <remarks>
        /// Konstruktor sám o sobě nevaliduje rozsah vůči <c>TicksPerDay</c>,
        /// protože by potřeboval <c>WorldTimeSpec</c>. Validaci zajišťuje
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.CreateTime"/>.
        /// </remarks>
        public WTimeOnly(long ticksOfDay) => TicksOfDay = ticksOfDay;

        #endregion

        #region Vlastnosti

        /// <summary>
        /// Počet worldTicks od začátku dne (půlnoci).
        /// Platný rozsah je [0, TicksPerDay) kde TicksPerDay závisí na <c>WorldTimeSpec</c>.
        /// Jediný zdroj pravdy — veškeré ostatní hodnoty se dopočítávají přes
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
        /// </summary>
        public long TicksOfDay { get; }

        #endregion

        #region Aritmetika (čistá matematika)

        /// <summary>
        /// Vrátí hrubý rozdíl dvou časů dne jako <see cref="WTimeSpan"/>.
        /// </summary>
        /// <param name="other">Čas dne, od kterého se odečítá.</param>
        /// <returns>
        /// <c>this.TicksOfDay - other.TicksOfDay</c> jako interval.
        /// Výsledek může být záporný pokud je <paramref name="other"/> později.
        /// </returns>
        /// <remarks>
        /// <b>Pozor:</b> tato metoda neprovádí wrap přes půlnoc.
        /// Pokud potřebuješ nejkratší vzdálenost mezi dvěma časy v rámci dne
        /// (s wraparoundem), použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.TimeDiff"/>.
        /// </remarks>
        public WTimeSpan Diff(WTimeOnly other) => new(TicksOfDay - other.TicksOfDay);

        #endregion

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud oba časy reprezentují stejný okamžik dne.</summary>
        public static bool operator ==(WTimeOnly a, WTimeOnly b) => a.TicksOfDay == b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud časy reprezentují různé okamžiky dne.</summary>
        public static bool operator !=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay != b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WTimeOnly a, WTimeOnly b) => a.TicksOfDay < b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejnou chvíli jako <paramref name="b"/>.</summary>
        public static bool operator <=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay <= b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později než <paramref name="b"/>.</summary>
        public static bool operator >(WTimeOnly a, WTimeOnly b) => a.TicksOfDay > b.TicksOfDay;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později nebo ve stejnou chvíli jako <paramref name="b"/>.</summary>
        public static bool operator >=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay >= b.TicksOfDay;

        #endregion

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WTimeOnly other) => TicksOfDay.CompareTo(other.TicksOfDay);

        /// <inheritdoc/>
        public bool Equals(WTimeOnly other) => TicksOfDay == other.TicksOfDay;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WTimeOnly t && Equals(t);

        /// <inheritdoc/>
        public override int GetHashCode() => TicksOfDay.GetHashCode();

        #endregion

        #region Formátování

        /// <summary>
        /// Vrátí raw <see cref="TicksOfDay"/> jako řetězec.
        /// <para>
        /// Pro čitelný formát (např. <c>06:30:00</c>) použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.Format(WTimeOnly)"/>.
        /// </para>
        /// </summary>
        public override string ToString() => TicksOfDay.ToString();

        #endregion
    }
}
