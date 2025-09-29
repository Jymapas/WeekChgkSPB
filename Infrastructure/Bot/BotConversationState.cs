using System.Collections.Concurrent;

namespace WeekChgkSPB.Infrastructure.Bot;

internal class BotConversationState
{
    private readonly ConcurrentDictionary<long, AddAnnouncementState> _states = new();

    public AddAnnouncementState AddOrUpdate(long userId)
    {
        return _states.AddOrUpdate(
            userId,
            _ => new AddAnnouncementState(),
            (_, existing) => existing ?? new AddAnnouncementState());
    }

    public AddAnnouncementState GetOrAdd(long userId)
    {
        return _states.GetOrAdd(userId, _ => new AddAnnouncementState())!;
    }

    public bool TryGet(long userId, out AddAnnouncementState? state)
    {
        return _states.TryGetValue(userId, out state);
    }

    public void Remove(long userId)
    {
        _states.TryRemove(userId, out _);
    }
}
