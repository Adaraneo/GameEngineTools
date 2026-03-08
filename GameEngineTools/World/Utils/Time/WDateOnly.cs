// WDateOnly.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje datum bez časové složky, uložené jako počet dní od světové epochy
    /// (0 = 1. den 1. měsíce 1. roku).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ.</b> Struct neobsahuje žádnou závislost na <c>WorldTimeSpec</c>
    /// ani jiném externím stavu. Jediným zdrojem pravdy je <see cref="DayIndex"/>.
    /// </para>
    /// <para>
    /// Operace vyžadující znalost kalendáře (rozklad na rok/měsíc/den, přičítání měsíců,
    /// parsování, formátování) patří do
    /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Vytvoření
    /// var date = _wtctx.CreateDate(1322, 7, 4);
    ///
    /// // Čistá matematika přímo na strukturách
    /// var tomorrow   = date.AddDays(1);
    /// var daysLeft   = date.DaysUntil(deadline);
    /// bool isPast    = date &lt; today;
    ///
    /// // Operace závislé na kalendáři přes context
    /// var nextMonth  = _wtctx.AddMonths(date, 1);
    /// var (y, m, d)  = _wtctx.GetDateParts(date);
    /// string label   = _wtctx.Format(date);
    /// </code>
    /// </para>
    /// </remarks>
    public readonly struct WDateOnly :
        IEquatable<WDateOnly>, IComparable<WDateOnly>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nové datum z 0-based indexu dne od světové epochy.
        /// </summary>
        /// <param name="dayIndex">
        /// Počet dní od světové epochy (0 = 1/1/1). Nesmí být záporný.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Pokud je <paramref name="dayIndex"/> záporný.
        /// </exception>
        [JsonConstructor]
        public WDateOnly(long dayIndex)
        {
            if (dayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dayIndex));
            }

            DayIndex = dayIndex;
        }

        #endregion

        #region Vlastnosti

        /// <summary>
        /// Počet dní od světové epochy (0-based).
        /// Jediný zdroj pravdy — veškeré ostatní hodnoty se dopočítávají přes
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
        /// </summary>
        public long DayIndex { get; }

        #endregion

        #region Aritmetika (čistá matematika)

        /// <summary>
        /// Přičte zadaný počet dní k datu.
        /// Čistá matematika — nevyžaduje <c>WorldTimeSpec</c>.
        /// </summary>
        /// <param name="days">Počet dní (může být záporný pro posun zpět).</param>
        /// <returns>Nové datum posunuté o <paramref name="days"/> dní.</returns>
        /// <exception cref="OverflowException">Pokud výsledek přeteče <c>long</c>.</exception>
        public WDateOnly AddDays(long days) => new(checked(DayIndex + days));

        /// <summary>
        /// Vrátí počet dní mezi tímto datem a <paramref name="other"/>.
        /// Kladný výsledek znamená, že <paramref name="other"/> je v budoucnosti.
        /// </summary>
        /// <param name="other">Cílové datum.</param>
        /// <returns>
        /// Počet dní jako <c>other.DayIndex - this.DayIndex</c>.
        /// Může být záporný pokud je <paramref name="other"/> v minulosti.
        /// </returns>
        public long DaysUntil(WDateOnly other) => other.DayIndex - DayIndex;

        #endregion

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud obě data reprezentují stejný den.</summary>
        public static bool operator ==(WDateOnly a, WDateOnly b) => a.DayIndex == b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud data reprezentují různé dny.</summary>
        public static bool operator !=(WDateOnly a, WDateOnly b) => a.DayIndex != b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WDateOnly a, WDateOnly b) => a.DayIndex < b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejný den jako <paramref name="b"/>.</summary>
        public static bool operator <=(WDateOnly a, WDateOnly b) => a.DayIndex <= b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později než <paramref name="b"/>.</summary>
        public static bool operator >(WDateOnly a, WDateOnly b) => a.DayIndex > b.DayIndex;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později nebo ve stejný den jako <paramref name="b"/>.</summary>
        public static bool operator >=(WDateOnly a, WDateOnly b) => a.DayIndex >= b.DayIndex;

        #endregion

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WDateOnly other) => DayIndex.CompareTo(other.DayIndex);

        /// <inheritdoc/>
        public bool Equals(WDateOnly other) => DayIndex == other.DayIndex;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WDateOnly d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => DayIndex.GetHashCode();

        #endregion

        #region Formátování

        /// <summary>
        /// Vrátí raw <see cref="DayIndex"/> jako řetězec.
        /// <para>
        /// Pro čitelný formát (např. <c>1322-07-04</c>) použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.Format(WDateOnly)"/>.
        /// </para>
        /// </summary>
        public override string ToString() => DayIndex.ToString();

        #endregion
    }
}
