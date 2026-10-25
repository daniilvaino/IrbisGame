using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KustoGame.Server.Analysis;
using KustoGame.Server.Engines;

namespace KustoGame.Tests;

[Collection(GameCollection.Name)]
public sealed class QueryApiTests(GameFixture game)
{
    [Theory]
    [InlineData(".show tables")]
    [InlineData("  .drop table Employees")]
    [InlineData(".set-or-append Employees <| Employees | take 1")]
    [InlineData("\n.create table T (x: long)")]
    public async Task Control_commands_are_rejected(string command)
    {
        foreach (var engine in EngineNames.All)
        {
            var response = await game.Client.PostAsJsonAsync("/api/query", new { query = command, engine });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("command", body.GetProperty("error").GetProperty("source").GetString());

            var steps = await game.Client.PostAsJsonAsync("/api/query/steps", new { query = command, engine });
            Assert.Equal(HttpStatusCode.BadRequest, steps.StatusCode);
        }

        // The table is still there.
        var check = await game.Queries.RunAsync("Employees | count", EngineNames.BabyKusto);
        Assert.Equal(5000L, check.Rows[0][0]);
    }

    [Fact]
    public async Task Query_returns_typed_columns_and_respects_row_limit()
    {
        var response = await game.Client.PostAsJsonAsync("/api/query", new { query = "NetworkLogs | project Timestamp, BytesSent, Url" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(["datetime", "long", "string"], body.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
        Assert.Equal(500, body.GetProperty("rows").GetArrayLength());
        Assert.Equal(50_000, body.GetProperty("totalRows").GetInt32());
        Assert.True(body.GetProperty("truncated").GetBoolean());
        Assert.True(body.GetProperty("elapsedMs").GetDouble() >= 0);
    }

    [Fact]
    public async Task Engine_error_is_returned_as_is_with_position()
    {
        const string query = "NetworkLogs | top 3 by BytesSent";
        var result = await game.Queries.RunAsync(query, EngineNames.BabyKusto);

        Assert.NotNull(result.Error);
        Assert.Equal("engine", result.Error!.Source);
        Assert.Contains("TopOperator", result.Error.Message);
        Assert.Equal(query.IndexOf("top", StringComparison.Ordinal), result.Error.Position!.Start);
    }

    [Fact]
    public async Task Syntax_errors_come_with_diagnostics()
    {
        var result = await game.Queries.RunAsync("Employees | where Nmae == 1", EngineNames.KustoLoco);
        Assert.Contains(result.Diagnostics, d => d.Severity == "Error" && d.Position.Start == "Employees | where ".Length);
    }

    [Fact]
    public async Task Dynamic_values_are_serialized_as_json()
    {
        var response = await game.Client.PostAsJsonAsync("/api/query", new { query = "Employees | where EmployeeId == \"E04217\" | project Profile", engine = "KustoLoco" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var profile = body.GetProperty("rows")[0][0];
        Assert.Equal(JsonValueKind.Object, profile.ValueKind);
        Assert.Equal("L3", profile.GetProperty("clearance").GetString());
    }

    [Fact]
    public async Task Schema_and_preview_describe_all_tables()
    {
        var schema = await game.Client.GetFromJsonAsync<JsonElement>("/api/schema");
        var tables = schema.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Employees", "NetworkLogs", "EmailEvents", "BadgeEvents", "Transactions"], tables);

        var types = schema.GetProperty("tables").EnumerateArray()
            .SelectMany(t => t.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("type").GetString()))
            .ToHashSet();
        Assert.Superset(new HashSet<string?> { "datetime", "string", "long", "real", "bool", "dynamic" }, types);

        foreach (var table in tables)
        {
            var preview = await game.Client.GetFromJsonAsync<JsonElement>($"/api/tables/{table}/preview");
            Assert.Equal(20, preview.GetProperty("rows").GetArrayLength());
        }
    }

    [Fact]
    public async Task Engine_endpoint_reports_both_engines()
    {
        var info = await game.Client.GetFromJsonAsync<JsonElement>("/api/engine");
        Assert.Equal("BabyKusto", info.GetProperty("active").GetString());
        Assert.All(info.GetProperty("engines").EnumerateArray(), e => Assert.True(e.GetProperty("loaded").GetBoolean()));
    }

    [Fact]
    public async Task Steps_endpoint_returns_every_prefix_with_row_counts()
    {
        var response = await game.Client.PostAsJsonAsync("/api/query/steps", new
        {
            query = "EmailEvents | where Direction == \"Inbound\" | summarize Count = count() by Sender | where Count > 100",
            engine = "BabyKusto",
        });
        var body = await response.Content.ReadFromJsonAsync<StepsResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(4, body!.Stages.Count);
        Assert.Equal(20_000, body.Stages[0].Result.TotalRows);
        Assert.True(body.Stages[1].Result.TotalRows < 20_000);
        Assert.All(body.Stages, s => Assert.Null(s.Result.Error));
    }
}
