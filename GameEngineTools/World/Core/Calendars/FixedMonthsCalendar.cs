namespace GameEngineTools.World.Core.Calendars
{
    public sealed class FixedMonthsCalendar : IWorldCalendar
    {
        private readonly Func<int, int> _leapExtraDays;
        private readonly int[] _months;                // např. {36,36,36,36,36,36,36,36,36,36} (10×36)
                                                       // vrací počet přestupných "epagomenálních" dní v daném roce

        public FixedMonthsCalendar(int[] months, Func<int, int> leapExtraDays)
        {
            _months = (int[])months.Clone();
            _leapExtraDays = leapExtraDays;
        }

        public (int year, int month, int day) DateFromDays(long days)
        {
            if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
            int year = 1;
            while (true)
            {
                long diy = DaysInYear(year);
                if (days < diy) break;
                days -= diy; year++;
            }
            int month = 1;
            while (true)
            {
                int dim = DaysInMonth(year, month);
                if (days < dim) break;
                days -= dim; month++;
            }
            int day = (int)days + 1;
            return (year, month, day);
        }

        public long DaysFromDate(int year, int month, int day)
        {
            if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
            int dim = DaysInMonth(year, month);
            if (day < 1 || day > dim) throw new ArgumentOutOfRangeException(nameof(day));

            long days = 0;
            for (int y = 1; y < year; y++) days += DaysInYear(y);
            for (int m = 1; m < month; m++) days += DaysInMonth(year, m);
            days += (day - 1);
            return days; // 0 = 1/1/1
        }

        public int DaysInMonth(int year, int month)
        {
            if (month < 1 || month > _months.Length) throw new ArgumentOutOfRangeException(nameof(month));
            return _months[month - 1];
        }

        public long DaysInYear(int year)
            => _months.AsSpan().ToArray().Sum() + _leapExtraDays(year);

        public bool IsLeapYear(int y) => _leapExtraDays(y) > 0;

        public int MonthsInYear(int year) => _months.Length;
    }
}
