namespace Diaphane.Privacy;

/// <summary>How far back a "clear now" request reaches.</summary>
public enum ClearTimeRange
{
    LastHour,
    LastDay,
    LastWeek,
    FourWeeks,
    Everything,
}

public static class ClearTimeRangeExtensions
{
    /// <summary>The cut-off instant, or <c>null</c> for "everything".</summary>
    public static DateTimeOffset? Since(this ClearTimeRange range, DateTimeOffset now) => range switch
    {
        ClearTimeRange.LastHour  => now.AddHours(-1),
        ClearTimeRange.LastDay   => now.AddDays(-1),
        ClearTimeRange.LastWeek  => now.AddDays(-7),
        ClearTimeRange.FourWeeks => now.AddDays(-28),
        ClearTimeRange.Everything => null,
        _ => null,
    };

    public static string Label(this ClearTimeRange range) => range switch
    {
        ClearTimeRange.LastHour  => "Last hour",
        ClearTimeRange.LastDay   => "Last 24 hours",
        ClearTimeRange.LastWeek  => "Last 7 days",
        ClearTimeRange.FourWeeks => "Last 4 weeks",
        ClearTimeRange.Everything => "All time",
        _ => range.ToString(),
    };
}
