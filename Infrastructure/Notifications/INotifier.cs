namespace WeekChgkSPB.Infrastructure.Notifications;

public interface INotifier
{
    Task<int> NotifyNewPostAsync(Post post, CancellationToken ct = default);
    Task NotifyAutomationSavedAsync(Post post, Announcement announcement, CancellationToken ct = default);
}
