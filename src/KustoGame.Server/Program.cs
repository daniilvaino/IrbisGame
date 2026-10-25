using System.Text.Encodings.Web;
using System.Text.Unicode;
using KustoGame.Server.Analysis;
using KustoGame.Server.Api;
using KustoGame.Server.Engines;
using KustoGame.Server.Levels;
using KustoGame.Server.Matrix;

if (args.Contains("--matrix"))
{
    // Regenerates the operator support table for README.md.
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var outcomes = EngineNames.All.ToDictionary(n => n, n => OperatorMatrix.Run(EngineFactory.Create(n)));
    Console.WriteLine(OperatorMatrix.ToMarkdown(outcomes));
    return;
}

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.Get<GameOptions>() ?? new GameOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<EngineHost>();
builder.Services.AddSingleton<QueryService>();
builder.Services.AddSingleton<MatrixCache>();
builder.Services.AddSingleton(sp => new LevelStore(
    LevelStore.ResolveDirectory(options.LevelsPath, builder.Environment.ContentRootPath),
    sp.GetRequiredService<EngineHost>().World.Today));
builder.Services.AddSingleton<LevelChecker>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));

var app = builder.Build();

// Generate the world and load both engines before accepting requests.
var host = app.Services.GetRequiredService<EngineHost>();
var levels = app.Services.GetRequiredService<LevelStore>();
app.Logger.LogInformation("Движок по умолчанию: {Engine}. Уровней: {Count} (из {Dir})", host.DefaultEngine, levels.Levels.Count, levels.Directory);

app.UseExceptionHandler(error => error.Run(async context =>
{
    var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = ex switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException or BadHttpRequestException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
    await context.Response.WriteAsJsonAsync(new { error = ex?.Message ?? "Внутренняя ошибка сервера." });
}));

// index.html must always be revalidated, otherwise a browser keeps an old build after the frontend is rebuilt;
// Vite's hashed assets never change under the same name and can be cached for good.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
        ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache" : "public, max-age=31536000, immutable",
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);
app.MapGameApi();
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

public partial class Program;
