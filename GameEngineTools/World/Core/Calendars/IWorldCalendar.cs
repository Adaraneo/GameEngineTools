// IWorldCalendar.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Calendars
{
    public interface IWorldCalendar
    {

        (int year, int month, int day) DateFromDays(long days);

        // 1-based (year>=1, month>=1, day>=1)
        long DaysFromDate(int year, int month, int day);

        int DaysInMonth(int year, int month);
        int MonthsInYear(int year);

        long DaysInYear(int year);

        bool IsLeapYear(int year);
    }
}
