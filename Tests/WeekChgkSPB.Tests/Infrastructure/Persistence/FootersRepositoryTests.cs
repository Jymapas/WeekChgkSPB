using System;
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
        Assert.Null(allDesc[0].ExpiresAt);
        Assert.Equal(id1, allDesc[1].Id);

        var texts = repo.GetAllTextsDesc();
        Assert.Equal(new[] { "<i>footer2</i>", "<b>footer1</b>" }, texts);

        repo.Delete(id2);
        var remaining = repo.ListAllDesc();
        Assert.Single(remaining);
        Assert.Equal(id1, remaining[0].Id);
    }

    [Fact]
    public void Insert_WithExpiry_StoresAndReturnsExpiry()
    {
        _fixture.Reset();
        var repo = _fixture.CreateFootersRepository();

        var expiry = new DateTime(2030, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var id = repo.Insert("<b>timed</b>", expiry);

        var all = repo.ListAllDesc();
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.NotNull(all[0].ExpiresAt);
        Assert.Equal(expiry, all[0].ExpiresAt!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetAllTextsDesc_ExcludesExpiredFooters()
    {
        _fixture.Reset();
        var repo = _fixture.CreateFootersRepository();

        var pastExpiry = DateTime.UtcNow.AddDays(-1);
        var futureExpiry = DateTime.UtcNow.AddDays(30);

        repo.Insert("<b>expired</b>", pastExpiry);
        repo.Insert("<i>active</i>", futureExpiry);
        repo.Insert("<u>permanent</u>");

        var texts = repo.GetAllTextsDesc();
        Assert.Equal(2, texts.Count);
        Assert.Contains("<i>active</i>", texts);
        Assert.Contains("<u>permanent</u>", texts);
        Assert.DoesNotContain("<b>expired</b>", texts);
    }

    [Fact]
    public void GetAllTextsDesc_IncludesFooterWithoutExpiry()
    {
        _fixture.Reset();
        var repo = _fixture.CreateFootersRepository();

        repo.Insert("<b>permanent</b>");

        var texts = repo.GetAllTextsDesc();
        Assert.Single(texts);
        Assert.Equal("<b>permanent</b>", texts[0]);
    }
}
