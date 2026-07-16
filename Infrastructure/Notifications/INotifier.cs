namespace WeekChgkSPB.Infrastructure.Notifications;

public interface INotifier
{
    Task NotifyNewPostAsync(Post post, CancellationToken ct = default);
    Task NotifyAutomationCandidateAsync(Post post, Announcement announcement, CancellationToken ct = default);
    Task NotifyAutomationSavedAsync(Post post, Announcement announcement, CancellationToken ct = default);
}
