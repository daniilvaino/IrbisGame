namespace KustoGame.Server.Levels;

/// <summary>Level file (levels/*.yaml). Property names map to camelCase YAML keys.</summary>
public sealed class LevelDefinition
{
    public string Id { get; set; } = "";
    public int Chapter { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Operator or function being taught, shown as a tag.</summary>
    public string Topic { get; set; } = "";
    /// <summary>Engines whose output for <see cref="Solution"/> is correct; the level is locked on others.</summary>
    public List<string> Engines { get; set; } = [];
    public string Theory { get; set; } = "";
    public string Example { get; set; } = "";
    public string Task { get; set; } = "";
    public List<string> Hints { get; set; } = [];
    public string Solution { get; set; } = "";
    public CheckOptions Check { get; set; } = new();
    public int Points { get; set; } = 100;
}

public sealed class CheckOptions
{
    /// <summary>set — rows as a multiset; ordered — order matters; scalar — one value; shape — same columns and row count.</summary>
    public string Mode { get; set; } = CheckModes.Set;
    /// <summary>ignore (default) — columns may be named differently; strict — names must match.</summary>
    public string ColumnNames { get; set; } = "ignore";
    /// <summary>ignore (default) or strict.</summary>
    public string ColumnOrder { get; set; } = "ignore";
    /// <summary>deny (default) — extra columns fail the check; allow — they are ignored.</summary>
    public string ExtraColumns { get; set; } = "deny";
}

public static class CheckModes
{
    public const string Set = "set";
    public const string Ordered = "ordered";
    public const string Scalar = "scalar";
    public const string Shape = "shape";
    public static readonly string[] All = [Set, Ordered, Scalar, Shape];
}

public sealed class ChapterDefinition
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
