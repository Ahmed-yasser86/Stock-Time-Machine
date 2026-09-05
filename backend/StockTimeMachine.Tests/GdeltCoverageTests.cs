using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Interval-completeness: the requested [start..end] must be traversed day by
// day with zero skipped windows. Every test asserts DATE COVERAGE, not just
// row counts — a passing test proves requested interval == searched interval.
public class GdeltCoverageTests
{
    private static IConfiguration CloudConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gdelt:ApiKey"] = "test-key",
            ["Gdelt:CloudBaseUrl"] = "https://gdeltcloud.com",
        }).Build();

    private const string EntityOk = """
        {"success": true, "data": [
          {"entity_id": "e_msft", "identifiers": {"ticker": ["MSFT"]}}
        ]}
        """;

    private static string CloudStories(params (string Date, (string Title, string Url)[] Articles)[] days)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"success\": true, \"data\": [");
        bool firstStory = true;
        foreach (var (date, articles) in days)
        {
            foreach (var (title, url) in articles)
            {
                if (!firstStory) sb.Append(',');
                firstStory = false;
                sb.Append("{\"id\": \"s\", \"title\": \"story\", \"story_date\": \"" + date + "\", \"top_articles\": [{\"title\": \"" + title + "\", \"url\": \"" + url + "\", \"domain\": \"example.com\"}]}");
            }
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // Records every queried day-range so tests prove contiguity, not just output.
    private sealed class RecordingCloudHandler : HttpMessageHandler
    {
        public List<string> QueriedDays { get; } = new();
        private readonly Func<string, HttpResponseMessage> _respond;
        public RecordingCloudHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            if (uri.Contains("/search"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EntityOk),
                });
            var start = System.Text.RegularExpressions.Regex.Match(uri, @"date_start=(\d{4}-\d{2}-\d{2})").Groups[1].Value;
            var end = System.Text.RegularExpressions.Regex.Match(uri, @"date_end=(\d{4}-\d{2}-\d{2})").Groups[1].Value;
            lock (QueriedDays)
                QueriedDays.Add($"{start}..{end}");
            return Task.FromResult(_respond(start));
        }
    }

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Failure(HttpStatusCode status) =>
        new(status) { Content = new StringContent("{}") };

    private static GdeltCloudNewsProvider CloudProvider(RecordingCloudHandler handler) =>
        new(new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig());

    private static List<DateOnly> Represented(IReadOnlyList<NewsArticle> rows) =>
        rows.Select(n => DateOnly.FromDateTime(n.PublishedAt)).Distinct().OrderBy(d => d).ToList();

    // Full 8-day window helper: explicit payloads per date, empty elsewhere.
    private static Func<string, HttpResponseMessage> Window(
        Dictionary<string, (string Title, string Url)[]> byDate,
        Dictionary<string, HttpStatusCode>? failures = null) =>
        day =>
        {
            if (failures is not null && failures.TryGetValue(day, out var status))
                return Failure(status);
            if (!byDate.TryGetValue(day, out var articles))
                return JsonOk(CloudStories());
            return JsonOk(CloudStories((day, articles)));
        };

    private static List<string> ExpectedRanges(DateOnly cutoff, int daysBack = 7) =>
        Enumerable.Range(0, daysBack + 1)
            .Select(i => cutoff.AddDays(-daysBack + i).ToString("yyyy-MM-dd"))
            .Select(d => $"{d}..{d}")
            .ToList();

    [Fact]
    public async Task Cloud_ThreeDayWindow_TraversesEntireEightDayRange()
    {
        // Cutoff 06-27 → requested [06-20..06-27]; news lives on 3 of those days.
        var handler = new RecordingCloudHandler(Window(new Dictionary<string, (string, string)[]>
        {
            ["2026-06-25"] = new[] { ("T1", "https://example.com/t1") },
            ["2026-06-26"] = new[] { ("T2", "https://example.com/t2") },
            ["2026-06-27"] = new[] { ("T3", "https://example.com/t3") },
        }));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 27));

        Assert.Equal(ExpectedRanges(new DateOnly(2026, 6, 27)), handler.QueriedDays);
        Assert.Equal(3, rows.Count);
        Assert.Equal(
            new[] { new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 26), new DateOnly(2026, 6, 27) },
            Represented(rows));
    }

    [Fact]
    public async Task Cloud_SevenDaySpan_CoversMonthBoundaryAndWeekend()
    {
        // 2026-06-28 is a Sunday; requested window crosses June→July.
        var days = Enumerable.Range(0, 8).Select(i => new DateOnly(2026, 6, 27).AddDays(i)).ToList();
        var byDate = days.ToDictionary(
            d => d.ToString("yyyy-MM-dd"),
            d => new[] { ($"T{d.Day}", $"https://example.com/{d:MMdd}") });
        var handler = new RecordingCloudHandler(Window(byDate));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 7, 4));

        Assert.Equal(ExpectedRanges(new DateOnly(2026, 7, 4)), handler.QueriedDays);
        Assert.Equal(8, rows.Count);
        Assert.Equal(days, Represented(rows));
        Assert.True(rows[0].PublishedAt >= rows[^1].PublishedAt); // newest-first
    }

    [Fact]
    public async Task Cloud_BusyDay_DoesNotStarveOtherDays()
    {
        // The reported bug shape: one sort=recent page let busy days eat the
        // window. Day one carries 105 articles (over the 100/day cap); the
        // other days must survive intact.
        var busy = Enumerable.Range(0, 105).Select(i => ($"B{i}", $"https://example.com/b{i}")).ToArray();
        var handler = new RecordingCloudHandler(Window(new Dictionary<string, (string, string)[]>
        {
            ["2026-06-25"] = busy,
            ["2026-06-26"] = new[] { ("Q1", "https://example.com/q1"), ("Q2", "https://example.com/q2") },
        }));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 26));

        Assert.Equal(102, rows.Count); // 100 (day cap) + 2 — nothing lost beyond the cap
        Assert.Contains(rows, n => n.Title == "Q1");
        Assert.Contains(rows, n => n.Title == "Q2");
        Assert.Equal(
            new[] { new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 26) },
            Represented(rows));
    }

    [Fact]
    public async Task Cloud_DuplicateAcrossDays_KeptOnce()
    {
        var handler = new RecordingCloudHandler(Window(new Dictionary<string, (string, string)[]>
        {
            ["2026-06-25"] = new[] { ("Same", "https://example.com/same"), ("A", "https://example.com/a") },
            ["2026-06-26"] = new[] { ("Same", "https://example.com/same"), ("B", "https://example.com/b") },
        }));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 26));

        Assert.Equal(3, rows.Count); // cross-day dup kept once, both dates preserved
        Assert.Equal(
            new[] { new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 26) },
            Represented(rows));
    }

    [Fact]
    public async Task Cloud_FailedDay_KeepsOtherDays()
    {
        var handler = new RecordingCloudHandler(Window(
            new Dictionary<string, (string, string)[]>
            {
                ["2026-06-25"] = new[] { ("T1", "https://example.com/t1") },
                ["2026-06-27"] = new[] { ("T3", "https://example.com/t3") },
            },
            new Dictionary<string, HttpStatusCode>
            {
                ["2026-06-26"] = HttpStatusCode.InternalServerError,
            }));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 27));

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, n => DateOnly.FromDateTime(n.PublishedAt) == new DateOnly(2026, 6, 26));
        // Attempted, not skipped: the failed day was still queried.
        Assert.Contains(handler.QueriedDays, d => d.StartsWith("2026-06-26"));
        Assert.Equal(
            new[] { new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 27) },
            Represented(rows));
    }

    [Fact]
    public async Task Cloud_CutoffDay_IsInclusive()
    {
        // The cutoff day itself must be searched AND kept (inclusive end).
        var handler = new RecordingCloudHandler(Window(new Dictionary<string, (string, string)[]>
        {
            ["2026-06-26"] = new[] { ("Edge", "https://example.com/edge") },
        }));
        var provider = CloudProvider(handler);

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 26));

        Assert.Contains(rows, n => n.Title == "Edge");
        Assert.Contains(handler.QueriedDays, d => d.StartsWith("2026-06-26"));
    }

    private static IConfiguration ProjectConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gdelt:BaseUrl"] = "https://api.gdeltproject.org/api/v2",
        }).Build();

    private static string ProjectArticles(params (string Title, string Url, string Date)[] articles)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"articles\": [");
        bool first = true;
        foreach (var (title, url, date) in articles)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"title\": \"" + title + "\", \"url\": \"" + url + "\", \"domain\": \"example.com\", \"published_date\": \"" + date + "\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private sealed class RecordingProjectHandler : HttpMessageHandler
    {
        public List<string> QueriedRanges { get; } = new();
        private readonly Func<string, string> _articlesFor;
        public RecordingProjectHandler(Func<string, string> articlesFor) => _articlesFor = articlesFor;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            var from = System.Text.RegularExpressions.Regex.Match(uri, @"startdatetime=(\d+)").Groups[1].Value;
            var to = System.Text.RegularExpressions.Regex.Match(uri, @"enddatetime=(\d+)").Groups[1].Value;
            lock (QueriedRanges)
                QueriedRanges.Add($"{from}..{to}");
            var day = from.Substring(0, 8);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_articlesFor(day)),
            });
        }
    }

    [Fact]
    public async Task Project_EachDayGetsFullDayBounds()
    {
        var handler = new RecordingProjectHandler(_ => ProjectArticles());
        var provider = new GdeltNewsProvider(
            new HttpClient(handler), NullLogger<GdeltNewsProvider>.Instance, ProjectConfig());

        await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 27));

        // 8 contiguous full days, explicit 00:00:00→23:59:59 bounds — no gaps.
        Assert.Equal(8, handler.QueriedRanges.Count);
        var ordered = handler.QueriedRanges.OrderBy(r => r).ToList();
        Assert.StartsWith("20260620" + "000000..20260620" + "235959", ordered[0]);
        Assert.StartsWith("20260627" + "000000..20260627" + "235959", ordered[^1]);
        for (int i = 1; i < ordered.Count; i++)
        {
            var prevEnd = ordered[i - 1].Split("..")[1].Substring(0, 8);
            var curStart = ordered[i].Split("..")[0].Substring(0, 8);
            Assert.Equal(DateOnly.ParseExact(prevEnd, "yyyyMMdd").AddDays(1),
                DateOnly.ParseExact(curStart, "yyyyMMdd"));
        }
    }

    [Fact]
    public async Task Project_MultiDayMerge_DedupesAndKeepsDates()
    {
        var handler = new RecordingProjectHandler(day => day switch
        {
            "20260625" => ProjectArticles(
                ("Same", "https://example.com/same", "2026-06-25 10:00:00"),
                ("Old", "https://example.com/old", "2026-06-25 11:00:00")),
            "20260626" => ProjectArticles(
                ("Same", "https://example.com/same", "2026-06-26 09:00:00"),
                ("New", "https://example.com/new", "2026-06-26 10:00:00"),
                ("Bad date", "https://example.com/bad", "not-a-date")),
            _ => ProjectArticles(),
        });
        var provider = new GdeltNewsProvider(
            new HttpClient(handler), NullLogger<GdeltNewsProvider>.Instance, ProjectConfig());

        var rows = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 26));

        Assert.Equal(3, rows.Count); // cross-day dup kept once, unparseable skipped, dates kept
        Assert.Equal(
            new[] { new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 26) },
            Represented(rows));
    }

    [Fact]
    public async Task Cloud_ThrottleMidWindow_AbortsAndPropagates()
    {
        // A 429 must abort the remaining days (not hammer through them) and
        // propagate so the outer resilience wrapper backs off the whole fetch.
        var handler = new RecordingCloudHandler(Window(
            new Dictionary<string, (string, string)[]>
            {
                ["2026-06-25"] = new[] { ("T1", "https://example.com/t1") },
            },
            new Dictionary<string, HttpStatusCode>
            {
                ["2026-06-26"] = HttpStatusCode.TooManyRequests,
            }));
        var provider = CloudProvider(handler);

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.SearchAsync("MSFT", new DateOnly(2026, 6, 27)));
        // Days after the 429 were queried on the attempts made (resilience
        // retries whole fetches: 5 attempts × reaching 06-26).
        Assert.DoesNotContain(handler.QueriedDays, d => d.StartsWith("2026-06-27"));
    }

    [Fact]
    public async Task Cloud_TenDayWindow_StaysWhole()
    {
        // Window size is fixed by the provider (trailing 8 days); a 10-day
        // span is covered by overlapping investigation cutoffs, never by
        // shrinking. Two cutoffs 4 days apart must jointly traverse 12 days.
        var byDate = Enumerable.Range(0, 12)
            .Select(i => new DateOnly(2026, 6, 20).AddDays(i))
            .ToDictionary(
                d => d.ToString("yyyy-MM-dd"),
                d => new[] { ($"T{d.Day}", $"https://example.com/{d:MMdd}") });
        var handler = new RecordingCloudHandler(Window(byDate));
        var provider = CloudProvider(handler);

        var first = await provider.SearchAsync("MSFT", new DateOnly(2026, 6, 27));
        var second = await provider.SearchAsync("MSFT", new DateOnly(2026, 7, 1));

        var all = first.Concat(second)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .Select(n => DateOnly.FromDateTime(n.PublishedAt))
            .Distinct().OrderBy(d => d).ToList();
        Assert.Equal(
            Enumerable.Range(0, 12).Select(i => new DateOnly(2026, 6, 20).AddDays(i)).ToList(),
            all);
    }
}
