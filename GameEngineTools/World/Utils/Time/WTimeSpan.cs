using System.Text.Json.Serialization;

namespace GameEngineTools.World.Utils.Time
{
    /// <summary>
    /// Délka/interval v "worldTicks" (stejná jednotka jako WDateTime.WorldTicks).
    /// Bez závislosti na BCL TimeSpan. Bezpečné pro libovolné "spec" (26h den apod.).
    /// </summary>
    [JsonConverter(typeof(WTimeSpanJsonConverter))]
    public readonly struct WTimeSpan :
        IEquatable<WTimeSpan>, IComparable<WTimeSpan>, IFormattable
    {
        public WTimeSpan(long ticks) => Ticks = ticks;

        // ----- Konstanty -----
        public static WTimeSpan Zero => new(0);

        public double AbsTotalDays => Math.Abs(TotalDays);
        public double AbsTotalHours => Math.Abs(TotalHours);
        public double AbsTotalMinutes => Math.Abs(TotalMinutes);

        // absolutní (kladné) verze pro UI/formatování
        public double AbsTotalSeconds => Math.Abs(TotalSeconds);

        public double AbsTotalWeeks => Math.Abs(TotalWeeks);

        // ----- Komponenty pro zobrazení (abs hodnota) -----
        public long DaysComponent
        {
            get
            {
                long at = Math.Abs(Ticks);
                return at / WDateTime.Spec.TicksPerDay;
            }
        }

        public int HoursComponent
        {
            get
            {
                long at = Math.Abs(Ticks) % WDateTime.Spec.TicksPerDay;
                return (int)(at / WDateTime.Spec.TicksPerHour);
            }
        }

        public int MinutesComponent
        {
            get
            {
                long at = Math.Abs(Ticks) % WDateTime.Spec.TicksPerHour;
                return (int)(at / WDateTime.Spec.TicksPerMinute);
            }
        }

        public int SecondsComponent
        {
            get
            {
                long at = Math.Abs(Ticks) % WDateTime.Spec.TicksPerMinute;
                return (int)(at / WDateTime.Spec.TicksPerSecond);
            }
        }

        public long SubTickComponent
        {
            get
            {
                long at = Math.Abs(Ticks) % WDateTime.Spec.TicksPerSecond;
                return at; // zbytek pod "sekundu" v jednotce worldTicku
            }
        }

        public long Ticks { get; }
        public double TotalDays => (double)Ticks / WDateTime.Spec.TicksPerDay;

        public double TotalHours => (double)Ticks / WDateTime.Spec.TicksPerHour;

        public double TotalMinutes => (double)Ticks / WDateTime.Spec.TicksPerMinute;

        // ----- Totals (double) -----
        public double TotalSeconds => (double)Ticks / WDateTime.Spec.TicksPerSecond;

        public double TotalWeeks => TotalDays / 7.0;

        // ----- Utility -----
        public static WTimeSpan Abs(WTimeSpan x) => x.Ticks >= 0 ? x : new WTimeSpan(-x.Ticks);

        public static WTimeSpan FromDays(double d)
                    => new(checked((long)(d * WDateTime.Spec.TicksPerDay)));

        public static WTimeSpan FromHours(double h)
                    => new(checked((long)(h * WDateTime.Spec.TicksPerHour)));

        public static WTimeSpan FromMinutes(double m)
                    => new(checked((long)(m * WDateTime.Spec.TicksPerMinute)));

        public static WTimeSpan FromSeconds(double s)
                    => new(checked((long)(s * WDateTime.Spec.TicksPerSecond)));

        // ----- Factory (používají světové jednotky) -----
        public static WTimeSpan FromTicks(long ticks) => new(ticks);

        public static WTimeSpan Max(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks ? a : b;

        public static WTimeSpan Min(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks ? a : b;

        public static WTimeSpan operator -(WTimeSpan a, WTimeSpan b) => new(a.Ticks - b.Ticks);

        public static WTimeSpan operator -(WTimeSpan a) => new(-a.Ticks);

        public static bool operator !=(WTimeSpan a, WTimeSpan b) => a.Ticks != b.Ticks;

        public static WTimeSpan operator *(WTimeSpan a, double k) => new(checked((long)(a.Ticks * k)));

        public static WTimeSpan operator *(double k, WTimeSpan a) => new(checked((long)(a.Ticks * k)));

        public static WTimeSpan operator /(WTimeSpan a, double k) => new(checked((long)(a.Ticks / k)));

        /// <summary>Poměr dvou intervalů (bez jednotky). Vrací a/b jako double.</summary>
        public static double operator /(WTimeSpan a, WTimeSpan b)
        {
            if (b.Ticks == 0) throw new DivideByZeroException();
            return (double)a.Ticks / b.Ticks;
        }

        // ----- Aritmetika -----
        public static WTimeSpan operator +(WTimeSpan a, WTimeSpan b) => new(a.Ticks + b.Ticks);

        public static bool operator <(WTimeSpan a, WTimeSpan b) => a.Ticks < b.Ticks;

        public static bool operator <=(WTimeSpan a, WTimeSpan b) => a.Ticks <= b.Ticks;

        public static bool operator ==(WTimeSpan a, WTimeSpan b) => a.Ticks == b.Ticks;

        public static bool operator >(WTimeSpan a, WTimeSpan b) => a.Ticks > b.Ticks;

        public static bool operator >=(WTimeSpan a, WTimeSpan b) => a.Ticks >= b.Ticks;

        public static int Sign(WTimeSpan x) => x.Ticks == 0 ? 0 : (x.Ticks > 0 ? 1 : -1);

        public WTimeSpan Clamp(WTimeSpan min, WTimeSpan max)
        {
            if (min.Ticks > max.Ticks) throw new ArgumentException("min > max");
            if (Ticks < min.Ticks) return min;
            if (Ticks > max.Ticks) return max;
            return this;
        }

        // ----- Porovnání -----
        public int CompareTo(WTimeSpan other) => Ticks.CompareTo(other.Ticks);

        public bool Equals(WTimeSpan other) => Ticks == other.Ticks;

        public override bool Equals(object? obj) => obj is WTimeSpan d && Equals(d);

        public override int GetHashCode() => Ticks.GetHashCode();

        // ----- IFormattable / ToString -----
        public override string ToString() => ToString("g", null);

        /// <summary>
        /// "g": [-]d.hh:mm:ss[.sub]  (d se vypouští, pokud je 0; .sub jsou zbytkové worldTick-y pod sekundu)
        /// "t": celkový počet worldTicků (číslo)
        /// </summary>
        public string ToString(string? format, IFormatProvider? _)
        {
            format ??= "g";
            if (format == "t")
                return Ticks.ToString();

            var sign = Ticks < 0 ? "-" : "";
            long at = Math.Abs(Ticks);

            long d = at / WDateTime.Spec.TicksPerDay; at %= WDateTime.Spec.TicksPerDay;
            long hh = at / WDateTime.Spec.TicksPerHour; at %= WDateTime.Spec.TicksPerHour;
            long mm = at / WDateTime.Spec.TicksPerMinute; at %= WDateTime.Spec.TicksPerMinute;
            long ss = at / WDateTime.Spec.TicksPerSecond; at %= WDateTime.Spec.TicksPerSecond;
            long sub = at;

            if (d != 0)
                return sub != 0
                    ? $"{sign}{d}.{hh:00}:{mm:00}:{ss:00}.{sub}"
                    : $"{sign}{d}.{hh:00}:{mm:00}:{ss:00}";
            else
                return sub != 0
                    ? $"{sign}{hh:00}:{mm:00}:{ss:00}.{sub}"
                    : $"{sign}{hh:00}:{mm:00}:{ss:00}";
        }
    }
}