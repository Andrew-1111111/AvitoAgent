using AvitoAgent.Core;

namespace AvitoAgent.Tests.Core;

public sealed class SleepScheduleTests
{
    [Theory]
    [InlineData(null, 7, false)]
    [InlineData(22, null, false)]
    [InlineData(22, 22, false)]
    [InlineData(-1, 7, false)]
    [InlineData(22, 24, false)]
    [InlineData(22, 7, true)]
    [InlineData(1, 8, true)]
    public void IsConfigured(int? from, int? to, bool expected)
    {
        Assert.Equal(expected, SleepSchedule.IsConfigured(from, to));
    }

    [Theory]
    [InlineData(1, 8, 3, true)]
    [InlineData(1, 8, 8, false)]
    [InlineData(1, 8, 0, false)]
    [InlineData(23, 7, 23, true)]
    [InlineData(23, 7, 2, true)]
    [InlineData(23, 7, 7, false)]
    [InlineData(23, 7, 12, false)]
    public void IsInWindow(int from, int to, int hour, bool expected)
    {
        Assert.Equal(expected, SleepSchedule.IsInWindow(hour, from, to));
    }

    [Fact]
    public void TryGetActiveUntil_returns_boundary_when_in_window()
    {
        var tz = TimeZoneInfo.CreateCustomTimeZone("MSK-test", TimeSpan.FromHours(3), "MSK", "MSK");
        // 2026-03-10 01:30 MSK = 2026-03-09 22:30 UTC
        var utc = new DateTime(2026, 3, 9, 22, 30, 0, DateTimeKind.Utc);

        Assert.True(SleepSchedule.TryGetActiveUntil(23, 7, tz, utc, out var until));
        Assert.Equal(new DateTime(2026, 3, 10, 7, 0, 0), until);
    }

    [Fact]
    public void TryGetActiveUntil_false_outside_window()
    {
        var tz = TimeZoneInfo.CreateCustomTimeZone("MSK-test", TimeSpan.FromHours(3), "MSK", "MSK");
        var utc = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc); // 13:00 MSK
        Assert.False(SleepSchedule.TryGetActiveUntil(23, 7, tz, utc, out _));
    }
}
