namespace WeekChgkSPB.Infrastructure.AnnouncementAutomation;

internal sealed record AnnouncementAutomationOptions(
    AnnouncementAutomationMode Mode,
    Uri? BaseUrl,
    string? ApiKey,
    string Model,
    TimeSpan Timeout)
{
    public const string DefaultModel = "qwen3.5-flash-2026-02-23";

    public static AnnouncementAutomationOptions Disabled { get; } = new(
        AnnouncementAutomationMode.Off,
        null,
        null,
        DefaultModel,
        TimeSpan.FromSeconds(30));
}
