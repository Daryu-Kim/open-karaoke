using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.Tests;

public class SearchQueryStrategyTests
{
    [Theory]
    [InlineData("  아이유  밤편지  ", "아이유 밤편지")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  \t아이유\n  ", "아이유")]
    public void Normalize_CollapsesAndTrimsWhitespace(string raw, string expected)
    {
        Assert.Equal(expected, SearchQueryStrategy.Normalize(raw));
    }

    [Fact]
    public void BuildPlan_PrimaryIsNormalizedQuery()
    {
        SearchPlan plan = SearchQueryStrategy.BuildPlan("  아이유 밤편지 ");
        Assert.Equal("아이유 밤편지", plan.Primary);
    }

    [Fact]
    public void BuildPlan_FallbacksAppendKaraokeVariantsInOrder()
    {
        SearchPlan plan = SearchQueryStrategy.BuildPlan("아이유 밤편지");
        Assert.Equal(
            new[] { "아이유 밤편지 노래방", "아이유 밤편지 MR", "아이유 밤편지 instrumental" },
            plan.Fallbacks);
    }
}
