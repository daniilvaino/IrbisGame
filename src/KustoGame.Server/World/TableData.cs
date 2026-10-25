using KustoGame.Server.Engines;

namespace KustoGame.Server.World;

public sealed record ColumnInfo(string Name, KqlType Type, string Description)
{
    public ColumnSpec ToSpec() => new(Name, Type);
}

public sealed record TableData(string Name, string Description, IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows)
{
    public IReadOnlyList<ColumnSpec> Specs => Columns.Select(c => c.ToSpec()).ToList();
}

/// <summary>Generated game world: tables plus the day the world is anchored to.</summary>
public sealed record GameWorld(DateTime Today, IReadOnlyList<TableData> Tables)
{
    /// <summary>Start of the day N days before <see cref="Today"/>, UTC.</summary>
    public DateTime Day(int daysAgo) => Today.AddDays(-daysAgo);
}
