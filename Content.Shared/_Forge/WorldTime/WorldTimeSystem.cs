using System.Globalization;
using Content.Shared._NC14.DayNightCycle;
using Content.Shared.Examine;

namespace Content.Shared._Forge.WorldTime;

/// <summary>
/// Exposes the main map's day/night phase as a round-wide time of day.
/// </summary>
public sealed class WorldTimeSystem : EntitySystem
{
    private const int SecondsPerDay = 24 * 60 * 60;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WorldClockComponent, ExaminedEvent>(OnClockExamined);
    }

    private void OnClockExamined(Entity<WorldClockComponent> _, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetTimeOfDay(out var timeOfDay))
        {
            args.PushMarkup(Loc.GetString("world-clock-examine-unavailable"));
            return;
        }

        args.PushMarkup(Loc.GetString("world-clock-examine-time",
            ("time", FormatTimeOfDay(timeOfDay))));
    }

    /// <summary>
    /// Tries to read the time of day from the map marked as the round's source.
    /// </summary>
    public bool TryGetTimeOfDay(out TimeSpan timeOfDay)
    {
        var query = EntityQueryEnumerator<WorldTimeSourceComponent, DayNightCycleComponent>();
        if (!query.MoveNext(out _, out _, out var dayNight))
        {
            timeOfDay = default;
            return false;
        }

        timeOfDay = CyclePhaseToTimeOfDay(dayNight.CurrentCycleTime);
        return true;
    }

    /// <summary>
    /// Converts a normalized day/night phase into a time within a 24-hour day.
    /// Values outside 0..1 wrap around in the same way as the day/night cycle.
    /// </summary>
    public static TimeSpan CyclePhaseToTimeOfDay(float phase)
    {
        if (!float.IsFinite(phase))
            return TimeSpan.Zero;

        var normalized = phase - MathF.Floor(phase);
        var totalSeconds = (int) MathF.Floor(normalized * SecondsPerDay);
        totalSeconds = Math.Clamp(totalSeconds, 0, SecondsPerDay - 1);

        return TimeSpan.FromSeconds(totalSeconds);
    }

    /// <summary>
    /// Formats a time within a day using the 24-hour clock.
    /// </summary>
    public static string FormatTimeOfDay(TimeSpan timeOfDay)
    {
        return timeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }
}
