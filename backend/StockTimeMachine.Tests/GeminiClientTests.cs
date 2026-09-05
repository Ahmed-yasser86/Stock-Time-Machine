using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class GeminiClientTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "test",
                ["Gemini:EmbeddingModel"] = "gemini-embedding-2-preview",
                ["Gemini:SummaryModel"] = "gemini-3.5-flash-lite",
            })
            .Build();

    private static GeminiClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<GeminiClient>.Instance, Config());

    private const string EmbedPayload = """{"embedding": {"values": [0.1, 0.2, 0.3]}}""";

    private const string BriefPayload = """
        {"candidates": [{"content": {"parts": [{"text": "{\"summary\": \"S\", \"key_points\": [\"K1 [1].\"]}"}]}}]}
        """;

    private const string ReviewPayload = """
        {"candidates": [{"content": {"parts": [{"text": "{\"issues\": [{\"ref\": \"move 2020-02-01\", \"verdict\": \"supported\", \"detail\": \"D\"}]}"}]}}]}
        """;

    [Fact]
    public async Task Embed_ParsesVectors()
    {
        var sut = Client(new StubHttpMessageHandler(EmbedPayload));

        var vectors = await sut.EmbedAsync(new[] { "hello" });

        var single = Assert.Single(vectors);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, single);
    }

    [Fact]
    public async Task Summarize_ParsesBrief()
    {
        var sut = Client(new StubHttpMessageHandler(BriefPayload));

        var brief = await sut.SummarizeClusterAsync("prompt");

        Assert.NotNull(brief);
        Assert.Equal("S", brief!.Summary);
        Assert.Equal(new[] { "K1 [1]." }, brief.KeyPoints);
        Assert.Equal("gemini-3.5-flash-lite", brief.Model);
    }

    [Fact]
    public async Task Review_ParsesIssues()
    {
        var sut = Client(new StubHttpMessageHandler(ReviewPayload));

        var issues = await sut.ReviewNoteAsync("prompt");

        var single = Assert.Single(issues);
        Assert.Equal("move 2020-02-01", single.Ref);
        Assert.Equal("supported", single.Verdict);
    }

    [Fact]
    public async Task ServerError_SummarizeNull_ReviewEmpty()
    {
        var sut = Client(new StubHttpMessageHandler("boom", HttpStatusCode.InternalServerError));

        Assert.Null(await sut.SummarizeClusterAsync("prompt"));
        Assert.Empty(await sut.ReviewNoteAsync("prompt"));
    }

    [Fact]
    public async Task DisabledKey_SummarizeNull_ReviewEmpty_EmbedThrows()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var sut = new GeminiClient(new HttpClient(new StubHttpMessageHandler("{}")),
            NullLogger<GeminiClient>.Instance, config);

        Assert.False(sut.IsEnabled);
        Assert.Null(await sut.SummarizeClusterAsync("prompt"));
        Assert.Empty(await sut.ReviewNoteAsync("prompt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.EmbedAsync(new[] { "x" }));
    }
}
