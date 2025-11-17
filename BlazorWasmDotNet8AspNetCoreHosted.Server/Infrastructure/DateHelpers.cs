namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;

public static class DateHelpers
{
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
