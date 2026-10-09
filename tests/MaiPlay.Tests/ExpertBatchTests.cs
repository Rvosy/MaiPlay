using System.Text.Json;

namespace MaiPlay.Tests;

public sealed class ExpertBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchKeepsScoreFieldsAndReportsOnlyInspectedMotion(bool human)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"MaiPlay-batch-{Guid.NewGuid():N}");
        var root = Path.Combine(directory, "charts");
        var song = Path.Combine(root, "21_Test", "000001_Batch");
        var output = Path.Combine(directory, "report");
        Directory.CreateDirectory(song);
        try
        {
            File.WriteAllText(Path.Combine(song, "maidata.txt"), "&title=Batch\n&first=1\n&lv_5=11.6\n&inote_5=(120){4}1,8,E\n");
            Assert.Equal(0, ExpertBatch.Run(root, output, difficulty: 5, humanPlanner: human));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "summary.json")));
            var summary = document.RootElement;
            var chart = Assert.Single(summary.GetProperty("charts").EnumerateArray());
            Assert.Equal(5, summary.GetProperty("difficulty").GetInt32());
            Assert.Equal(1, summary.GetProperty("allCP").GetInt32());
            Assert.Equal("all-cp", chart.GetProperty("status").GetString());
            Assert.Equal(2, chart.GetProperty("notes").GetInt32());
            Assert.Equal(2, chart.GetProperty("cp").GetInt32());
            Assert.Equal(100, chart.GetProperty("achievement").GetDecimal());
            Assert.Equal(6, chart.GetProperty("dxScore").GetInt32());
            Assert.True(summary.GetProperty("totalRating").GetInt32() > 0);
            Assert.Equal(human ? 1 : 0, summary.GetProperty("motionInspected").GetInt32());
            foreach (var name in new[] { "isExecutable", "isSolved" })
                if (human) Assert.True(chart.GetProperty(name).GetBoolean());
                else Assert.Equal(JsonValueKind.Null, chart.GetProperty(name).ValueKind);
            foreach (var name in new[] { "collisions", "collisionSeconds", "speedLimitViolations", "continuousConnections",
                "relayConnections", "sameHandBreaks", "sameHandContactGapSeconds", "unassignedConnections" })
            {
                if (human)
                {
                    Assert.Equal(0, chart.GetProperty(name).GetDouble());
                    Assert.Equal(0, summary.GetProperty(name).GetDouble());
                }
                else
                {
                    Assert.Equal(JsonValueKind.Null, chart.GetProperty(name).ValueKind);
                    Assert.Equal(JsonValueKind.Null, summary.GetProperty(name).ValueKind);
                }
            }
            if (human)
            {
                Assert.Equal(1, summary.GetProperty("solved").GetInt32());
                Assert.Equal(1, summary.GetProperty("executable").GetInt32());
                Assert.Equal(0, summary.GetProperty("physicalViolations").GetInt32());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, summary.GetProperty("solved").ValueKind);
                Assert.Equal(JsonValueKind.Null, summary.GetProperty("executable").ValueKind);
                Assert.Equal(JsonValueKind.Null, summary.GetProperty("physicalViolations").ValueKind);
            }
            using var individual = JsonDocument.Parse(File.ReadAllText(Path.Combine(output,
                Path.ChangeExtension(chart.GetProperty("path").GetString()!, ".json"))));
            Assert.Equal(chart.GetProperty("isSolved").GetRawText(), individual.RootElement.GetProperty("report").GetProperty("isSolved").GetRawText());
            Assert.True(individual.RootElement.GetProperty("rating").GetInt32() > 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
