using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenKaraoke.Core.Caching;
using OpenKaraoke.Core.RateLimit;
using OpenKaraoke.Core.Time;

namespace OpenKaraoke.Core.Search;

public sealed record YoutubeSearchOptions
{
    /// <summary>Max results kept after ranking (search.list maxResults + enrichment).</summary>
    public int MaxResults { get; init; } = 30;

    /// <summary>How many fallback queries ("노래방", "MR", ...) may run when the primary is empty.</summary>
    public int MaxFallbackAttempts { get; init; } = 1;

    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromMinutes(10);

    public int CacheMaxEntries { get; init; } = 200;

    public int MaxRequestsPerMinute { get; init; } = 15;
}

/// <summary>
/// Coordinates a quota-conscious YouTube search: normalized query plan, cache lookup,
/// sliding-window rate limit, primary + fallback calls, then a single videos.list call
/// to enrich hits with duration/embeddability.
/// </summary>
public sealed class YoutubeSearchService
{
    private const string SearchUrl = "youtube/v3/search";
    private const string VideosUrl = "youtube/v3/videos";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IClock _clock;
    private readonly SearchCache _cache;
    private readonly RateLimiter _rateLimiter;
    private readonly YoutubeSearchOptions _options;

    public YoutubeSearchService(
        HttpClient http,
        string apiKey,
        IClock clock,
        SearchCache? cache = null,
        RateLimiter? rateLimiter = null,
        YoutubeSearchOptions? options = null)
    {
        _http = http;
        _apiKey = apiKey ?? string.Empty;
        _clock = clock;
        _options = options ?? new YoutubeSearchOptions();
        _cache = cache ?? new SearchCache(_options.CacheTtl, _options.CacheMaxEntries);
        _rateLimiter = rateLimiter ?? new RateLimiter(_options.MaxRequestsPerMinute);
    }

    public async Task<SearchResponse> SearchAsync(string rawQuery, CancellationToken cancellationToken = default)
    {
        string query = SearchQueryStrategy.Normalize(rawQuery);
        if (query.Length == 0)
        {
            throw new SearchException(SearchErrorKind.ApiError, "검색어를 입력해 주세요.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new SearchException(
                SearchErrorKind.ApiKeyMissing,
                "YouTube API 키가 설정되지 않았습니다. 설정에서 API 키를 입력해 주세요.");
        }

        IReadOnlyList<SearchVideoItem>? cached = _cache.Get(query, _clock.UtcNow);
        if (cached is not null)
        {
            return new SearchResponse { Items = cached, QueryUsed = query, FromCache = true };
        }

        if (!_rateLimiter.TryAcquire(_clock.UtcNow))
        {
            throw new SearchException(
                SearchErrorKind.RateLimited,
                "검색 요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.");
        }

        SearchPlan plan = SearchQueryStrategy.BuildPlan(query);
        var candidates = new List<string> { plan.Primary };
        candidates.AddRange(plan.Fallbacks.Take(Math.Max(0, _options.MaxFallbackAttempts)));

        IReadOnlyList<SearchVideoItem> results = Array.Empty<SearchVideoItem>();
        string used = query;

        foreach (string candidate in candidates)
        {
            results = await SearchCoreAsync(candidate, cancellationToken).ConfigureAwait(false);
            used = candidate;
            if (results.Count > 0)
            {
                break;
            }
        }

        _cache.Set(query, results, _clock.UtcNow);
        return new SearchResponse { Items = results, QueryUsed = used, FromCache = false };
    }

    private async Task<IReadOnlyList<SearchVideoItem>> SearchCoreAsync(string query, CancellationToken ct)
    {
        string url = $"{SearchUrl}?part=snippet&type=video&maxResults=50&q={Uri.EscapeDataString(query)}";
        string body = await GetStringAsync(url, ct).ConfigureAwait(false);

        List<SearchVideoItem> items = YouTubeDataParser.ParseSearchResponse(body).ToList();
        if (items.Count == 0)
        {
            return Array.Empty<SearchVideoItem>();
        }

        await EnrichWithDurationsAsync(items, ct).ConfigureAwait(false);

        return new SearchResultScorer()
            .RankAndDedupe(items, _options.MaxResults);
    }

    private async Task EnrichWithDurationsAsync(List<SearchVideoItem> items, CancellationToken ct)
    {
        for (int start = 0; start < items.Count; start += 50)
        {
            List<SearchVideoItem> batch = items.Skip(start).Take(50).ToList();
            string ids = string.Join(",", batch.Select(i => i.VideoId));
            string url = $"{VideosUrl}?part=contentDetails,status&id={Uri.EscapeDataString(ids)}";
            string body = await GetStringAsync(url, ct).ConfigureAwait(false);

            IReadOnlyDictionary<string, (int DurationSeconds, bool Embeddable)> details =
                YouTubeDataParser.ParseVideoDetailsResponse(body);

            for (int i = 0; i < batch.Count; i++)
            {
                if (!details.TryGetValue(batch[i].VideoId, out var detail))
                {
                    continue;
                }

                // SearchVideoItem is an immutable record — replace with an enriched copy.
                items[start + i] = batch[i] with
                {
                    DurationSeconds = detail.DurationSeconds,
                    Embeddable = detail.Embeddable,
                };
            }
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        string full = $"{url}&key={Uri.EscapeDataString(_apiKey)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(full, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SearchException(SearchErrorKind.NetworkError, "네트워크 연결이 원활하지 않습니다. 잠시 후 다시 시도해 주세요.");
        }
        catch (HttpRequestException)
        {
            throw new SearchException(SearchErrorKind.NetworkError, "네트워크 연결이 원활하지 않습니다. 잠시 후 다시 시도해 주세요.");
        }

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDetectError(body);
            if (error is not null)
            {
                throw error;
            }

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new SearchException(SearchErrorKind.ApiKeyInvalid, "YouTube API 키가 올바르지 않습니다. 설정을 확인해 주세요."),
                HttpStatusCode.TooManyRequests =>
                    new SearchException(SearchErrorKind.RateLimited, "검색 요청이 너무 많습니다. 잠시 후 다시 시도해 주세요."),
                _ => new SearchException(SearchErrorKind.ApiError, $"YouTube 서버 오류가 발생했습니다. ({(int)response.StatusCode})"),
            };
        }

        return body;
    }

    private static SearchException? TryDetectError(string body)
    {
        try
        {
            var detected = YouTubeDataParser.DetectError(body);
            return detected is null ? null : new SearchException(detected.Value.Kind, detected.Value.Message);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
