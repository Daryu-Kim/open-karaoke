using System.Globalization;
using System.Text.Json;

namespace OpenKaraoke.Core.Search;

/// <summary>
/// Pure parsing of YouTube Data API v3 JSON payloads so the logic is unit-testable
/// without network access.
/// </summary>
public static class YouTubeDataParser
{
    public static IReadOnlyList<SearchVideoItem> ParseSearchResponse(string json)
    {
        var results = new List<SearchVideoItem>();

        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out JsonElement items))
        {
            return results;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out JsonElement id) ||
                !id.TryGetProperty("videoId", out JsonElement videoId) ||
                videoId.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(videoId.GetString()))
            {
                continue;
            }

            if (!item.TryGetProperty("snippet", out JsonElement snippet))
            {
                continue;
            }

            string title = GetString(snippet, "title") ?? string.Empty;
            string channel = GetString(snippet, "channelTitle") ?? string.Empty;
            string thumb = GetThumbnailUrl(snippet);

            results.Add(new SearchVideoItem
            {
                VideoId = videoId.GetString()!,
                Title = title,
                ChannelTitle = channel,
                ThumbnailUrl = thumb,
            });
        }

        return results;
    }

    /// <summary>Returns videoId → (durationSeconds, embeddable) from a videos.list payload.</summary>
    public static IReadOnlyDictionary<string, (int DurationSeconds, bool Embeddable)> ParseVideoDetailsResponse(string json)
    {
        var map = new Dictionary<string, (int, bool)>(StringComparer.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out JsonElement items))
        {
            return map;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? videoId = item.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(videoId))
            {
                continue;
            }

            int duration = 0;
            bool embeddable = true;

            if (item.TryGetProperty("contentDetails", out JsonElement content) &&
                content.TryGetProperty("duration", out JsonElement durationEl))
            {
                duration = ParseIso8601Duration(durationEl.GetString());
            }

            if (item.TryGetProperty("status", out JsonElement status) &&
                status.TryGetProperty("embeddable", out JsonElement embedEl) &&
                embedEl.ValueKind == JsonValueKind.False)
            {
                embeddable = false;
            }

            map[videoId] = (duration, embeddable);
        }

        return map;
    }

    /// <summary>Maps an API error payload to a kind, or null when the payload is not an error.</summary>
    public static (SearchErrorKind Kind, string Message)? DetectError(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("error", out JsonElement error))
        {
            return null;
        }

        string? message = GetString(error, "message");
        string? reason = null;
        if (error.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in errors.EnumerateArray())
            {
                reason = GetString(e, "reason");
                if (reason is not null)
                {
                    break;
                }
            }
        }

        long code = error.TryGetProperty("code", out JsonElement codeEl) && codeEl.ValueKind == JsonValueKind.Number
            ? codeEl.GetInt64()
            : 0;

        var kind = reason switch
        {
            "quotaExceeded" => SearchErrorKind.QuotaExceeded,
            "keyInvalid" or "ipRefererBlocked" or "forbidden" when code == 403 => SearchErrorKind.ApiKeyInvalid,
            _ => code == 403 || code == 429 ? SearchErrorKind.RateLimited : SearchErrorKind.ApiError,
        };

        return (kind, message ?? "YouTube API 오류");
    }

    /// <summary>Parses ISO-8601 durations such as "PT4M13S" / "P1DT2H" into seconds.</summary>
    public static int ParseIso8601Duration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return 0;
        }

        // Expect the common form: P[nD]T[nH][nM][nS]
        int days = 0, hours = 0, minutes = 0;
        double seconds = 0;

        string body = iso.Trim();
        if (!body.StartsWith("P", StringComparison.Ordinal))
        {
            return 0;
        }

        int timeIndex = body.IndexOf('T');
        string datePart = timeIndex >= 0 ? body[1..timeIndex] : body[1..];
        string timePart = timeIndex >= 0 ? body[(timeIndex + 1)..] : string.Empty;

        if (TryConsumeNumber(datePart, 'D', out double d)) days = (int)d;
        if (TryConsumeNumber(timePart, 'H', out double h)) hours = (int)h;
        if (TryConsumeNumber(timePart, 'M', out double m)) minutes = (int)m;
        if (TryConsumeNumber(timePart, 'S', out double s)) seconds = s;

        return (int)Math.Round(((days * 24 + hours) * 60 + minutes) * 60 + seconds);
    }

    private static bool TryConsumeNumber(string text, char unit, out double value)
    {
        value = 0;
        int idx = text.IndexOf(unit);
        if (idx <= 0)
        {
            return false;
        }

        string numberPart = text[..idx];
        int start = numberPart.LastIndexOfAny(new[] { 'T', 'D', 'H', 'M', 'S' });
        numberPart = start >= 0 ? numberPart[(start + 1)..] : numberPart;

        return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string GetThumbnailUrl(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out JsonElement thumbs))
        {
            return string.Empty;
        }

        foreach (string quality in new[] { "medium", "high", "default", "standard" })
        {
            if (thumbs.TryGetProperty(quality, out JsonElement t) &&
                t.TryGetProperty("url", out JsonElement url) &&
                url.ValueKind == JsonValueKind.String)
            {
                return url.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string? GetString(JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }

        return null;
    }
}
