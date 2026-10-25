using KustoGame.Server.Analysis;
using KustoGame.Server.Engines;

namespace KustoGame.Server.Levels;

public sealed record SolutionView(string Query, IReadOnlyList<ResultColumn> Columns, IReadOnlyList<object?[]> Rows, int TotalRows);

public sealed record CheckResponse(
    string LevelId,
    string Engine,
    bool Passed,
    string Message,
    ResultDiff? Diff,
    QueryError? Error,
    SolutionView? Solution);

/// <summary>Runs the player's query and the level's reference query on the same engine and compares the results.</summary>
public sealed class LevelChecker(LevelStore levels, EngineHost host, QueryService queries, GameOptions options)
{
    /// <summary>Enough for every table of the world: comparisons need complete results.</summary>
    public const int CompareRows = 100_000;

    public async Task<CheckResponse> CheckAsync(string levelId, string? query, string? engineName)
    {
        var level = levels.Find(levelId) ?? throw new KeyNotFoundException($"Уровень {levelId} не найден.");
        var engine = host.Get(engineName);
        if (!level.Engines.Contains(engine.Name))
            return new CheckResponse(level.Id, engine.Name, false,
                $"Этот уровень недоступен на движке {engine.Name}: нужен {string.Join(" или ", level.Engines)}.", null, null, null);

        var (actual, playerError) = await queries.RunForCheckAsync(query ?? "", engine, CompareRows);
        if (playerError != null)
            return new CheckResponse(level.Id, engine.Name, false, "Запрос завершился ошибкой — исправьте её и проверьте снова.", null, playerError, null);

        var (expected, referenceError) = await queries.RunForCheckAsync(level.Solution, engine, CompareRows);
        if (referenceError != null)
            return new CheckResponse(level.Id, engine.Name, false, $"Эталон уровня не выполнился на {engine.Name}: {referenceError.Message}", null, null, null);

        var outcome = ResultComparer.Compare(expected!, actual!, level.Check);
        return new CheckResponse(level.Id, engine.Name, outcome.Passed, outcome.Message, outcome.Diff, null,
            outcome.Passed ? View(level.Solution, expected!) : null);
    }

    /// <summary>Gives the reference query up (the "сдаться" button); the client awards no points.</summary>
    public async Task<SolutionView> RevealAsync(string levelId, string? engineName)
    {
        var level = levels.Find(levelId) ?? throw new KeyNotFoundException($"Уровень {levelId} не найден.");
        var engine = host.Get(engineName);
        var (expected, _) = level.Engines.Contains(engine.Name)
            ? await queries.RunForCheckAsync(level.Solution, engine, options.MaxRows)
            : (null, null);
        return expected == null ? new SolutionView(level.Solution, [], [], 0) : View(level.Solution, expected);
    }

    private SolutionView View(string query, EngineResult result) =>
        new(query, result.Columns, result.Rows.Take(options.MaxRows).ToList(), result.TotalRows);
}
