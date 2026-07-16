using System;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class AnnouncementPreParserTests
{
    private readonly AnnouncementPreParser _parser = new(PostFormatter.Moscow);
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Стоимость турнира<br>Команда до 6 человек — 1800 руб.<br>Студенты — 1200 руб.", 1800)]
    [InlineData("Стоимость<br>Команда из 5 человек — 1700 ₽", 1700)]
    [InlineData("Стоимость<br>Команда из 4 человек — 1600 рублей", 1600)]
    [InlineData("Стоимость<br>Команда до 6 человек — 1800 ₽<br>Скидка за вторую игру — 300 ₽", 1800)]
    [InlineData("Стоимость<br>Команда — 1800 ₽<br>Пара — 700 ₽<br>Соло — 400 ₽", 1800)]
    [InlineData("Стоимость<br>Команда — 1800 ₽<br>Депозит<br>Депозит 500 ₽<br>Призы 10000 ₽", 1800)]
    public void Parse_ExtractsOnlyStandardFullTeamPrice(string costBlock, int expected)
    {
        var result = _parser.Parse(CreatePost(costBlock), Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(expected, result.Cost);
        Assert.NotNull(result.CostEvidence);
    }

    [Fact]
    public void Parse_RejectsSeveralCompetingFullTeamPrices()
    {
        var post = CreatePost("Стоимость<br>Команда до 6 человек — 1800 ₽<br>Команда — 2000 ₽");

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("cost_ambiguous", result.FailureCode);
    }

    [Theory]
    [InlineData("Перенос площадки")]
    [InlineData("ПРОДОЛЖАЕТСЯ РЕГИСТРАЦИЯ")]
    public void Parse_RejectsUpdatePostsBeforeExtraction(string marker)
    {
        var post = CreatePost("Стоимость<br>Команда — 1800 ₽");
        post.Description = $"<strong>{marker}</strong><br>{post.Description}";

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("post_update_ignored", result.FailureCode);
    }

    [Fact]
    public void Parse_SupportsContributionAndGameTimeOnFollowingLine()
    {
        var post = new Post
        {
            Id = 43,
            Title = "Лёгкий Смоленск - 36",
            Link = "https://chgk-spb.livejournal.com/43.html",
            Description = "Приглашаем отыграть «Лёгкий Смоленск - 36» 24-го июля в кафе «Тегеран».<br>" +
                          "Начало регистрации в 19:15, первый вопрос будет задан в 19:30.<br>" +
                          "Взнос - 1650 рублей. Все расчёты только на игре."
        };

        var result = _parser.Parse(post, Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(1650, result.Cost);
        Assert.Equal(new DateTime(2026, 7, 24, 19, 30, 0), result.LocalDateTime);
        Assert.Equal("Тегеран", result.Place);
    }

    [Theory]
    [InlineData("Стоимость<br>Бесплатно")]
    [InlineData("Стоимость<br>По договорённости")]
    [InlineData("Депозит 1000 ₽")]
    [InlineData("Стоимость<br>Команда студентов до 6 человек — 1200 ₽")]
    [InlineData("Стоимость<br>Команда из 3 человек — 1200 ₽")]
    public void Parse_RejectsUnsupportedOrMissingCost(string costBlock)
    {
        var result = _parser.Parse(CreatePost(costBlock), Now);

        Assert.False(result.Success);
        Assert.Equal("cost_not_found", result.FailureCode);
    }

    private static Post CreatePost(string costBlock) => new()
    {
        Id = 42,
        Link = "https://chgk-spb.livejournal.com/42.html",
        Title = "Турнир «Кубок знаний» 12 июля в 19:30",
        Description = $"12 июля в 19:30 турнир «Кубок знаний» в клубе «Rossi's»<br>{costBlock}<br>Для участия заполните форму"
    };
}
