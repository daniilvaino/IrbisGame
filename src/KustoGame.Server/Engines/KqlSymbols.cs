using Kusto.Language.Symbols;

namespace KustoGame.Server.Engines;

/// <summary>Mapping of game column types to the host's Kusto.Language symbols.</summary>
public static class KqlSymbols
{
    public static ScalarSymbol ToSymbol(KqlType type) => type switch
    {
        KqlType.Bool => ScalarTypes.Bool,
        KqlType.Long => ScalarTypes.Long,
        KqlType.Real => ScalarTypes.Real,
        KqlType.String => ScalarTypes.String,
        KqlType.DateTime => ScalarTypes.DateTime,
        KqlType.TimeSpan => ScalarTypes.TimeSpan,
        KqlType.Dynamic => ScalarTypes.Dynamic,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
