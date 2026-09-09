using System.Net;
using OpenKaraoke.Core.RateLimit;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Core.Time;

namespace OpenKaraoke.App.Tests;

public class YoutubeSearchServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Canned HTTP response body plus status code.</summary>
    private readonly record struct CannedResponse(int Status, string Body);

    /// <summary>Records requests and returns canned responses keyed by (decoded) URL.</summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, CannedResponse> _responses;

        public FakeHttpHandler(Dictionary<string, CannedResponse> responses)
        {
            _responses = responses;
        }

        public List<string> RequestedUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            RequestedUrls.Add(url);

            HttpResponseMessage response = _responses.TryGetValue(url, out CannedResponse canned)
                ? new HttpResponseMessage((HttpStatusCode)canned.Status) { Content = new StringContent(canned.Body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"code\":404,\"message\":\"not found\"}}"),
                };

            return Task.FromResult(response);
        }
    }

    private static string SearchBody(params string[] videoIds)
    {
        string[] items = videoIds
            .Select(id => "{\"id\":{\"videoId\":\"" + id + "\"},\"snippet\":{\"title\":\"" + id +
                          " 곡\",\"channelTitle\":\"채널\"}}")
            .ToArray();
        return "{\"items\":[" + string.Join(",", items) + "]}";
    }

    private static string VideosBody(params (string Id, int Seconds, bool Embeddable)[] videos)
    {
        string[] items = videos
            .Select(v => "{\"id\":\"" + v.Id + "\",\"contentDetails\":{\"duration\":\"PT" + v.Seconds +
                         "S\"},\"status\":{\"embeddable\":" +
                         v.Embeddable.ToString().ToLowerInvariant() + "}}")
            .ToArray();
        return "{\"items\":[" + string.Join(",", items) + "]}";
    }

    private static (YoutubeSearchService Service, FakeHttpHandler Handler, FakeClock Clock) Create(
        Dictionary<string, CannedResponse>? responses = null,
        string apiKey = "test-key",
        YoutubeSearchOptions? options = null,
        int maxRequestsPerMinute = 100)
    {
        var handler = new FakeHttpHandler(responses ?? new Dictionary<string, CannedResponse>());
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.googleapis.com/") };
        var clock = new FakeClock();
        var service = new YoutubeSearchService(
            http,
            apiKey,
            clock,
            cache: null,
            rateLimiter: new RateLimiter(maxRequestsPerMinute),
            options: options ?? new YoutubeSearchOptions());

        return (service, handler, clock);
    }

    private static string PrimaryUrl(string query) =>
        "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=50&q=" +
        query + "&key=test-key";

    private static string VideosUrl(string ids) =>
        "https://www.googleapis.com/youtube/v3/videos?part=contentDetails,status&id=" + ids +
        "&key=test-key";

    private static CannedResponse Ok(string body) => new(200, body);

    [Fact]
    public async Task SearchAsync_Success_RanksAndReturnsResponse()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("밤편지")] = Ok(SearchBody("bbb", "aaa")),
            [VideosUrl("bbb,aaa")] = Ok(VideosBody(("aaa", 253, true), ("bbb", 120, true))),
        };

        (YoutubeSearchService service, FakeHttpHandler handler, _) = Create(responses);

        SearchResponse result = await service.SearchAsync("밤편지");

        Assert.False(result.FromCache);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("밤편지", result.QueryUsed);
        Assert.Contains(handler.RequestedUrls, u => u == PrimaryUrl("밤편지"));
        Assert.Contains(handler.RequestedUrls, u => u == VideosUrl("bbb,aaa"));
    }

    [Fact]
    public async Task SearchAsync_SecondCall_ServedFromCacheWithoutHttp()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("밤편지")] = Ok(SearchBody("aaa")),
            [VideosUrl("aaa")] = Ok(VideosBody(("aaa", 253, true))),
        };
        (YoutubeSearchService service, FakeHttpHandler handler, _) = Create(responses);

        await service.SearchAsync("밤편지");
        int requestCountAfterFirst = handler.RequestedUrls.Count;

        SearchResponse second = await service.SearchAsync("밤편지");

        Assert.True(second.FromCache);
        Assert.Equal(requestCountAfterFirst, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task SearchAsync_EmptyPrimary_UsesFallback()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("밤편지")] = Ok("""{"items":[]}"""),
            [PrimaryUrl("밤편지 노래방")] = Ok(SearchBody("fff")),
            [VideosUrl("fff")] = Ok(VideosBody(("fff", 240, true))),
        };

        (YoutubeSearchService service, _, _) = Create(responses);

        SearchResponse result = await service.SearchAsync("밤편지");

        Assert.Single(result.Items);
        Assert.Equal("밤편지 노래방", result.QueryUsed);
    }

    [Fact]
    public async Task SearchAsync_AllEmpty_ReturnsEmptyResponse()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("zzz")] = Ok("""{"items":[]}"""),
        };

        (YoutubeSearchService service, _, _) = Create(responses, options: new YoutubeSearchOptions
        {
            MaxFallbackAttempts = 0,
        });

        SearchResponse result = await service.SearchAsync("zzz");

        Assert.Empty(result.Items);
        Assert.Equal("zzz", result.QueryUsed);
    }

    [Fact]
    public async Task SearchAsync_EmptyApiKey_ThrowsApiKeyMissing()
    {
        (YoutubeSearchService service, _, _) = Create(apiKey: "  ");

        var ex = await Assert.ThrowsAsync<SearchException>(() => service.SearchAsync("밤편지"));

        Assert.Equal(SearchErrorKind.ApiKeyMissing, ex.Kind);
    }

    [Fact]
    public async Task SearchAsync_QuotaExceeded_ThrowsQuotaExceeded()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("밤편지")] = new(403,
                """{"error":{"code":403,"message":"quota","errors":[{"reason":"quotaExceeded"}]}}"""),
        };

        (YoutubeSearchService service, _, _) = Create(responses);

        var ex = await Assert.ThrowsAsync<SearchException>(() => service.SearchAsync("밤편지"));

        Assert.Equal(SearchErrorKind.QuotaExceeded, ex.Kind);
    }

    [Fact]
    public async Task SearchAsync_RateLimited_ThrowsBeforeHttpCall()
    {
        var responses = new Dictionary<string, CannedResponse>
        {
            [PrimaryUrl("첫번째")] = Ok(SearchBody("aaa")),
            [VideosUrl("aaa")] = Ok(VideosBody(("aaa", 60, true))),
        };

        (YoutubeSearchService service, FakeHttpHandler handler, _) = Create(
            responses,
            maxRequestsPerMinute: 1);
        await service.SearchAsync("첫번째");
        int requestsAfterFirst = handler.RequestedUrls.Count;

        var ex = await Assert.ThrowsAsync<SearchException>(() => service.SearchAsync("두번째"));

        Assert.Equal(SearchErrorKind.RateLimited, ex.Kind);
        Assert.Equal(requestsAfterFirst, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task SearchAsync_BlankQuery_Throws()
    {
        (YoutubeSearchService service, _, _) = Create();

        var ex = await Assert.ThrowsAsync<SearchException>(() => service.SearchAsync("   "));

        Assert.Equal(SearchErrorKind.ApiError, ex.Kind);
    }
}
