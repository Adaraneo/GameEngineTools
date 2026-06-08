// WTimeSpan.cs
// Copyright (c) 50PSoftware

using System.Text.Json.Serialization;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Represents a time interval in units of <c>worldTicks</c> — the same unit as
    /// <see cref="WDateTime.WorldTicks"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure data type + ambient properties.</b>
    /// The single source of truth is <see cref="Ticks"/>. Properties such as
    /// <see cref="TotalHours"/>, <see cref="TotalDays"/> a factory metody jako
    /// <see cref="FromHours"/> require <see cref="GameEngineTools.World.Core.Time.WWorld"/> to be configured.
    /// </para>
    /// <para>
    /// The interval may be negative — expressing a backward shift in time.
    /// </para>
    /// <para>
    /// Examples:
    /// <code>
    /// // Factory from human units (requires WWorld.Configure)
    /// var twoHours  = WTimeSpan.FromHours(2);
    /// var halfDay   = WTimeSpan.FromHours(13);    // half a day in a 26h world
    /// var threeWeeks = WTimeSpan.FromDays(21);
    ///
    /// // Properties (require WWorld.Configure)
    /// double h = twoHours.TotalHours;             // 2.0
    /// double d = threeWeeks.TotalDays;             // 21.0
    ///
    /// // Pure math — does not require WWorld
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
        /// Initializes a new interval with an exact number of <c>worldTicks</c>.
        /// </summary>
        /// <param name="ticks">
        /// Number of world ticks. A negative value represents a backward shift in time.
        /// </param>
        public WTimeSpan(long ticks) => Ticks = ticks;

        #endregion Konstrukce

        #region Vlastnosti — raw data

        /// <summary>
        /// Number of world ticks represented by this interval.
        /// The single source of truth — all other values are derived
        /// z <see cref="GameEngineTools.World.Core.Time.WWorld.Spec"/>.
        /// </summary>
        public long Ticks { get; }

        #endregion Vlastnosti — raw data

        #region Konstanty

        /// <summary>An interval of zero length.</summary>
        public static WTimeSpan Zero => new(0);

        #endregion Konstanty

        #region Ambient vlastnosti — konverze na lidské jednotky

        // These properties require WWorld.Configure. The result may be fractional and negative.

        /// <summary>
        /// Total number of world seconds in this interval.
        /// May be fractional and negative.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public double TotalSeconds => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerSecond;

        /// <summary>
        /// Total number of world minutes in this interval.
        /// May be fractional and negative.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public double TotalMinutes => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerMinute;

        /// <summary>
        /// Total number of world hours in this interval.
        /// May be fractional and negative.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public double TotalHours => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerHour;

        /// <summary>
        /// Total number of world days in this interval.
        /// May be fractional and negative.
        /// </summary>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public double TotalDays => (double)Ticks / GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerDay;

        #endregion Ambient vlastnosti — konverze na lidské jednotky

        #region Static factory — z tiků

        /// <summary>
        /// Creates an interval directly from a number of world ticks.
        /// Semantically equivalent to the constructor — for more readable call sites.
        /// </summary>
        /// <param name="ticks">Number of world ticks.</param>
        public static WTimeSpan FromTicks(long ticks) => new(ticks);

        #endregion Static factory — z tiků

        #region Static factory — z lidských jednotek (vyžadují WWorld.Configure)

        /// <summary>
        /// Creates an interval corresponding to the given number of world seconds.
        /// </summary>
        /// <param name="seconds">Number of seconds (may be fractional).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WTimeSpan FromSeconds(double seconds)
            => new((long)(seconds * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerSecond));

        /// <summary>
        /// Creates an interval corresponding to the given number of world minutes.
        /// </summary>
        /// <param name="minutes">Number of minutes (may be fractional).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WTimeSpan FromMinutes(double minutes)
            => new((long)(minutes * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerMinute));

        /// <summary>
        /// Creates an interval corresponding to the given number of world hours.
        /// </summary>
        /// <param name="hours">Number of hours (may be fractional).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WTimeSpan FromHours(double hours)
            => new((long)(hours * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerHour));

        /// <summary>
        /// Creates an interval corresponding to the given number of world days.
        /// </summary>
        /// <param name="days">Number of days (may be fractional).</param>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public static WTimeSpan FromDays(double days)
            => new((long)(days * GameEngineTools.World.Core.Time.WWorld.Spec.TicksPerDay));

        #endregion Static factory — z lidských jednotek (vyžadují WWorld.Configure)

        #region Utility (čistá matematika)

        /// <summary>
        /// Returns the absolute value of the interval.
        /// If the interval is positive or zero, returns it unchanged.
        /// </summary>
        public static WTimeSpan Abs(WTimeSpan x) => x.Ticks >= 0 ? x : new(-x.Ticks);

        /// <summary>
        /// Returns the sign of the interval:
        /// <c>1</c> for positive, <c>-1</c> for negative, <c>0</c> for zero.
        /// </summary>
        public static int Sign(WTimeSpan x) => x.Ticks == 0 ? 0 : (x.Ticks > 0 ? 1 : -1);

        /// <summary>Returns the longer of two intervals.</summary>
        public static WTimeSpan Max(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks ? a : b;

        /// <summary>Returns the shorter of two intervals.</summary>
        public static WTimeSpan Min(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks ? a : b;

        /// <summary>
        /// Clamps the interval to the range [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        /// <exception cref="ArgumentException">If min is greater than max.</exception>
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

        /// <summary>Sum of two intervals.</summary>
        public static WTimeSpan operator +(WTimeSpan a, WTimeSpan b) => new(a.Ticks + b.Ticks);

        /// <summary>Difference of two intervals.</summary>
        public static WTimeSpan operator -(WTimeSpan a, WTimeSpan b) => new(a.Ticks - b.Ticks);

        /// <summary>Negation of the interval — reverses the direction of the time shift.</summary>
        public static WTimeSpan operator -(WTimeSpan a) => new(-a.Ticks);

        /// <summary>Scales the interval by the factor <paramref name="k"/>.</summary>
        /// <exception cref="OverflowException">If the result overflows <c>long</c>.</exception>
        public static WTimeSpan operator *(WTimeSpan a, double k) => new(checked((long)(a.Ticks * k)));

        /// <inheritdoc cref="operator *(WTimeSpan, double)"/>
        public static WTimeSpan operator *(double k, WTimeSpan a) => new(checked((long)(a.Ticks * k)));

        /// <summary>Divides the interval by the factor <paramref name="k"/>.</summary>
        /// <exception cref="OverflowException">If the result overflows <c>long</c>.</exception>
        public static WTimeSpan operator /(WTimeSpan a, double k) => new(checked((long)(a.Ticks / k)));

        /// <summary>
        /// Ratio of two intervals — returns a dimensionless <c>double</c>.
        /// Useful for computing the percentage of elapsed time.
        /// </summary>
        /// <exception cref="DivideByZeroException">If <paramref name="b"/> is zero.</exception>
        public static double operator /(WTimeSpan a, WTimeSpan b)
        {
            if (b.Ticks == 0) throw new DivideByZeroException();
            return (double)a.Ticks / b.Ticks;
        }

        #endregion Aritmetické operátory

        #region Porovnávací operátory

        /// <summary>Returns <c>true</c> if both intervals are equally long.</summary>
        public static bool operator ==(WTimeSpan a, WTimeSpan b) => a.Ticks == b.Ticks;

        /// <summary>Returns <c>true</c> if the intervals differ.</summary>
        public static bool operator !=(WTimeSpan a, WTimeSpan b) => a.Ticks != b.Ticks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is shorter than <paramref name="b"/>.</summary>
        public static bool operator <(WTimeSpan a, WTimeSpan b) => a.Ticks < b.Ticks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is shorter than or equal in length to <paramref name="b"/>.</summary>
        public static bool operator <=(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is longer than <paramref name="b"/>.</summary>
        public static bool operator >(WTimeSpan a, WTimeSpan b) => a.Ticks > b.Ticks;

        /// <summary>Returns <c>true</c> if <paramref name="a"/> is longer than or equal in length to <paramref name="b"/>.</summary>
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
        /// Returns the interval as a readable string in the format <c>[-]d.hh:mm:ss</c> or <c>[-]hh:mm:ss</c>.
        /// Requires <see cref="GameEngineTools.World.Core.Time.WWorld"/> to be configured.
        /// Falls back to raw ticks if WWorld is not configured.
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
