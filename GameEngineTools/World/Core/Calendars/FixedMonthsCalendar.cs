// FixedMonthsCalendar.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Calendars
{
    /// <summary>
    /// An <see cref="IWorldCalendar"/> with a fixed list of month lengths and an optional per-year
    /// number of extra "epagomenal" leap days appended to the final month.
    /// </summary>
    public sealed class FixedMonthsCalendar : IWorldCalendar
    {
        private readonly Func<int, int> _leapExtraDays;
        private readonly int[] _months;                // např. {36,36,36,36,36,36,36,36,36,36} (10×36)
                                                       // vrací počet přestupných "epagomenálních" dní v daném roce

        /// <summary>Creates a calendar from fixed month lengths and a leap-day function.</summary>
        /// <param name="months">Length of each month (the array length is the months-per-year).</param>
        /// <param name="leapExtraDays">Returns the number of extra days appended to the last month for a given year.</param>
        public FixedMonthsCalendar(int[] months, Func<int, int> leapExtraDays)
        {
            _months = (int[])months.Clone();
            _leapExtraDays = leapExtraDays;
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
            if (month == _months.Length)
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
