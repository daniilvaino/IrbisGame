using System.Diagnostics;
using KustoGame.Server.Engines;

namespace KustoGame.Server.Analysis;

/// <param name="Source">engine — ошибка движка; command — управляющая команда; timeout; input — пустой/слишком длинный запрос.</param>
public sealed record QueryError(string Message, string Source, TextPosition? Position);

public sealed record QueryResponse(
    string Engine,
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int TotalRows,
    bool Truncated,
    double ElapsedMs,
    QueryError? Error,
    IReadOnlyList<QueryDiagnostic> Diagnostics);

public sealed record StageResponse(int Index, string Label, string Query, TextPosition Position, QueryResponse Result);

public sealed record StepsResponse(string Engine, IReadOnlyList<StageResponse> Stages, QueryError? Error);

public sealed class QueryService(EngineHost host, GameOptions options)
{
    public const int MaxQueryLength = 20_000;
    public const int StageRows = 200;

    /// <summary>Runs a query with the timeout; never throws for query problems, they come back in <see cref="QueryResponse.Error"/>.</summary>
    public async Task<QueryResponse> RunAsync(string? query, string? engineName, int? maxRows = null)
    {
        var engine = host.Get(engineName);
        if (Validate(query) is { } invalid) return Failed(engine.Name, invalid, []);

        var diagnostics = host.Analyzer.GetDiagnostics(query!);
        var (result, elapsed, error) = await ExecuteAsync(engine, query!, maxRows ?? options.MaxRows);
        if (error != null) return Failed(engine.Name, error, diagnostics, elapsed);

        if (result!.Error != null)
        {
            var position = KqlAnalyzer.LocateEngineError(query!, result.Error)
                           ?? diagnostics.FirstOrDefault(d => d.Severity == "Error")?.Position;
            return Failed(engine.Name, new QueryError(result.Error, "engine", position), diagnostics, elapsed);
        }

        return new QueryResponse(engine.Name, result.Columns, result.Rows, result.TotalRows,
            result.TotalRows > result.Rows.Count, elapsed, null, diagnostics);
    }

    /// <summary>Runs every prefix of the top-level pipeline; stops after the first failing stage.</summary>
    public async Task<StepsResponse> RunStepsAsync(string? query, string? engineName)
    {
        var engine = host.Get(engineName);
        if (Validate(query) is { } invalid) return new StepsResponse(engine.Name, [], invalid);

        var stages = host.Analyzer.SplitPipeline(query!);
        if (stages.Count == 0)
            return new StepsResponse(engine.Name, [], new QueryError("Не удалось выделить конвейер: последний оператор запроса не является табличным выражением.", "input", null));

        var responses = new List<StageResponse>();
        foreach (var stage in stages)
        {
            var result = await RunAsync(stage.Query, engine.Name, StageRows);
            responses.Add(new StageResponse(stage.Index, stage.Label, stage.Query, stage.Position, result));
            if (result.Error != null) break;
        }

        return new StepsResponse(engine.Name, responses, null);
    }

    /// <summary>Unrestricted execution for answer checking (full result needed), still bounded by the timeout.</summary>
    public async Task<(EngineResult? Result, QueryError? Error)> RunForCheckAsync(string query, IKqlEngine engine, int maxRows)
    {
        if (Validate(query) is { } invalid) return (null, invalid);
        var (result, _, error) = await ExecuteAsync(engine, query, maxRows);
        if (error != null) return (null, error);
        if (result!.Error != null)
            return (null, new QueryError(result.Error, "engine", KqlAnalyzer.LocateEngineError(query, result.Error)));
        return (result, null);
    }

    private async Task<(EngineResult? Result, double ElapsedMs, QueryError? Error)> ExecuteAsync(IKqlEngine engine, string query, int maxRows)
    {
        var sw = Stopwatch.StartNew();
        // Neither engine accepts a CancellationToken: on timeout the evaluation keeps running in the background,
        // the client gets its answer right away.
        var task = Task.Run(() => engine.Run(query, maxRows));
        try
        {
            var result = await task.WaitAsync(TimeSpan.FromSeconds(options.QueryTimeoutSeconds));
            return (result, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (TimeoutException)
        {
            return (null, sw.Elapsed.TotalMilliseconds,
                new QueryError($"Запрос выполнялся дольше {options.QueryTimeoutSeconds} с и был прерван.", "timeout", null));
        }
    }

    private static QueryError? Validate(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new QueryError("Пустой запрос.", "input", null);
        if (query.Length > MaxQueryLength) return new QueryError($"Запрос длиннее {MaxQueryLength} символов.", "input", null);
        if (KqlAnalyzer.IsControlCommand(query))
            return new QueryError("Управляющие команды (начинающиеся с «.») запрещены: здесь можно только читать данные запросами.", "command",
                KqlAnalyzer.Position(query, query.Length - query.TrimStart().Length, 1));
        return null;
    }

    private static QueryResponse Failed(string engine, QueryError error, IReadOnlyList<QueryDiagnostic> diagnostics, double elapsed = 0) =>
        new(engine, [], [], 0, false, elapsed, error, diagnostics);
}
