using System.Diagnostics;
using KustoGame.Server.Analysis;
using KustoGame.Server.World;

namespace KustoGame.Server.Engines;

public sealed class GameOptions
{
    /// <summary>Default engine: "BabyKusto" or "KustoLoco" (appsettings "Engine").</summary>
    public string Engine { get; set; } = EngineNames.BabyKusto;

    public int QueryTimeoutSeconds { get; set; } = 10;

    public int MaxRows { get; set; } = 500;

    /// <summary>Folder with level YAML files; empty means auto-detect (repo levels/ or the copy next to the binaries).</summary>
    public string? LevelsPath { get; set; }

    /// <summary>World anchor date (yyyy-MM-dd) for reproducible screenshots/tests; empty means today (UTC).</summary>
    public string? WorldDate { get; set; }
}

public sealed record EngineStatus(string Name, bool Loaded, string? Error, double LoadMs);

/// <summary>Generates the world once and loads it into every engine; both engines stay available side by side.</summary>
public sealed class EngineHost
{
    private readonly Dictionary<string, IKqlEngine> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EngineStatus> _status = [];

    public EngineHost(GameOptions options, ILogger<EngineHost> logger)
    {
        DefaultEngine = EngineNames.Normalize(options.Engine);
        if (!EngineNames.All.Contains(DefaultEngine))
            throw new InvalidOperationException($"appsettings: неизвестный движок \"{options.Engine}\". Допустимо: {string.Join(", ", EngineNames.All)}.");

        var today = string.IsNullOrWhiteSpace(options.WorldDate)
            ? DateTime.UtcNow.Date
            : DateTime.ParseExact(options.WorldDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        var sw = Stopwatch.StartNew();
        World = WorldGenerator.Generate(today);
        logger.LogInformation("Мир сгенерирован за {Ms} мс: {Tables}", sw.ElapsedMilliseconds,
            string.Join(", ", World.Tables.Select(t => $"{t.Name}={t.Rows.Count}")));
        Analyzer = new KqlAnalyzer(World.Tables);

        foreach (var name in EngineNames.All)
        {
            sw.Restart();
            try
            {
                var engine = EngineFactory.Create(name);
                foreach (var table in World.Tables) engine.LoadTable(table.Name, table.Specs, table.Rows);
                _engines[name] = engine;
                _status.Add(new EngineStatus(name, true, null, sw.Elapsed.TotalMilliseconds));
                logger.LogInformation("Движок {Engine} загружен за {Ms} мс", name, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _status.Add(new EngineStatus(name, false, EngineErrors.Describe(ex), sw.Elapsed.TotalMilliseconds));
                logger.LogError(ex, "Движок {Engine} не загрузился", name);
            }
        }

        if (!_engines.ContainsKey(DefaultEngine))
            throw new InvalidOperationException($"Движок по умолчанию {DefaultEngine} не загрузился: {_status.First(s => s.Name == DefaultEngine).Error}");
    }

    public GameWorld World { get; }

    public KqlAnalyzer Analyzer { get; }

    public string DefaultEngine { get; }

    public IReadOnlyList<EngineStatus> Status => _status;

    /// <summary>Returns the requested engine, or the default one when <paramref name="name"/> is empty.</summary>
    public IKqlEngine Get(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? DefaultEngine : EngineNames.Normalize(name);
        if (_engines.TryGetValue(key, out var engine)) return engine;
        var status = _status.FirstOrDefault(s => s.Name == key);
        throw new ArgumentException(status == null
            ? $"Неизвестный движок \"{name}\". Допустимо: {string.Join(", ", EngineNames.All)}."
            : $"Движок {key} недоступен: {status.Error}");
    }
}
