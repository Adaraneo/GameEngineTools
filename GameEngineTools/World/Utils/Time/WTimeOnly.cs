// WTimeOnly.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Core.Time;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Represents a time of day without a date component, stored as the number of world ticks since midnight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure data type + ambient properties.</b>
    /// The single source of truth is <see cref="TicksOfDay"/>.
    /// Properties jako <see cref="Hour"/>, <see cref="Minute"/>, <see cref="Second"/>
    /// and methods such as <see cref="AddHours"/> require <see cref="WWorld"/> to be configured.
    /// </para>
    /// <para>
    /// Examples:
    /// <code>
    /// // Factory (requires WWorld.Configure)
    /// var time = WTimeOnly.New(6, 30, 0);
    ///
    /// // Ambient properties (require WWorld.Configure)
    /// int hour = time.Hour;     // 6
    /// int min  = time.Minute;   // 30
    ///
    /// // Pure math — does not require WWorld
    /// var diff  = timeA.Diff(timeB);   // WTimeSpan (bez wrapu)
    /// bool late = time > WTimeOnly.New(22, 0, 0);
    ///
    /// // Arithmetic with wraparound (requires WWorld.Configure)
    /// var later = time.AddHours(3);    // wraps across midnight automatically
    /// </code>
    /// </para>
    /// </remarks>
    public readonly struct WTimeOnly :
        IEquatable<WTimeOnly>, IComparable<WTimeOnly>
    {
        #region Konstrukce

        /// <summary>
        /// Initializes a new time of day from the number of world ticks since midnight.
        /// </summary>
        /// <param name="ticksOfDay">
        /// Number of world ticks since the start of the day (0 = midnight). Must be in the range [0, TicksPerDay).
        /// The range is validated in <see cref="New"/> and <see cref="WWorld"/>-dependent methods.
        /// </param>
        public WTimeOnly(long ticksOfDay) => TicksOfDay = ticksOfDay;

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Number of world ticks since the start of the day (midnight). The single source of truth.
        /// The valid range is [0, TicksPerDay) where TicksPerDay depends on <see cref="WWorld.Spec"/>.
        /// </summary>
        public long TicksOfDay { get; }

        #endregion Vlastnosti — raw data

        #region Ambient vlastnosti — složky času (vyžadují WWorld.Configure)

        /// <summary>Hour of this time of day (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Hour
        {
            get
            {
                var spec = WWorld.Spec;
                return (int)(TicksOfDay / spec.TicksPerHour);
            }
        }

        /// <summary>Minute of this time of day (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public int Minute
        {
            get
            {
                var spec = WWorld.Spec;
                long rem = TicksOfDay % spec.TicksPerHour;
                return (int)(rem / spec.TicksPerMinute);
            }
        }

        /// <summary>Second of this time of day (0-based).</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
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
        /// Creates a time of day from its components (hour, minute, second).
        /// Validates the range against <see cref="WWorld.Spec"/>.
        /// </summary>
        /// <param name="hour">Hodina (0..HoursPerDay-1).</param>
        /// <param name="minute">Minuta (0..MinutesPerHour-1).</param>
        /// <param name="second">Sekunda (0..SecondsPerMinute-1).</param>
        /// <param name="subTick">Sub-ticks below a second (0..TicksPerSecond-1). Default 0.</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If a component is outside the valid range.</exception>
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
        /// Returns the raw difference of two times of day as a <see cref="WTimeSpan"/> (without wrapping across midnight).
        /// </summary>
        /// <remarks>
        /// For the shortest distance with wraparound across midnight, use
        /// <c>WorldTimeContext.TimeDiff</c>.
        /// </remarks>
        public WTimeSpan Diff(WTimeOnly other) => new(TicksOfDay - other.TicksOfDay);

        #endregion Aritmetika — čistá matematika (nevyžaduje WWorld)

        #region Aritmetika s wraparoundem (vyžadují WWorld.Configure)

        /// <summary>
        /// Adds an interval to the time of day with automatic wraparound across midnight.
        /// </summary>
        /// <param name="span">The interval (may be negative).</param>
        /// <returns>The new time of day in the range [0, TicksPerDay).</returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WTimeOnly Add(WTimeSpan span)
        {
            long tpd = WWorld.Spec.TicksPerDay;
            long t = (TicksOfDay + span.Ticks) % tpd;
            if (t < 0) t += tpd;
            return new WTimeOnly(t);
        }

        /// <summary>Adds hours to the time of day with wraparound across midnight.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WTimeOnly AddHours(double hours) => Add(WTimeSpan.FromHours(hours));

        /// <summary>Adds minutes to the time of day with wraparound across midnight.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WTimeOnly AddMinutes(double minutes) => Add(WTimeSpan.FromMinutes(minutes));

        /// <summary>Adds seconds to the time of day with wraparound across midnight.</summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public WTimeOnly AddSeconds(double seconds) => Add(WTimeSpan.FromSeconds(seconds));

        #endregion Aritmetika s wraparoundem (vyžadují WWorld.Configure)

        #region Porovnávací operátory

        /// <summary>Returns <c>true</c> if both times represent the same instant of the day.</summary>
        public static bool operator ==(WTimeOnly a, WTimeOnly b) => a.TicksOfDay == b.TicksOfDay;

        /// <summary>Returns <c>true</c> if the times represent different instants of the day.</summary>
        public static bool operator !=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay != b.TicksOfDay;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than <paramref name="b"/>.</summary>
        public static bool operator <(WTimeOnly a, WTimeOnly b) => a.TicksOfDay < b.TicksOfDay;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is earlier than or at the same moment as <paramref name="b"/>.</summary>
        public static bool operator <=(WTimeOnly a, WTimeOnly b) => a.TicksOfDay <= b.TicksOfDay;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than <paramref name="b"/>.</summary>
        public static bool operator >(WTimeOnly a, WTimeOnly b) => a.TicksOfDay > b.TicksOfDay;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is later than or at the same moment as <paramref name="b"/>.</summary>
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
        /// Returns the time of day as a readable string in the format <c>HH:MM:SS</c>.
        /// Requires <see cref="WWorld"/> to be configured. Falls back to TicksOfDay otherwise.
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
