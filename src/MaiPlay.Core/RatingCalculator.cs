// Rating coefficients adapted from PaperPig/MaimaiData, Apache-2.0.
// ConvertUtils.achievementToRating at 30ca1c3cce9069051d7d3a244b6623c0a650aae4.
// Uses decimal arithmetic and accepts achievement percentages rather than scaled integers.
namespace MaiPlay.Core;

public sealed record RatingEntry(string ChartId, string Title, int Version, decimal Constant, decimal Achievement)
{
    public int Rating => RatingCalculator.Calculate(Constant, Achievement);
}

public sealed record Best50(IReadOnlyList<RatingEntry> Old35, IReadOnlyList<RatingEntry> New15)
{
    public int OldTotal => Old35.Sum(e => e.Rating);
    public int NewTotal => New15.Sum(e => e.Rating);
    public int Total => OldTotal + NewTotal;
}

public static class RatingCalculator
{
    public static int Calculate(decimal constant, decimal achievement)
    {
        if (constant < 0) throw new ArgumentOutOfRangeException(nameof(constant));
        var value = decimal.Floor(Math.Clamp(achievement, 0, 100.5m) * 10000) / 10000;
        var factor = value switch
        {
            >= 100.5m => 22.4m,
            100.4999m => 22.2m,
            >= 100 => 21.6m,
            99.9999m => 21.4m,
            >= 99.5m => 21.1m,
            >= 99 => 20.8m,
            >= 98 => 20.3m,
            >= 97 => 20m,
            >= 94 => 16.8m,
            >= 90 => 15.2m,
            >= 80 => 13.6m,
            >= 75 => 12m,
            >= 70 => 11.2m,
            >= 60 => 9.6m,
            >= 50 => 8m,
            _ => 0m
        };
        return (int)decimal.Floor(constant * value * factor / 100);
    }

    public static Best50 SelectBest50(IEnumerable<RatingEntry> entries, IReadOnlySet<int> newVersions)
    {
        var ordered = entries.GroupBy(e => e.ChartId, StringComparer.Ordinal).Select(g => g.OrderByDescending(e => e.Rating)
            .ThenByDescending(e => e.Achievement).First()).OrderByDescending(e => e.Rating)
            .ThenByDescending(e => e.Achievement).ThenBy(e => e.ChartId, StringComparer.Ordinal).ToArray();
        return new(ordered.Where(e => !newVersions.Contains(e.Version)).Take(35).ToArray(),
            ordered.Where(e => newVersions.Contains(e.Version)).Take(15).ToArray());
    }
}
