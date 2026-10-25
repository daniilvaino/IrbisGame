namespace KustoGame.Server.Engines;

/// <summary>Column definition of a table loaded into an engine.</summary>
public sealed record ColumnSpec(string Name, KqlType Type);

/// <summary>Column of a query result: name and KQL type name (string, long, datetime, ...).</summary>
public sealed record ResultColumn(string Name, string Type);

public sealed record TableSchema(string Name, IReadOnlyList<ColumnSpec> Columns, int RowCount);

/// <summary>
/// Raw result of a query as returned by an engine. Values are already normalized
/// (see <see cref="KqlValues"/>), rows are capped by the caller-provided limit,
/// <see cref="TotalRows"/> holds the full row count.
/// </summary>
public sealed record EngineResult(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int TotalRows,
    string? Error)
{
    public static EngineResult Fail(string error) => new([], [], 0, error);
}

public interface IKqlEngine
{
    /// <summary>Engine identifier: "BabyKusto" or "KustoLoco".</summary>
    string Name { get; }

    void LoadTable(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<object?[]> rows);

    /// <summary>Runs a query synchronously. Engine failures are returned in <see cref="EngineResult.Error"/>.</summary>
    EngineResult Run(string query, int maxRows);

    IReadOnlyList<TableSchema> GetSchema();
}
