using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.Tests;

public class YouTubeDataParserTests
{
    [Fact]
    public void ParseSearchResponse_ExtractsItems()
    {
        const string json = """
        {
          "items": [
            {
              "id": { "kind": "youtube#video", "videoId": "abc123" },
              "snippet": {
                "title": "밤편지 (노래방 MR)",
                "channelTitle": "MR채널",
                "thumbnails": { "default": { "url": "http://x/default.jpg" },
                                "medium": { "url": "http://x/medium.jpg" } }
              }
            },
            {
              "id": { "kind": "youtube#channel", "channelId": "ch1" },
              "snippet": { "title": "채널", "channelTitle": "채널" }
            }
          ]
        }
        """;

        IReadOnlyList<SearchVideoItem> items = YouTubeDataParser.ParseSearchResponse(json);

        Assert.Single(items);
        Assert.Equal("abc123", items[0].VideoId);
        Assert.Equal("밤편지 (노래방 MR)", items[0].Title);
        Assert.Equal("MR채널", items[0].ChannelTitle);
        Assert.Equal("http://x/medium.jpg", items[0].ThumbnailUrl);
    }

    [Fact]
    public void ParseSearchResponse_MissingItems_ReturnsEmpty()
    {
        Assert.Empty(YouTubeDataParser.ParseSearchResponse("{}"));
    }

    [Theory]
    [InlineData("PT4M13S", 253)]
    [InlineData("PT1H2M3S", 3723)]
    [InlineData("P1DT2H", 93600)]
    [InlineData("PT0S", 0)]
    [InlineData("PT1M", 60)]
    [InlineData(null, 0)]
    [InlineData("garbage", 0)]
    public void ParseIso8601Duration_ConvertsToSeconds(string? iso, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, YouTubeDataParser.ParseIso8601Duration(iso));
    }

    [Fact]
    public void ParseVideoDetailsResponse_MapsDurationAndEmbeddability()
    {
        const string json = """
        {
          "items": [
            {
              "id": "v1",
              "contentDetails": { "duration": "PT4M13S" },
              "status": { "embeddable": true }
            },
            {
              "id": "v2",
              "contentDetails": { "duration": "PT1H2M3S" },
              "status": { "embeddable": false }
            },
            { "id": "v3", "contentDetails": {}, "status": {} }
          ]
        }
        """;

        IReadOnlyDictionary<string, (int DurationSeconds, bool Embeddable)> map =
            YouTubeDataParser.ParseVideoDetailsResponse(json);

        Assert.Equal(3, map.Count);
        Assert.Equal((253, true), map["v1"]);
        Assert.Equal((3723, false), map["v2"]);
        Assert.Equal((0, true), map["v3"]);
    }

    [Fact]
    public void DetectError_QuotaExceeded_MapsKind()
    {
        const string json = """
        {
          "error": {
            "code": 403,
            "message": "The request cannot be completed because you have exceeded your quota.",
            "errors": [{ "message": "quota", "domain": "youtube.quota", "reason": "quotaExceeded" }]
          }
        }
        """;

        (SearchErrorKind Kind, string Message)? error = YouTubeDataParser.DetectError(json);

        Assert.NotNull(error);
        Assert.Equal(SearchErrorKind.QuotaExceeded, error.Value.Kind);
    }

    [Fact]
    public void DetectError_InvalidKey_ReturnsApiKeyInvalid()
    {
        const string json = """
        {
          "error": {
            "code": 403,
            "message": "Bad Request",
            "errors": [{ "reason": "keyInvalid" }]
          }
        }
        """;

        (SearchErrorKind Kind, string Message)? error = YouTubeDataParser.DetectError(json);

        Assert.NotNull(error);
        Assert.Equal(SearchErrorKind.ApiKeyInvalid, error.Value.Kind);
    }

    [Fact]
    public void DetectError_NoErrorPayload_ReturnsNull()
    {
        Assert.Null(YouTubeDataParser.DetectError("{ \"items\": [] }"));
    }
}
