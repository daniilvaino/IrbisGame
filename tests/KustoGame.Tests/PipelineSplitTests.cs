using KustoGame.Server.Analysis;
using KustoGame.Server.World;

namespace KustoGame.Tests;

public sealed class PipelineSplitTests
{
    private static readonly KqlAnalyzer Analyzer = new(WorldGenerator.Generate(new DateTime(2024, 3, 14)).Tables.Take(1));

    [Fact]
    public void Splits_top_level_pipes_into_cumulative_prefixes()
    {
        var stages = Analyzer.SplitPipeline("T | where x > 1 | summarize count() by y");

        Assert.Equal(["T", "T | where x > 1", "T | where x > 1 | summarize count() by y"], stages.Select(s => s.Query));
        Assert.Equal(["T", "where x > 1", "summarize count() by y"], stages.Select(s => s.Label));
    }

    [Fact]
    public void Pipes_inside_strings_comments_and_subqueries_are_not_split()
    {
        const string query = """
            NetworkLogs // first | not a stage
            | where Url contains "a|b"
            | join (Employees | where Floor > 3 | project EmployeeId) on $left.UserId == $right.EmployeeId
            | count
            """;
        var stages = Analyzer.SplitPipeline(query);

        Assert.Equal(4, stages.Count);
        Assert.Equal("NetworkLogs", stages[0].Label);
        Assert.Equal("where Url contains \"a|b\"", stages[1].Label);
        Assert.StartsWith("join (Employees | where Floor > 3", stages[2].Label);
        Assert.EndsWith("| count", stages[3].Query);
    }

    [Fact]
    public void Let_statements_stay_in_every_prefix()
    {
        var stages = Analyzer.SplitPipeline("let limit = 5;\nEmployees | where Floor > limit | take 3");

        Assert.Equal(3, stages.Count);
        Assert.All(stages, s => Assert.StartsWith("let limit = 5;", s.Query));
        Assert.Equal("Employees", stages[0].Label);
        Assert.Equal(2, stages[0].Position.Line);
    }

    [Fact]
    public void Multiline_query_positions_point_at_operators()
    {
        const string query = "Employees\n| where Floor > 3\n| count";
        var stages = Analyzer.SplitPipeline(query);

        Assert.Equal(query.IndexOf("where", StringComparison.Ordinal), stages[1].Position.Start);
        Assert.Equal(2, stages[1].Position.Line);
        Assert.Equal(3, stages[1].Position.Column);
        Assert.Equal(3, stages[2].Position.Line);
    }

    [Fact]
    public void Union_source_is_one_stage()
    {
        var stages = Analyzer.SplitPipeline("union (A | project x), (B | project x) | sort by x");
        Assert.Equal(2, stages.Count);
        Assert.Equal("union (A | project x), (B | project x)", stages[0].Label);
    }

    [Fact]
    public void Signature_help_tracks_the_active_argument()
    {
        const string query = "Employees | extend X = extract(@\"(\\d+)\", 1, Name)";
        var afterFirstComma = query.IndexOf(", 1", StringComparison.Ordinal) + 2;

        var help = Analyzer.GetSignatureHelp(query, afterFirstComma)!;
        Assert.StartsWith("extract(", help.Signatures[help.ActiveSignature].Label);
        Assert.Equal(1, help.ActiveParameter);

        var unfinished = Analyzer.GetSignatureHelp("Employees | summarize dcount(", "Employees | summarize dcount(".Length)!;
        Assert.StartsWith("dcount(", unfinished.Signatures[0].Label);
        Assert.Equal(0, unfinished.ActiveParameter);

        Assert.Null(Analyzer.GetSignatureHelp("Employees | take 5", 5));
    }

    [Fact]
    public void Control_commands_are_detected()
    {
        Assert.True(KqlAnalyzer.IsControlCommand(".show tables"));
        Assert.True(KqlAnalyzer.IsControlCommand("   \n .drop table T"));
        Assert.False(KqlAnalyzer.IsControlCommand("Employees | where Name == \".show\""));
    }
}
