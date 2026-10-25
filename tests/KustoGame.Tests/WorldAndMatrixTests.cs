using KustoGame.Server.Engines;
using KustoGame.Server.Matrix;
using KustoGame.Server.World;

namespace KustoGame.Tests;

public sealed class WorldAndMatrixTests
{
    [Fact]
    public void World_is_deterministic_and_only_shifts_with_the_anchor_date()
    {
        var a = WorldGenerator.Generate(new DateTime(2024, 3, 14));
        var b = WorldGenerator.Generate(new DateTime(2024, 3, 14));
        var shifted = WorldGenerator.Generate(new DateTime(2025, 1, 1));

        foreach (var (ta, tb, ts) in a.Tables.Zip(b.Tables, shifted.Tables))
        {
            Assert.Equal(ta.Rows.Count, tb.Rows.Count);
            Assert.InRange(ta.Rows.Count, 5_000, 50_000);
            Assert.Equal(ta.Rows.Count, ts.Rows.Count);
            for (var i = 0; i < ta.Rows.Count; i += 997)
            {
                Assert.Equal(Fingerprint(ta.Rows[i], a.Today), Fingerprint(tb.Rows[i], b.Today));
                Assert.Equal(Fingerprint(ta.Rows[i], a.Today), Fingerprint(ts.Rows[i], shifted.Today));
            }
        }

        // Dates are written relative to the anchor so that the comparison above ignores the calendar shift.
        static string Fingerprint(object?[] row, DateTime today) => string.Join("|", row.Select(v => v switch
        {
            DateTime dt => (dt - today).ToString(),
            System.Text.Json.Nodes.JsonNode node => node.ToJsonString(),
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
        }));
    }

    [Fact]
    public void Story_clues_are_in_place()
    {
        var world = WorldGenerator.Generate(new DateTime(2024, 3, 14));
        var network = world.Tables.Single(t => t.Name == "NetworkLogs");
        var attack = network.Rows.Where(r => (string)r[3]! == WorldGenerator.Story.AttackerIp).ToList();

        Assert.Equal(15, attack.Count);
        Assert.All(attack, r => Assert.Equal(WorldGenerator.Story.VictimId, r[1]));
        Assert.Equal(6, network.Rows.Count(r => (string)r[4]! == WorldGenerator.Story.DropIp));

        var mail = world.Tables.Single(t => t.Name == "EmailEvents");
        Assert.Equal(WorldGenerator.Story.PhishRecipients + 1, mail.Rows.Count(r => (string)r[2]! == WorldGenerator.Story.PhishSender));
    }

    [Theory]
    [InlineData("BabyKusto")]
    [InlineData("KustoLoco")]
    public void Core_operators_are_supported_by_both_engines(string engine)
    {
        var outcomes = OperatorMatrix.Run(EngineFactory.Create(engine)).ToDictionary(o => o.Case.Feature);
        string[] core = ["take", "project", "where (and, ==, >)", "sort by", "count", "summarize count() by", "bin()", "contains",
            "startswith", "matches regex", "extend", "iff()", "ago() / now()", "join kind=inner", "join kind=leftouter", "join kind=leftanti", "union"];

        Assert.All(core, f => Assert.True(outcomes[f].Status == SupportStatus.Supported, $"{engine}: {f} — {outcomes[f].Detail}"));
    }

    [Fact]
    public void BabyKusto_runs_against_its_own_Kusto_Language()
    {
        // With Kusto.Language 12.x BabyKusto misreads `desc` and `$left/$right` (SyntaxKind values moved).
        var outcomes = OperatorMatrix.Run(EngineFactory.Create(EngineNames.BabyKusto)).ToDictionary(o => o.Case.Feature);
        Assert.Equal(SupportStatus.Supported, outcomes["sort by"].Status);
        Assert.Equal(SupportStatus.Supported, outcomes["join (подзапрос, $left/$right)"].Status);
    }
}
