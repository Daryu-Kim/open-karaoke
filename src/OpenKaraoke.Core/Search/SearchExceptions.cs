namespace OpenKaraoke.Core.Search;

public enum SearchErrorKind
{
    ApiKeyMissing,
    ApiKeyInvalid,
    RateLimited,
    QuotaExceeded,
    ApiError,
    NetworkError,
}

public sealed class SearchException : Exception
{
    public SearchErrorKind Kind { get; }

    public SearchException(SearchErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }
}
