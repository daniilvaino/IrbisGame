using KustoGame.Server.Engines;
using KustoGame.Server.Levels;

namespace KustoGame.Tests;

public sealed class ComparerTests
{
    private static EngineResult Result(string[] columns, params object?[][] rows) =>
        new(columns.Select(c => new ResultColumn(c, "string")).ToList(), rows, rows.Length, null);

    private static readonly EngineResult Expected = Result(["Name", "Count"], ["a", 1L], ["b", 2L]);

    [Fact]
    public void Set_mode_ignores_row_and_column_order_and_names()
    {
        var actual = Result(["N", "Who"], [2L, "b"], [1L, "a"]);
        var outcome = ResultComparer.Compare(Expected, actual, new CheckOptions());
        Assert.True(outcome.Passed, outcome.Message);
    }

    [Fact]
    public void Strict_column_names_are_enforced_when_level_requires()
    {
        var actual = Result(["N", "Who"], [2L, "b"], [1L, "a"]);
        var outcome = ResultComparer.Compare(Expected, actual, new CheckOptions { ColumnNames = "strict" });
        Assert.False(outcome.Passed);
        Assert.Equal(["Name", "Count"], outcome.Diff.MissingColumns);
    }

    [Fact]
    public void Diff_lists_missing_and_extra_rows()
    {
        var actual = Result(["Name", "Count"], ["a", 1L], ["c", 3L], ["c", 3L]);
        var outcome = ResultComparer.Compare(Expected, actual, new CheckOptions());

        Assert.False(outcome.Passed);
        Assert.Equal(1, outcome.Diff.MissingCount);
        Assert.Equal("b", outcome.Diff.MissingRows[0][0]);
        Assert.Equal(2, outcome.Diff.ExtraCount);
    }

    [Fact]
    public void Extra_columns_fail_by_default()
    {
        var actual = Result(["Name", "Count", "More"], ["a", 1L, "x"], ["b", 2L, "y"]);
        var outcome = ResultComparer.Compare(Expected, actual, new CheckOptions());
        Assert.False(outcome.Passed);
        Assert.Equal(["More"], outcome.Diff.ExtraColumns);

        Assert.True(ResultComparer.Compare(Expected, actual, new CheckOptions { ExtraColumns = "allow" }).Passed);
    }

    [Fact]
    public void Ordered_mode_reports_first_mismatch()
    {
        var actual = Result(["Name", "Count"], ["b", 2L], ["a", 1L]);
        var outcome = ResultComparer.Compare(Expected, actual, new CheckOptions { Mode = CheckModes.Ordered });
        Assert.False(outcome.Passed);
        Assert.Equal(0, outcome.Diff.FirstOrderMismatch);
    }

    [Fact]
    public void Scalar_mode_accepts_value_in_any_column_and_tolerates_rounding()
    {
        var expected = Result(["Avg"], [3.1415926]);
        Assert.True(ResultComparer.Compare(expected, Result(["x", "y"], ["label", 3.14159261]), new CheckOptions { Mode = CheckModes.Scalar }).Passed);
        Assert.False(ResultComparer.Compare(expected, Result(["y"], [3.2]), new CheckOptions { Mode = CheckModes.Scalar }).Passed);
        Assert.False(ResultComparer.Compare(expected, Result(["y"], [3.1415926], [1.0]), new CheckOptions { Mode = CheckModes.Scalar }).Passed);
    }

    [Fact]
    public void Shape_mode_checks_columns_and_row_count_only()
    {
        var actual = Result(["Count", "Name"], ["zz", 9L], ["yy", 8L]);
        Assert.True(ResultComparer.Compare(Expected, actual, new CheckOptions { Mode = CheckModes.Shape }).Passed);
        Assert.False(ResultComparer.Compare(Expected, Result(["Name", "Count"], ["a", 1L]), new CheckOptions { Mode = CheckModes.Shape }).Passed);
    }
}
