using BabyKusto.Core;
using BabyKusto.Core.Evaluation;
using BabyKusto.Core.Util;
using Kusto.Language.Symbols;

namespace KustoGame.Server.Engines;

/// <summary>
/// Adapter over BabyKusto.Core (github.com/davidnx/baby-kusto-csharp).
/// Runs in its own AssemblyLoadContext with Kusto.Language 11.3, the version BabyKusto was built against.
/// </summary>
public sealed class BabyKustoKqlEngine : IKqlEngine
{
    private readonly BabyKustoEngine _engine = new();
    private readonly List<TableSchema> _schema = [];

    public string Name => EngineNames.BabyKusto;

    public void LoadTable(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<object?[]> rows)
    {
        var symbol = new TableSymbol(name, columns.Select(c => new ColumnSymbol(c.Name, ToSymbol(c.Type))));
        var data = new Column[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var values = new object?[rows.Count];
            for (var r = 0; r < rows.Count; r++) values[r] = rows[r][c];
            data[c] = ColumnHelpers.CreateFromObjectArray(values!, ToSymbol(columns[c].Type));
        }

        _engine.AddGlobalTable(new InMemoryTableSource(symbol, data));
        _schema.Add(new TableSchema(name, columns, rows.Count));
    }

    public EngineResult Run(string query, int maxRows)
    {
        try
        {
            return _engine.Evaluate(query, false, false) switch
            {
                TabularResult tabular => ReadTable(tabular.Value, maxRows),
                ScalarResult scalar => new EngineResult(
                    [new ResultColumn("print_0", TypeName(scalar.Type))],
                    [[KqlValues.Normalize(scalar.Value)]], 1, null),
                null => EngineResult.Fail("Движок не вернул результат."),
                var other => EngineResult.Fail($"Неподдерживаемый тип результата: {other.GetType().Name}"),
            };
        }
        catch (Exception ex)
        {
            return EngineResult.Fail(EngineErrors.Describe(ex));
        }
    }

    private static EngineResult ReadTable(ITableSource table, int maxRows)
    {
        var columns = table.Type.Columns.Select(c => new ResultColumn(c.Name, TypeName(c.Type))).ToList();
        var rows = new List<object?[]>();
        var total = 0;
        foreach (var chunk in table.GetData())
        {
            for (var r = 0; r < chunk.RowCount && rows.Count < maxRows; r++)
            {
                var row = new object?[chunk.Columns.Length];
                for (var c = 0; c < row.Length; c++) row[c] = KqlValues.Normalize(chunk.Columns[c].RawData.GetValue(r));
                rows.Add(row);
            }
            total += chunk.RowCount;
        }

        return new EngineResult(columns, rows, total, null);
    }

    public IReadOnlyList<TableSchema> GetSchema() => _schema;

    private static ScalarSymbol ToSymbol(KqlType type) => type switch
    {
        KqlType.Bool => ScalarTypes.Bool,
        KqlType.Long => ScalarTypes.Long,
        KqlType.Real => ScalarTypes.Real,
        KqlType.String => ScalarTypes.String,
        KqlType.DateTime => ScalarTypes.DateTime,
        KqlType.TimeSpan => ScalarTypes.TimeSpan,
        KqlType.Dynamic => ScalarTypes.Dynamic,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string TypeName(TypeSymbol symbol) => symbol.Name;
}
