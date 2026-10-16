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

    [Fact]
    public void Parse_SelectsPriceForLargestExplicitTeamCapacity()
    {
        var post = CreatePost(
            "Стоимость<br>" +
            "1950 руб с команды до 6ти человек<br>" +
            "1800 руб с команды до 5ти человек<br>" +
            "1650 рублей - 4 человека<br>" +
            "1500 рублей - 3 человека");

        var result = _parser.Parse(post, Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(1950, result.Cost);
        Assert.Equal("1950 руб с команды до 6ти человек", result.CostEvidence);
    }

    [Fact]
    public void Parse_AcceptsFullTeamPriceBeforeOtherTariffsOnTheSameLine()
    {
        var post = CreatePost(
            "Стоимость турнира: 1800 руб. с команды до 6ти человек, пары — 800 руб., школьники и соло — 400 руб.");

        var result = _parser.Parse(post, Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(1800, result.Cost);
    }

    [Fact]
    public void Parse_UsesTitleAsEventLineWhenBodyHasNoDate()
    {
        var post = new Post
        {
            Id = 3393834,
            Link = "https://chgk-spb.livejournal.com/3393834.html",
            Title = "31 июля (пятница) 19:30 - \"Sims To Be: Vol. 22\" в кафе \"Тегеран\"",
            Description = "Стоимость турнира:<br>2100 руб с команды до 6ти человек<br>1800 руб - студенты и команды до 3х человек"
        };

        var result = _parser.Parse(post, Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.Equal(new DateTime(2026, 7, 31, 16, 30, 0, DateTimeKind.Utc),
            TimeZoneInfo.ConvertTimeToUtc(result.LocalDateTime!.Value, PostFormatter.Moscow));
        Assert.Equal("Тегеран", result.Place);
        Assert.Equal(2100, result.Cost);
    }

    [Fact]
    public void Parse_RejectsDifferentPricesForSameLargestCapacity()
    {
        var post = CreatePost(
            "Стоимость<br>" +
            "Команда до 6 человек — 1800 ₽<br>" +
            "Команда до 6 человек — 2000 ₽<br>" +
            "Команда до 5 человек — 1700 ₽");

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("cost_ambiguous", result.FailureCode);
    }

    [Fact]
    public void Parse_RejectsGenericTeamPriceCompetingWithLargestCapacity()
    {
        var post = CreatePost(
            "Стоимость<br>" +
            "Команда — 1800 ₽<br>" +
            "Команда до 6 человек — 2000 ₽<br>" +
            "Команда до 5 человек — 1700 ₽");

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("cost_ambiguous", result.FailureCode);
    }

    [Fact]
    public void Parse_RejectsRegistrationUpdateBeforeExtraction()
    {
        var post = CreatePost("Стоимость<br>Команда — 1800 ₽");
        post.Description = $"<strong>ПРОДОЛЖАЕТСЯ РЕГИСТРАЦИЯ</strong><br>{post.Description}";

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("post_update_ignored", result.FailureCode);
    }

    [Theory]
    [InlineData("Перенос площадки")]
    [InlineData("Замена площадки")]
    [InlineData("Смена площадки")]
    public void Parse_RecognizesVenueChangeAndKeepsExtractionData(string marker)
    {
        var post = CreatePost("Стоимость<br>Команда — 1800 ₽");
        post.Description = $"<strong>{marker}</strong><br>{post.Description}";

        var result = _parser.Parse(post, Now);

        Assert.True(result.Success, result.FailureCode);
        Assert.True(result.IsVenueChange);
        Assert.Equal("Rossi's", result.Place);
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
    [InlineData("Стоимость<br>1200 ₽ с команды студентов до 6 человек")]
    [InlineData("Стоимость<br>Команда из 3 человек — 1200 ₽")]
    public void Parse_RejectsUnsupportedOrMissingCost(string costBlock)
    {
        var result = _parser.Parse(CreatePost(costBlock), Now);

        Assert.False(result.Success);
        Assert.Equal("cost_not_found", result.FailureCode);
    }

    [Fact]
    public void Parse_AllowsApiWhenPriceAndEventExistButPlaceIsMissing()
    {
        var post = new Post
        {
            Id = 44,
            Title = "Турнир «Кубок знаний» 12 июля в 19:30",
            Description =
                "12 июля в 19:30 турнир «Кубок знаний»<br>" +
                "Стоимость<br>Команда до 6 человек — 1800 ₽"
        };

        var result = _parser.Parse(post, Now);

        Assert.False(result.Success);
        Assert.Equal("place_not_found", result.FailureCode);
        Assert.True(result.CanCallApi);
        Assert.Equal(1800, result.Cost);
        Assert.NotNull(result.LocalDateTime);
    }

    [Theory]
    [InlineData("09:30", "9-30", null)]
    [InlineData("19:00", "19-30", "datetime_conflict")]
    public void Parse_ComparesExplicitStartTimes(string titleTime, string gameTime, string? expectedFailure)
    {
        var post = new Post
        {
            Title = $"12 июля {titleTime} турнир «Кубок» в клубе «Rossi's»",
            Description = $"Начало регистрации в 9:00, начало турнира {gameTime}<br>Взнос — 1800 рублей"
        };
        var result = _parser.Parse(post, Now);
        Assert.Equal(expectedFailure, result.FailureCode);
    }

    private static Post CreatePost(string costBlock) => new()
    {
        Id = 42,
        Link = "https://chgk-spb.livejournal.com/42.html",
        Title = "Турнир «Кубок знаний» 12 июля в 19:30",
        Description = $"12 июля в 19:30 турнир «Кубок знаний» в клубе «Rossi's»<br>{costBlock}<br>Для участия заполните форму"
    };
}
