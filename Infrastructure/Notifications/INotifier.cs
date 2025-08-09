namespace WeekChgkSPB.Infrastructure.Notifications;

public interface INotifier
{
    Task NotifyNewPostAsync(Post post, CancellationToken ct = default);
}