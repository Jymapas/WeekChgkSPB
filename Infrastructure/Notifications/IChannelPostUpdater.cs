using System.Threading;
using System.Threading.Tasks;

namespace WeekChgkSPB.Infrastructure.Notifications;

public interface IChannelPostUpdater
{
    Task UpdateLastPostAsync(CancellationToken ct);
}

internal sealed class NoOpChannelPostUpdater : IChannelPostUpdater
{
    public Task UpdateLastPostAsync(CancellationToken ct) => Task.CompletedTask;
}
