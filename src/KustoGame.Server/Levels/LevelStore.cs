using System.Text.RegularExpressions;
using KustoGame.Server.Engines;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KustoGame.Server.Levels;

/// <summary>
/// Reads levels/*.yaml and levels/chapters.yaml. Files are re-read when they change, so a new level
/// appears without restarting. Date placeholders are substituted on read: {{D-2}} → yyyy-MM-dd of two days
/// before the world anchor, {{TODAY}} → the anchor itself.
/// </summary>
public sealed partial class LevelStore(string directory, DateTime worldToday)
{
    private const string ChaptersFile = "chapters.yaml";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly object _gate = new();
    private (DateTime Stamp, int Count, IReadOnlyList<LevelDefinition> Levels, IReadOnlyList<ChapterDefinition> Chapters)? _cache;

    public string Directory { get; } = directory;

    public IReadOnlyList<LevelDefinition> Levels => Load().Levels;

    public IReadOnlyList<ChapterDefinition> Chapters => Load().Chapters;

    public LevelDefinition? Find(string id) => Levels.FirstOrDefault(l => l.Id == id);

    /// <summary>Picks the levels folder: explicit config, the repository's levels/, or the copy next to the binaries.</summary>
    public static string ResolveDirectory(string? configured, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured, contentRoot);
        var repo = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "levels"));
        return System.IO.Directory.Exists(repo) ? repo : Path.Combine(AppContext.BaseDirectory, "levels");
    }

    private (DateTime Stamp, int Count, IReadOnlyList<LevelDefinition> Levels, IReadOnlyList<ChapterDefinition> Chapters) Load()
    {
        var files = System.IO.Directory.Exists(Directory) ? System.IO.Directory.GetFiles(Directory, "*.yaml") : [];
        var stamp = files.Length == 0 ? DateTime.MinValue : files.Max(File.GetLastWriteTimeUtc);
        lock (_gate)
        {
            if (_cache is { } c && c.Stamp == stamp && c.Count == files.Length) return c;

            var chapters = new List<ChapterDefinition>();
            var levels = new List<LevelDefinition>();
            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                var text = Substitute(File.ReadAllText(file));
                try
                {
                    if (Path.GetFileName(file) == ChaptersFile)
                        chapters.AddRange(Yaml.Deserialize<List<ChapterDefinition>>(text));
                    else
                        levels.Add(Validate(Yaml.Deserialize<LevelDefinition>(text), file));
                }
                catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidDataException)
                {
                    throw new InvalidDataException($"Ошибка в файле уровня {Path.GetFileName(file)}: {ex.Message}", ex);
                }
            }

            var duplicate = levels.GroupBy(l => l.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidDataException($"Повторяющийся id уровня: {duplicate.Key}");

            var result = (stamp, files.Length,
                (IReadOnlyList<LevelDefinition>)levels.OrderBy(l => l.Chapter).ThenBy(l => l.Order).ThenBy(l => l.Id).ToList(),
                (IReadOnlyList<ChapterDefinition>)chapters.OrderBy(ch => ch.Number).ToList());
            _cache = result;
            return result;
        }
    }

    private static LevelDefinition Validate(LevelDefinition level, string file)
    {
        if (string.IsNullOrWhiteSpace(level.Id)) level.Id = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(level.Solution)) throw new InvalidDataException("не задан solution");
        if (!CheckModes.All.Contains(level.Check.Mode)) throw new InvalidDataException($"неизвестный check.mode \"{level.Check.Mode}\"");
        if (level.Engines.Count == 0) level.Engines = [.. EngineNames.All];
        level.Engines = level.Engines.Select(EngineNames.Normalize).ToList();
        var unknown = level.Engines.FirstOrDefault(e => !EngineNames.All.Contains(e));
        if (unknown != null) throw new InvalidDataException($"неизвестный движок \"{unknown}\"");
        level.Solution = level.Solution.Trim();
        level.Example = level.Example.Trim();
        return level;
    }

    private string Substitute(string text) => PlaceholderRegex().Replace(text, m =>
    {
        var day = m.Groups["today"].Success ? worldToday : worldToday.AddDays(-int.Parse(m.Groups["n"].Value));
        return day.ToString("yyyy-MM-dd");
    });

    [GeneratedRegex(@"\{\{\s*(?:(?<today>TODAY)|D-(?<n>\d+))\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
