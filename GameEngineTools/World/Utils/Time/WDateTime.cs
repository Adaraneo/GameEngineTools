// WDateTime.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Reprezentuje konkrétní okamžik v herním světě, uložený jako počet worldTicks
    /// od světové epochy (1. den 1. měsíce 1. roku, 00:00:00).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Čistý datový typ.</b> Struct neobsahuje žádnou závislost na <c>WorldTimeSpec</c>,
    /// globální stav ani statické mutovatelné properties.
    /// Jediným zdrojem pravdy je <see cref="WorldTicks"/>.
    /// </para>
    /// <para>
    /// Veškeré operace vyžadující znalost kalendáře nebo časové soustavy
    /// (rozklad na složky, přičítání hodin/dní/měsíců, parsování, formátování,
    /// aktuální čas) patří do
    /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
    /// </para>
    /// <para>
    /// Příklady:
    /// <code>
    /// // Vytvoření
    /// var now  = _wtctx.Now();
    /// var dt   = _wtctx.Create(1322, 7, 4, hour: 6);
    ///
    /// // Čistá matematika přímo na strukturách
    /// var later   = now + _wtctx.Hours(2);
    /// var diff    = later - now;          // WTimeSpan
    /// bool isPast = dt &lt; now;
    ///
    /// // Operace závislé na kalendáři přes context
    /// var (y, mo, d, h, m, s, _) = _wtctx.GetParts(now);
    /// string label                = _wtctx.Format(now);
    /// </code>
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(WDateTimeJsonConverter))]
    public readonly struct WDateTime :
        IEquatable<WDateTime>, IComparable<WDateTime>
    {
        #region Konstrukce

        /// <summary>
        /// Inicializuje nový okamžik z přesného počtu worldTicks od světové epochy.
        /// </summary>
        /// <param name="worldTicks">
        /// Počet worldTicks od světové epochy (0 = 1/1/1 00:00:00).
        /// Záporná hodnota by reprezentovala čas před epochou — není podporována.
        /// </param>
        public WDateTime(long worldTicks) => WorldTicks = worldTicks;

        #endregion

        #region Vlastnosti

        /// <summary>
        /// Počet worldTicks od světové epochy (1/1/1 00:00:00).
        /// Jediný zdroj pravdy — veškeré ostatní hodnoty se dopočítávají přes
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext"/>.
        /// </summary>
        public long WorldTicks { get; }

        #endregion

        #region Konstanty

        /// <summary>
        /// Minimální reprezentovatelná hodnota — světová epocha (1/1/1 00:00:00).
        /// </summary>
        public static WDateTime MinValue => new(0);

        #endregion

        #region Aritmetika (čistá matematika)

        /// <summary>
        /// Vrátí rozdíl dvou okamžiků jako <see cref="WTimeSpan"/>.
        /// Ekvivalentní operátoru <c>a - b</c>.
        /// </summary>
        /// <param name="a">Pozdější okamžik.</param>
        /// <param name="b">Dřívější okamžik.</param>
        /// <returns>
        /// Interval <c>a - b</c>. Záporný pokud je <paramref name="a"/> dříve než <paramref name="b"/>.
        /// </returns>
        public static WTimeSpan Difference(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Posune okamžik o zadaný interval dopředu.</summary>
        public static WDateTime operator +(WDateTime t, WTimeSpan d) => new(t.WorldTicks + d.Ticks);

        /// <inheritdoc cref="operator +(WDateTime, WTimeSpan)"/>
        public static WDateTime operator +(WTimeSpan d, WDateTime t) => new(t.WorldTicks + d.Ticks);

        /// <summary>Posune okamžik o zadaný počet worldTicks dopředu.</summary>
        public static WDateTime operator +(WDateTime t, long ticks) => new(t.WorldTicks + ticks);

        /// <inheritdoc cref="operator +(WDateTime, long)"/>
        public static WDateTime operator +(long ticks, WDateTime t) => new(t.WorldTicks + ticks);

        /// <summary>Posune okamžik o zadaný interval dozadu.</summary>
        public static WDateTime operator -(WDateTime t, WTimeSpan d) => new(t.WorldTicks - d.Ticks);

        /// <summary>Posune okamžik o zadaný počet worldTicks dozadu.</summary>
        public static WDateTime operator -(WDateTime t, long ticks) => new(t.WorldTicks - ticks);

        /// <summary>
        /// Vrátí rozdíl dvou okamžiků jako <see cref="WTimeSpan"/>.
        /// Záporný pokud je <paramref name="a"/> dříve než <paramref name="b"/>.
        /// </summary>
        public static WTimeSpan operator -(WDateTime a, WDateTime b) => new(a.WorldTicks - b.WorldTicks);

        /// <summary>Posune okamžik o jeden worldTick dopředu.</summary>
        public static WDateTime operator ++(WDateTime t) => new(t.WorldTicks + 1);

        /// <summary>Posune okamžik o jeden worldTick dozadu.</summary>
        public static WDateTime operator --(WDateTime t) => new(t.WorldTicks - 1);

        #endregion

        #region Porovnávací operátory

        /// <summary>Vrátí <c>true</c> pokud oba okamžiky nastávají ve stejný worldTick.</summary>
        public static bool operator ==(WDateTime a, WDateTime b) => a.WorldTicks == b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud okamžiky nastávají v různý worldTick.</summary>
        public static bool operator !=(WDateTime a, WDateTime b) => a.WorldTicks != b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve než <paramref name="b"/>.</summary>
        public static bool operator <(WDateTime a, WDateTime b) => a.WorldTicks < b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> dříve nebo ve stejný okamžik jako <paramref name="b"/>.</summary>
        public static bool operator <=(WDateTime a, WDateTime b) => a.WorldTicks <= b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později než <paramref name="b"/>.</summary>
        public static bool operator >(WDateTime a, WDateTime b) => a.WorldTicks > b.WorldTicks;

        /// <summary>Vrátí <c>true</c> pokud je <paramref name="a"/> později nebo ve stejný okamžik jako <paramref name="b"/>.</summary>
        public static bool operator >=(WDateTime a, WDateTime b) => a.WorldTicks >= b.WorldTicks;

        #endregion

        #region Rovnost a hashování

        /// <inheritdoc/>
        public int CompareTo(WDateTime other) => WorldTicks.CompareTo(other.WorldTicks);

        /// <inheritdoc/>
        public bool Equals(WDateTime other) => WorldTicks == other.WorldTicks;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WDateTime d && Equals(d);

        /// <inheritdoc/>
        public override int GetHashCode() => WorldTicks.GetHashCode();

        #endregion

        #region Formátování

        /// <summary>
        /// Vrátí raw <see cref="WorldTicks"/> jako řetězec.
        /// <para>
        /// Pro čitelný formát (např. <c>1322-07-04T06:30:00</c>) použij
        /// <see cref="GameEngineTools.World.Core.Time.WorldTimeContext.Format(WDateTime)"/>.
        /// </para>
        /// </summary>
        public override string ToString() => WorldTicks.ToString();

        #endregion
    }
}
