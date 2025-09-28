using WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

namespace WeekChgkSPB.Tests.Infrastructure.Persistence;

public class FootersRepositoryTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fixture;

    public FootersRepositoryTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void InsertListDelete_Works()
    {
        _fixture.Reset();
        var repo = _fixture.CreateFootersRepository();

        var id1 = repo.Insert("<b>footer1</b>");
        var id2 = repo.Insert("<i>footer2</i>");

        var allDesc = repo.ListAllDesc();
        Assert.Equal(2, allDesc.Count);
        Assert.Equal(id2, allDesc[0].Id);
        Assert.Equal("<i>footer2</i>", allDesc[0].Text);
        Assert.Equal(id1, allDesc[1].Id);

        var texts = repo.GetAllTextsDesc();
        Assert.Equal(new[] { "<i>footer2</i>", "<b>footer1</b>" }, texts);

        repo.Delete(id2);
        var remaining = repo.ListAllDesc();
        Assert.Single(remaining);
        Assert.Equal(id1, remaining[0].Id);
    }
}
