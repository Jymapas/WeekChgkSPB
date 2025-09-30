using System;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public class ChannelPostScheduleOptionsTests
{
    [Fact]
    public void ParseFromStrings_ReadsValidValues()
    {
        var options = ChannelPostScheduleOptions.FromStrings("2", "monday, thursday", "12:00");

        Assert.Equal(2, options.PostsPerWeek);
        Assert.Collection(options.Days,
            day => Assert.Equal(DayOfWeek.Monday, day),
            day => Assert.Equal(DayOfWeek.Thursday, day));
        Assert.Equal(new TimeSpan(12, 0, 0), options.TimeOfDay);
    }

    [Fact]
    public void ParseFromStrings_ThrowsWhenDaysMismatch()
    {
        Assert.Throws<ArgumentException>(() => ChannelPostScheduleOptions.FromStrings("3", "monday, thursday", "12:00"));
    }

    [Fact]
    public void ParseFromStrings_ThrowsWhenTimeInvalid()
    {
        Assert.Throws<FormatException>(() => ChannelPostScheduleOptions.FromStrings("2", "monday, thursday", "nope"));
    }
}
