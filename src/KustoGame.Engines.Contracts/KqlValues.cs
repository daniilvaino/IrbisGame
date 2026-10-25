using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KustoGame.Server.Engines;

public enum KqlType { Bool, Long, Real, String, DateTime, TimeSpan, Dynamic }

/// <summary>
/// Conversions between engine values and JSON-friendly values. Deliberately free of
/// Kusto.Language types: engines live in different load contexts with different versions of it.
/// </summary>
public static class KqlValues
{
    public static string TypeName(KqlType type) => type switch
    {
        KqlType.Bool => "bool",
        KqlType.Long => "long",
        KqlType.Real => "real",
        KqlType.String => "string",
        KqlType.DateTime => "datetime",
        KqlType.TimeSpan => "timespan",
        KqlType.Dynamic => "dynamic",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static KqlType? ParseType(string name) => name.ToLowerInvariant() switch
    {
        "bool" or "boolean" => KqlType.Bool,
        "long" or "int" => KqlType.Long,
        "real" or "double" or "decimal" => KqlType.Real,
        "string" => KqlType.String,
        "datetime" or "date" => KqlType.DateTime,
        "timespan" or "time" => KqlType.TimeSpan,
        "dynamic" => KqlType.Dynamic,
        _ => null,
    };

    /// <summary>
    /// Converts a raw engine value to a JSON-serializable value:
    /// numbers and bools as is, datetime as ISO-8601 UTC, timespan in KQL notation,
    /// dynamic as <see cref="JsonNode"/>.
    /// </summary>
    public static object? Normalize(object? value) => value switch
    {
        // The explicit object cast matters: without it the switch would get JsonNode as its natural
        // type (JsonNode has implicit conversions from string/long/double/bool) and wrap every value.
        null => (object?)null,
        int i => (long)i,
        long l => l,
        double d when double.IsNaN(d) || double.IsInfinity(d) => d.ToString(CultureInfo.InvariantCulture),
        double d => d,
        float f => (double)f,
        decimal m => (double)m,
        bool b => b,
        string s => s,
        DateTime dt => FormatDateTime(dt),
        DateTimeOffset dto => FormatDateTime(dto.UtcDateTime),
        TimeSpan ts => FormatTimeSpan(ts),
        Guid g => g.ToString(),
        JsonNode node => node.DeepClone(),
        JsonElement el => JsonNode.Parse(el.GetRawText()),
        _ => value.ToString(),
    };

    public static string FormatDateTime(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
        var text = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return text + "Z";
    }

    public static string FormatTimeSpan(TimeSpan ts)
    {
        var sb = new StringBuilder();
        if (ts < TimeSpan.Zero) { sb.Append('-'); ts = ts.Duration(); }
        if (ts.Days > 0) sb.Append(ts.Days).Append('.');
        sb.Append(ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        var ticks = ts.Ticks % TimeSpan.TicksPerSecond;
        if (ticks != 0) sb.Append('.').Append(ticks.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0'));
        return sb.ToString();
    }

    /// <summary>
    /// Canonical string used for comparing results: reals rounded to 6 significant digits,
    /// dynamic serialized compactly, everything else via invariant formatting.
    /// Kusto has no null strings, so a missing value and "" compare equal.
    /// </summary>
    public static string Canonical(object? normalized) => normalized switch
    {
        null => "",
        double d => d.ToString("G6", CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        JsonNode node => node.ToJsonString(),
        _ => Convert.ToString(normalized, CultureInfo.InvariantCulture) ?? "",
    };
}
