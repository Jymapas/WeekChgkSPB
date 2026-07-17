using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeekChgkSPB.Infrastructure.AnnouncementAutomation;

namespace WeekChgkSPB.Tests.Infrastructure.AnnouncementAutomation;

public sealed class QwenAnnouncementExtractionClientTests
{
    [Fact]
    public async Task ExtractAsync_DoesNotSendPriceOrContacts()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var options = new AnnouncementAutomationOptions(
            AnnouncementAutomationMode.Shadow,
            new Uri("https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"),
            "test-key",
            AnnouncementAutomationOptions.DefaultModel,
            TimeSpan.FromSeconds(30));
        var client = new QwenAnnouncementExtractionClient(httpClient, options);
        var post = new Post
        {
            Id = 1,
            Link = "https://example.test/1",
            Title = "Кубок знаний",
            Description = "Стоимость: команда 1800 ₽. Телефон +79990000000"
        };
        var preParse = new AnnouncementPreParseResult(
            true, null, "Кубок знаний\n12 июля в 19:30 в клубе «Rossi's»", 1800,
            "Команда — 1800 ₽", new DateTime(2026, 7, 12, 19, 30, 0), "Rossi's", 1000);

        var result = await client.ExtractAsync(post, preParse, [], new DateTime(2026, 7, 1), CancellationToken.None);

        Assert.True(result.Success, result.FailureCode);
        Assert.DoesNotContain("1800", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("79990000000", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cost", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("qwen3.5-flash-2026-02-23", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"json_object\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"enable_thinking\":false", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("json_schema", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("max_tokens", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractAsync_PreservesTokenUsageWhenModelJsonIsInvalid()
    {
        var handler = new CaptureHandler(content: "not-json");
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var client = new QwenAnnouncementExtractionClient(httpClient, CreateOptions());
        var result = await client.ExtractAsync(
            new Post { Id = 2, Link = "https://example.test/2", Title = "Test" },
            new AnnouncementPreParseResult(
                true, null, "Test event", 1000, "Команда — 1000 ₽",
                new DateTime(2026, 7, 12, 19, 30, 0), "Place", 100),
            [],
            new DateTime(2026, 7, 1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("json_invalid", result.FailureCode);
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(40, result.OutputTokens);
    }

    private static AnnouncementAutomationOptions CreateOptions() => new(
        AnnouncementAutomationMode.Shadow,
        new Uri("https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"),
        "test-key",
        AnnouncementAutomationOptions.DefaultModel,
        TimeSpan.FromSeconds(30));

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _content;

        public CaptureHandler(string? content = null)
        {
            _content = content ?? "{\"rawTournamentName\":\"Кубок знаний\",\"tournamentName\":\"Кубок знаний\",\"place\":\"Rossi's\",\"localDateTime\":\"2026-07-12T19:30\",\"evidence\":{\"tournamentName\":\"Кубок знаний\",\"place\":\"Rossi's\",\"localDateTime\":\"12 июля в 19:30\"}}";
        }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = _content } } },
                usage = new { prompt_tokens = 100, completion_tokens = 40 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
