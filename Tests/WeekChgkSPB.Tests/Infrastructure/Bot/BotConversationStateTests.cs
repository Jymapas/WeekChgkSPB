using WeekChgkSPB.Infrastructure.Bot;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class BotConversationStateTests
{
    [Fact]
    public void AddOrUpdate_CreatesNewState_WhenMissing()
    {
        var store = new BotConversationState();

        var state = store.AddOrUpdate(1);

        Assert.NotNull(state);
        Assert.Equal(AddStep.None, state.Step);

        var sameReference = store.AddOrUpdate(1);
        Assert.Same(state, sameReference);
    }

    [Fact]
    public void GetOrAdd_ReturnsExistingOrCreates()
    {
        var store = new BotConversationState();

        var created = store.GetOrAdd(2);
        created.Step = AddStep.WaitingId;

        var fetched = store.GetOrAdd(2);
        Assert.Same(created, fetched);
        Assert.Equal(AddStep.WaitingId, fetched.Step);

        var newState = store.GetOrAdd(3);
        Assert.NotSame(created, newState);
        Assert.Equal(AddStep.None, newState.Step);
    }

    [Fact]
    public void TryGet_ReturnsFalseWhenMissing()
    {
        var store = new BotConversationState();

        var exists = store.TryGet(5, out var state);

        Assert.False(exists);
        Assert.Null(state);
    }

    [Fact]
    public void Remove_DeletesState()
    {
        var store = new BotConversationState();
        var state = store.AddOrUpdate(7);
        state.Step = AddStep.WaitingName;

        var removed = store.TryGet(7, out var existing);
        Assert.True(removed);
        Assert.Same(state, existing);

        store.Remove(7);

        Assert.False(store.TryGet(7, out _));
    }
}
