using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WeekChgkSPB.Infrastructure.Notifications;

public sealed class ChannelPostScheduleOptions
{
    private static readonly TimeSpan DefaultTriggerWindow = TimeSpan.FromHours(3);

    public int PostsPerWeek { get; }
    public IReadOnlyList<DayOfWeek> Days { get; }
    public TimeSpan TimeOfDay { get; }
    public TimeSpan TriggerWindow { get; }

    public ChannelPostScheduleOptions(
        int postsPerWeek,
        IReadOnlyList<DayOfWeek> days,
        TimeSpan timeOfDay,
        TimeSpan? triggerWindow = null)
    {
        if (postsPerWeek <= 0)
            throw new ArgumentOutOfRangeException(nameof(postsPerWeek), "Posts per week must be positive.");
        if (days is null || days.Count == 0)
            throw new ArgumentException("At least one day must be provided.", nameof(days));
        if (days.Count != postsPerWeek)
            throw new ArgumentException("Number of days must match posts per week.", nameof(days));

        var distinctDays = days.Distinct().OrderBy(d => d).ToArray();
        if (distinctDays.Length != days.Count)
            throw new ArgumentException("Duplicate days are not allowed.", nameof(days));

        PostsPerWeek = postsPerWeek;
        Days = distinctDays;
        TimeOfDay = timeOfDay;
        TriggerWindow = triggerWindow ?? DefaultTriggerWindow;
    }

    public static ChannelPostScheduleOptions FromStrings(
        string? postsPerWeek,
        string? days,
        string? timeOfDay,
        string? triggerWindowMinutes = null)
    {
        if (string.IsNullOrWhiteSpace(postsPerWeek))
            throw new ArgumentException("Posts per week is required.", nameof(postsPerWeek));
        if (string.IsNullOrWhiteSpace(days))
            throw new ArgumentException("Days are required.", nameof(days));
        if (string.IsNullOrWhiteSpace(timeOfDay))
            throw new ArgumentException("Time of day is required.", nameof(timeOfDay));

        if (!int.TryParse(postsPerWeek, NumberStyles.Integer, CultureInfo.InvariantCulture, out var perWeek) || perWeek <= 0)
            throw new FormatException("Posts per week must be a positive integer.");

        var dayTokens = days
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Replace(" ", string.Empty))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
        if (dayTokens.Count == 0)
            throw new ArgumentException("At least one day must be specified.", nameof(days));

        var parsedDays = new List<DayOfWeek>();
        foreach (var token in dayTokens)
        {
            if (!Enum.TryParse<DayOfWeek>(token, true, out var day))
            {
                throw new ArgumentException($"Unknown day of week: '{token}'.", nameof(days));
            }
            parsedDays.Add(day);
        }

        var orderedDays = parsedDays.Distinct().OrderBy(d => d).ToArray();

        if (orderedDays.Length != perWeek)
            throw new ArgumentException("Number of days must match posts per week.", nameof(days));

        var parsedTime = TimeSpan.Parse(timeOfDay, CultureInfo.InvariantCulture);

        TimeSpan? triggerWindow = null;
        if (!string.IsNullOrWhiteSpace(triggerWindowMinutes))
        {
            if (!int.TryParse(triggerWindowMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
                throw new FormatException("Trigger window minutes must be a positive integer when provided.");
            triggerWindow = TimeSpan.FromMinutes(minutes);
        }

        return new ChannelPostScheduleOptions(perWeek, orderedDays, parsedTime, triggerWindow);
    }
}
