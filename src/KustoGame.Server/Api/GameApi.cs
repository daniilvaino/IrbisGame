using KustoGame.Server.Analysis;
using KustoGame.Server.Engines;
using KustoGame.Server.Levels;
using KustoGame.Server.Matrix;

namespace KustoGame.Server.Api;

public sealed record QueryRequest(string? Query, string? Engine);

public sealed record CheckRequest(string? LevelId, string? Query, string? Engine);

public sealed record SignatureRequest(string? Query, int Offset);

public static class GameApi
{
    public const int PreviewRows = 20;

    public static void MapGameApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/query", async (QueryRequest request, QueryService queries) =>
        {
            var response = await queries.RunAsync(request.Query, request.Engine);
            return response.Error?.Source is "command" or "input" ? Results.BadRequest(response) : Results.Ok(response);
        });

        api.MapPost("/query/steps", async (QueryRequest request, QueryService queries) =>
        {
            var response = await queries.RunStepsAsync(request.Query, request.Engine);
            return response.Error?.Source is "command" or "input" ? Results.BadRequest(response) : Results.Ok(response);
        });

        api.MapPost("/signature", (SignatureRequest request, EngineHost host) =>
            host.Analyzer.GetSignatureHelp(request.Query ?? "", request.Offset) is { } help ? Results.Ok(help) : Results.NoContent());

        api.MapPost("/diagnostics", (QueryRequest request, EngineHost host) =>
            Results.Ok(host.Analyzer.GetDiagnostics(request.Query ?? "")));

        api.MapPost("/check", async (CheckRequest request, LevelChecker checker) =>
            string.IsNullOrWhiteSpace(request.LevelId)
                ? Results.BadRequest(new { error = "Не указан levelId." })
                : Results.Ok(await checker.CheckAsync(request.LevelId, request.Query, request.Engine)));

        api.MapPost("/levels/{id}/reveal", async (string id, string? engine, LevelChecker checker) =>
            Results.Ok(await checker.RevealAsync(id, engine)));

        api.MapGet("/levels", (LevelStore levels, EngineHost host) => new
        {
            world = WorldInfo(host),
            chapters = levels.Chapters,
            levels = levels.Levels.Select(l => new
            {
                l.Id, l.Chapter, l.Order, l.Title, l.Topic, l.Engines, l.Theory, l.Example, l.Task, l.Hints, l.Points,
                checkMode = l.Check.Mode,
            }),
        });

        api.MapGet("/schema", (EngineHost host, string? engine) =>
        {
            var e = host.Get(engine);
            var described = host.World.Tables.ToDictionary(t => t.Name);
            return new
            {
                engine = e.Name,
                database = "Mayak",
                world = WorldInfo(host),
                tables = e.GetSchema().Select(t => new
                {
                    t.Name,
                    description = described[t.Name].Description,
                    t.RowCount,
                    columns = described[t.Name].Columns.Select(c => new { c.Name, type = KqlValues.TypeName(c.Type), c.Description }),
                }),
            };
        });

        api.MapGet("/tables/{name}/preview", async (string name, string? engine, EngineHost host, QueryService queries) =>
        {
            var table = host.World.Tables.FirstOrDefault(t => t.Name == name);
            if (table == null) return Results.NotFound(new { error = $"Таблица {name} не найдена." });
            return Results.Ok(await queries.RunAsync($"{table.Name} | take {PreviewRows}", engine, PreviewRows));
        });

        api.MapGet("/engine", (EngineHost host, GameOptions options) => new
        {
            active = host.DefaultEngine,
            engines = host.Status,
            timeoutSeconds = options.QueryTimeoutSeconds,
            maxRows = options.MaxRows,
        });

        api.MapGet("/engine/matrix", (MatrixCache matrix) => matrix.Get());
    }

    private static object WorldInfo(EngineHost host) => new
    {
        today = host.World.Today.ToString("yyyy-MM-dd"),
        days = World.WorldGenerator.Days,
    };
}

/// <summary>The operator matrix runs once per process on fresh engine instances with the matrix dataset.</summary>
public sealed class MatrixCache
{
    private readonly Lazy<object> _value = new(() =>
    {
        var outcomes = EngineNames.All.ToDictionary(n => n, n => OperatorMatrix.Run(EngineFactory.Create(n)));
        return new
        {
            engines = EngineNames.All,
            rows = OperatorMatrix.Cases.Select((c, i) => new
            {
                feature = c.Feature,
                query = c.Query,
                results = EngineNames.All.ToDictionary(n => n, n => new
                {
                    status = outcomes[n][i].Status.ToString(),
                    detail = outcomes[n][i].Detail,
                }),
            }),
        };
    });

    public object Get() => _value.Value;
}
