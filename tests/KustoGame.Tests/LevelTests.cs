using KustoGame.Server.Engines;
using KustoGame.Server.Levels;
using Xunit.Abstractions;

namespace KustoGame.Tests;

[Collection(GameCollection.Name)]
public sealed class LevelTests(GameFixture game, ITestOutputHelper output)
{
    public static TheoryData<string, string> DeclaredPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var level in new LevelStore(TestPaths.Levels, DateTime.UtcNow.Date).Levels)
        foreach (var engine in level.Engines)
            data.Add(level.Id, engine);
        return data;
    }

    [Theory]
    [MemberData(nameof(DeclaredPairs))]
    public async Task Solution_returns_rows_on_declared_engine(string levelId, string engine)
    {
        var level = game.Levels.Find(levelId)!;
        var (result, error) = await game.Queries.RunForCheckAsync(level.Solution, game.Host.Get(engine), LevelChecker.CompareRows);

        Assert.Null(error?.Message);
        Assert.True(result!.TotalRows > 0, $"{levelId}: эталон на {engine} вернул пустой результат");
        if (level.Check.Mode == CheckModes.Scalar)
            Assert.True(result.TotalRows == 1, $"{levelId}: скалярный уровень, но эталон вернул {result.TotalRows} строк");
    }

    [Theory]
    [MemberData(nameof(DeclaredPairs))]
    public async Task Example_runs_on_declared_engine(string levelId, string engine)
    {
        var level = game.Levels.Find(levelId)!;
        var (result, error) = await game.Queries.RunForCheckAsync(level.Example, game.Host.Get(engine), 1000);

        Assert.Null(error?.Message);
        Assert.True(result!.TotalRows > 0, $"{levelId}: пример на {engine} вернул пустой результат");
    }

    [Theory]
    [MemberData(nameof(DeclaredPairs))]
    public async Task Solution_passes_its_own_check(string levelId, string engine)
    {
        var level = game.Levels.Find(levelId)!;
        var response = await game.Checker.CheckAsync(levelId, level.Solution, engine);
        Assert.True(response.Passed, response.Message);
    }

    [Fact]
    public void Levels_cover_all_chapters_and_count_is_in_range()
    {
        var levels = game.Levels.Levels;
        Assert.InRange(levels.Count, 20, 30);
        Assert.Equal(Enumerable.Range(1, 7), levels.Select(l => l.Chapter).Distinct().Order());
        Assert.All(levels, l =>
        {
            Assert.InRange(l.Hints.Count, 2, 3);
            Assert.False(string.IsNullOrWhiteSpace(l.Theory));
            Assert.False(string.IsNullOrWhiteSpace(l.Example));
            Assert.False(string.IsNullOrWhiteSpace(l.Task));
        });
    }

    /// <summary>
    /// Undeclared engines must really be unable to run the reference: otherwise the level is hidden for nothing.
    /// "Unable" means an error, an empty result, or a result different from the one of a declared engine.
    /// </summary>
    [Fact]
    public async Task Engines_list_is_not_narrower_than_needed()
    {
        var problems = new List<string>();
        foreach (var level in game.Levels.Levels)
        {
            var reference = level.Engines[0];
            foreach (var engine in EngineNames.All.Except(level.Engines))
            {
                var (result, error) = await game.Queries.RunForCheckAsync(level.Solution, game.Host.Get(engine), LevelChecker.CompareRows);
                var (expected, _) = await game.Queries.RunForCheckAsync(level.Solution, game.Host.Get(reference), LevelChecker.CompareRows);
                var verdict = error != null ? $"ошибка: {error.Message}"
                    : result!.TotalRows == 0 ? "пустой результат"
                    : ResultComparer.Compare(expected!, result, level.Check).Passed ? null
                    : "результат отличается от " + reference;
                output.WriteLine($"{level.Id} на {engine}: {verdict ?? "РАБОТАЕТ"}");
                if (verdict == null) problems.Add($"{level.Id} работает на {engine}, но движок не указан в engines");
            }
        }

        Assert.Empty(problems);
    }
}
