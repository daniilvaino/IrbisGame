using KustoGame.Server.Analysis;
using KustoGame.Server.Engines;
using KustoGame.Server.Levels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KustoGame.Tests;

/// <summary>One application instance (world + both engines) shared by all tests: loading it takes about a second.</summary>
public sealed class GameFixture : IDisposable
{
    public GameFixture()
    {
        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();
    }

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Client { get; }

    public EngineHost Host => Factory.Services.GetRequiredService<EngineHost>();

    public QueryService Queries => Factory.Services.GetRequiredService<QueryService>();

    public LevelStore Levels => Factory.Services.GetRequiredService<LevelStore>();

    public LevelChecker Checker => Factory.Services.GetRequiredService<LevelChecker>();

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class GameCollection : ICollectionFixture<GameFixture>
{
    public const string Name = "game";
}
