namespace OpenKaraoke.Core.Search;

/// <summary>Plan describing which YouTube queries to run for a user query.</summary>
public sealed record SearchPlan(string Primary, IReadOnlyList<string> Fallbacks);

/// <summary>
/// Builds the query plan. Only the primary query is issued normally; fallbacks are
/// attempted one at a time (configurable) when the primary yields no usable results,
/// to keep API quota consumption low.
/// </summary>
public static class SearchQueryStrategy
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public static SearchPlan BuildPlan(string rawQuery)
    {
        string primary = Normalize(rawQuery);
        return new SearchPlan(
            primary,
            new[]
            {
                $"{primary} 노래방",
                $"{primary} MR",
                $"{primary} instrumental",
            });
    }
}
