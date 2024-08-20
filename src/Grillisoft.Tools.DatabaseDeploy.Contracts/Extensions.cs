namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public static class Extensions
{
    public static DateTimeOffset Trim(this DateTimeOffset date, long ticks)
    {
        return new DateTime(date.Ticks - (date.Ticks % ticks), DateTimeKind.Utc);
    }

    public static DateTimeOffset TrimToSeconds(this DateTimeOffset date)
    {
        return date.Trim(TimeSpan.TicksPerSecond);
    }
}