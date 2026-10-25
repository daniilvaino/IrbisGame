namespace KustoGame.Server.Engines;

public static class EngineFactory
{
    public static IKqlEngine Create(string name) => EngineNames.Normalize(name) switch
    {
        EngineNames.BabyKusto => IsolatedEngineLoader.Load(
            EngineNames.BabyKusto, "KustoGame.Engines.BabyKusto", "KustoGame.Server.Engines.BabyKustoKqlEngine"),
        EngineNames.KustoLoco => new KustoLocoKqlEngine(),
        _ => throw new ArgumentException($"Неизвестный движок '{name}'. Допустимо: {string.Join(", ", EngineNames.All)}."),
    };
}
