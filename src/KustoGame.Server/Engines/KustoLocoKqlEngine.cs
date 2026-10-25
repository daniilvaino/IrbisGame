using Kusto.Language.Symbols;
using KustoLoco.Core;
using KustoLoco.Core.DataSource;
using KustoLoco.Core.DataSource.Columns;
using KustoLoco.Core.Util;

namespace KustoGame.Server.Engines;

/// <summary>Adapter over KustoLoco.Core (github.com/NeilMacMullen/kusto-loco).</summary>
public sealed class KustoLocoKqlEngine : IKqlEngine
{
    private readonly KustoQueryContext _context = new();
    private readonly List<TableSchema> _schema = [];

    public string Name => EngineNames.KustoLoco;

    public void LoadTable(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<object?[]> rows)
    {
        var symbol = new TableSymbol(name, columns.Select(c => new ColumnSymbol(c.Name, KqlSymbols.ToSymbol(c.Type))));
        var data = new BaseColumn[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var values = new object?[rows.Count];
            for (var r = 0; r < rows.Count; r++) values[r] = rows[r][c];
            data[c] = ColumnHelpers.CreateFromObjectArray(values!, KqlSymbols.ToSymbol(columns[c].Type));
        }

        _context.AddTable(new InMemoryTableSource(symbol, data));
        _schema.Add(new TableSchema(name, columns, rows.Count));
    }

    public EngineResult Run(string query, int maxRows)
    {
        try
        {
            // RunQuery only wraps the synchronous evaluation into a completed task.
            var result = _context.RunQuery(query).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result.Error)) return EngineResult.Fail(result.Error);

            var columns = result.Table.Type.Columns
                .Select(c => new ResultColumn(c.Name, c.Type.Name))
                .ToList();
            var rows = result.EnumerateRows()
                .Take(maxRows)
                .Select(row => row.Select(KqlValues.Normalize).ToArray())
                .ToList();
            return new EngineResult(columns, rows, result.RowCount, null);
        }
        catch (Exception ex)
        {
            return EngineResult.Fail(EngineErrors.Describe(ex));
        }
    }

    public IReadOnlyList<TableSchema> GetSchema() => _schema;
}
