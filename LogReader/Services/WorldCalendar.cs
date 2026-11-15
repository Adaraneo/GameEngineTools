// Services/WorldCalendar.cs — world calendar derived from the engine's appsettings.World.json.
using GameEngineTools.Config;
using GameEngineTools.World.Core.Astro;
using GameEngineTools.World.Core.Time;
using GameEngineTools.Universe;

namespace LogReader.Services;

/// <summary>Planet name and calendar the simulation ran with; null fields when no world file is configured.</summary>
public sealed class WorldCalendar
{
    public string? Planet { get; private set; }
    public string? PlanetName { get; private set; }
    public WorldTimeSpec? Spec { get; private set; }
    /// <summary>Detail stays server-side (log); the API only reports that the calendar is unavailable.</summary>
    public string? Error { get; private set; }

    public static WorldCalendar Load(string? path, ILogger log)
    {
        var result = new WorldCalendar();
        if (string.IsNullOrWhiteSpace(path)) return result;
        try
        {
            var cfg = WorldSettings.Load(path);
            var universe = cfg.GetSection("World:Universe").Get<UniverseConfig>() ?? new UniverseConfig();
            var calendar = cfg.GetSection("World:Calendar").Get<CalendarConfig>() ?? new CalendarConfig();
            result.Planet = cfg[WorldSettings.PlanetKey];
            result.PlanetName = universe.PlanetName;
            result.Spec = PlanetaryCalendarFactory.Build(universe.ToPlanetConfig(), universe.ToOrbitalElements(), universe.ToStarPhysics(), calendar.ToCalendarOptions());
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            log.LogWarning("World calendar unavailable ({Path}): {Msg}", path, ex.Message);
        }
        return result;
    }

    /// <summary>Body of /api/config; calMonths/calHours/calName keep the keys the UI already reads.</summary>
    public object ToConfig(string runStamp)
    {
        var cal = Spec?.Calendar;
        var months = cal?.MonthsInYear(1);
        return new
        {
            calMonths = months,
            calHours = Spec?.HoursPerDay,
            calName = PlanetName,
            planet = Planet,
            daysInYear = cal?.DaysInYear(1),
            monthLengths = cal == null ? null : Enumerable.Range(1, months!.Value).Select(m => cal.DaysInMonth(1, m)).ToArray(),
            calendarError = Error != null ? "world calendar unavailable" : null,
            runStamp,
        };
    }
}
