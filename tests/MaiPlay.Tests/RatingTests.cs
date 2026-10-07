using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class RatingTests
{
    [Theory]
    [InlineData("101", 288)]
    [InlineData("100.5", 288)]
    [InlineData("100.4999", 285)]
    [InlineData("100.4998", 277)]
    [InlineData("100", 276)]
    [InlineData("99.9999", 273)]
    [InlineData("99.9998", 270)]
    [InlineData("99.5", 268)]
    [InlineData("50", 51)]
    [InlineData("49.9999", 0)]
    public void RatingMatchesReferenceBoundariesAndCapsAchievement(string achievement, int expected) =>
        Assert.Equal(expected, RatingCalculator.Calculate(12.8m, decimal.Parse(achievement, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Best50KeepsOldAndNewQuotasAndUsesEachChartOnce()
    {
        var entries = Enumerable.Range(0, 65).Select(i => new RatingEntry(i.ToString(), $"Song {i}", i < 40 ? 24 : i < 55 ? 25 : 26, 12, 101)).ToList();
        entries.Add(entries[0] with { Achievement = 99 });
        var result = RatingCalculator.SelectBest50(entries, new HashSet<int> { 25, 26 });
        Assert.Equal(35, result.Old35.Count);
        Assert.Equal(15, result.New15.Count);
        Assert.Equal(13500, result.Total);
        Assert.All(result.Old35, e => Assert.Equal(24, e.Version));
        Assert.All(result.New15, e => Assert.Contains(e.Version, new[] { 25, 26 }));
        var sparse = RatingCalculator.SelectBest50(entries.Where(e => e.Version == 24), new HashSet<int> { 25, 26 });
        Assert.Empty(sparse.New15);
        Assert.Equal(35, sparse.Old35.Count);
    }
}
