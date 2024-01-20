namespace Grillisoft.Tools.DatabaseDeploy.Tests;

public static class Extensions
{
    public static DateTimeOffset Trim(this DateTimeOffset date, long ticks)
    {
        return new DateTime(date.Ticks - (date.Ticks % ticks));
    }
    
    public static DateTimeOffset TrimToSeconds(this DateTimeOffset date)
    {
        return date.Trim(TimeSpan.TicksPerSecond);
    }
}