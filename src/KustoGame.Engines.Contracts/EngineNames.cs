namespace KustoGame.Server.Engines;

public static class EngineNames
{
    public const string BabyKusto = "BabyKusto";
    public const string KustoLoco = "KustoLoco";

    public static readonly IReadOnlyList<string> All = [BabyKusto, KustoLoco];

    public static string Normalize(string? name) =>
        All.FirstOrDefault(n => string.Equals(n, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? name ?? "";
}

public static class EngineErrors
{
    /// <summary>Engine exception text as is; the innermost message is the useful one for wrappers.</summary>
    public static string Describe(Exception ex)
    {
        var e = ex;
        while (e is AggregateException { InnerException: not null } || (e.InnerException != null && e is System.Reflection.TargetInvocationException))
            e = e.InnerException!;
        var kind = e is NotImplementedException or NotSupportedException ? " (не реализовано в движке)" : "";
        return $"{e.GetType().Name}{kind}: {e.Message}";
    }
}
