// FixedMonthsCalendar.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Calendars
{
    /// <summary>
    /// An <see cref="IWorldCalendar"/> with a fixed list of month lengths and an optional per-year
    /// number of extra "epagomenal" leap days added to a chosen month (the last month by default,
    /// or e.g. February for a Gregorian-style calendar).
    /// </summary>
    public sealed class FixedMonthsCalendar : IWorldCalendar
    {
        private readonly Func<int, int> _leapExtraDays;
        private readonly int[] _months;                // e.g. {36,36,36,36,36,36,36,36,36,36} (10×36)
                                                       // returns the number of leap ("epagomenal") days in the given year
        private readonly int _leapMonth;               // 1-based month that receives the leap days

        /// <summary>Creates a calendar from fixed month lengths and a leap-day function.</summary>
        /// <param name="months">Length of each month (the array length is the months-per-year).</param>
        /// <param name="leapExtraDays">Returns the number of extra days added to the leap month for a given year.</param>
        /// <param name="leapMonth">
        /// 1-based month that receives the leap days. <c>null</c> defaults to the last month
        /// (epagomenal style); pass <c>2</c> for a Gregorian February leap day.
        /// </param>
        public FixedMonthsCalendar(int[] months, Func<int, int> leapExtraDays, int? leapMonth = null)
        {
            _months = (int[])months.Clone();
            _leapExtraDays = leapExtraDays;
            _leapMonth = leapMonth ?? _months.Length;

            if (_leapMonth < 1 || _leapMonth > _months.Length)
                throw new ArgumentOutOfRangeException(nameof(leapMonth), _leapMonth,
                    "Leap month must be between 1 and the number of months.");
        }

        /// <inheritdoc/>
        public (int year, int month, int day) DateFromDays(long days)
        {
            if (days < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(days));
            }

            int year = 1;
            while (true)
            {
                long diy = DaysInYear(year);
                if (days < diy)
                {
                    break;
                }

                days -= diy; year++;
            }
            int month = 1;
            while (true)
            {
                int dim = DaysInMonth(year, month);
                if (days < dim)
                {
                    break;
                }

                days -= dim; month++;
            }

            int day = (int)days + 1;
            return (year, month, day);
        }

        /// <inheritdoc/>
        public long DaysFromDate(int year, int month, int day)
        {
            if (year < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            int dim = DaysInMonth(year, month);
            if (day < 1 || day > dim)
            {
                throw new ArgumentOutOfRangeException(nameof(day));
            }

            long days = 0;
            for (int y = 1; y < year; y++)
            {
                days += DaysInYear(y);
            }

            for (int m = 1; m < month; m++)
            {
                days += DaysInMonth(year, m);
            }

            days += (day - 1);
            return days; // 0 = 1/1/1
        }

        /// <inheritdoc/>
        public int DaysInMonth(int year, int month)
        {
            if (month < 1 || month > _months.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            var days = _months[month - 1];
            if (month == _leapMonth)
            {
                days += _leapExtraDays(year);
            }

            return days;
        }

        /// <inheritdoc/>
        public long DaysInYear(int year)
            => _months.AsSpan().ToArray().Sum() + _leapExtraDays(year);

        /// <inheritdoc/>
        public bool IsLeapYear(int y) => _leapExtraDays(y) > 0;

        /// <inheritdoc/>
        public int MonthsInYear(int year) => _months.Length;
    }
}
