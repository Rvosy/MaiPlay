using MaiPlay.Core;

namespace MaiPlay.Tests;

public sealed class NormalHoldSweepTests
{
    [Theory]
    [MemberData(nameof(HumanHoldSweepTests.ChordToHoldCases), MemberType = typeof(HumanHoldSweepTests))]
    [MemberData(nameof(HumanHoldSweepTests.OccupiedHandCases), MemberType = typeof(HumanHoldSweepTests))]
    public void SweepReachesAndKeepsItsFinalHold(string source)
    {
        HumanHoldSweepTests.Verify(NormalHandPlanner.Plan(SimaiParser.ParseChart(source, first: 1)));
    }
}
