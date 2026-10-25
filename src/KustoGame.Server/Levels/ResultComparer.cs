using KustoGame.Server.Engines;

namespace KustoGame.Server.Levels;

public sealed record ColumnMatch(string Expected, string? Actual);

public sealed record ResultDiff(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> MissingRows,
    IReadOnlyList<object?[]> ExtraRows,
    int MissingCount,
    int ExtraCount,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> ExtraColumns,
    IReadOnlyList<ColumnMatch> ColumnMapping,
    int ExpectedRowCount,
    int ActualRowCount,
    object? ExpectedValue,
    object? ActualValue,
    int? FirstOrderMismatch);

public sealed record CompareOutcome(bool Passed, string Message, ResultDiff Diff);

/// <summary>
/// Compares the player's result with the reference result produced by the same engine.
/// Values are compared through <see cref="KqlValues.Canonical"/> (reals to 6 significant digits).
/// </summary>
public static class ResultComparer
{
    public const int MaxDiffRows = 50;

    public static CompareOutcome Compare(EngineResult expected, EngineResult actual, CheckOptions options) => options.Mode switch
    {
        CheckModes.Scalar => CompareScalar(expected, actual),
        CheckModes.Shape => CompareShape(expected, actual, options),
        _ => CompareRows(expected, actual, options),
    };

    private static CompareOutcome CompareScalar(EngineResult expected, EngineResult actual)
    {
        var want = expected.Rows.Count > 0 && expected.Columns.Count > 0 ? expected.Rows[0][0] : null;
        var key = KqlValues.Canonical(want);
        object? got = actual.Rows.Count > 0 && actual.Columns.Count > 0 ? actual.Rows[0][0] : null;

        string message;
        var passed = false;
        if (actual.TotalRows != 1)
            message = $"Нужно одно значение, а запрос вернул строк: {actual.TotalRows}.";
        else
        {
            // The answer may sit in any column of the single row (e.g. "summarize Total = count(), Name").
            var hit = actual.Rows[0].Select(v => (Value: v, Match: KqlValues.Canonical(v) == key)).FirstOrDefault(x => x.Match);
            passed = hit.Match;
            if (passed) got = hit.Value;
            message = passed ? "Верно!" : $"Значение не совпадает: ожидалось {Show(want)}, получено {Show(got)}.";
        }

        return new CompareOutcome(passed, message, EmptyDiff(expected, actual) with { ExpectedValue = want, ActualValue = got });
    }

    private static CompareOutcome CompareShape(EngineResult expected, EngineResult actual, CheckOptions options)
    {
        var mapping = MatchColumns(expected, actual, options);
        var missing = mapping.Where(m => m.Actual == null).Select(m => m.Expected).ToList();
        var extra = actual.Columns.Select(c => c.Name).Except(mapping.Select(m => m.Actual)).OfType<string>().ToList();
        var diff = EmptyDiff(expected, actual) with { MissingColumns = missing, ExtraColumns = extra, ColumnMapping = mapping };

        if (missing.Count > 0 || extra.Count > 0)
            return new CompareOutcome(false, ColumnMessage(missing, extra), diff);
        if (expected.TotalRows != actual.TotalRows)
            return new CompareOutcome(false, $"Колонки верные, но строк должно быть {expected.TotalRows}, а получено {actual.TotalRows}.", diff);
        return new CompareOutcome(true, "Верно!", diff);
    }

    private static CompareOutcome CompareRows(EngineResult expected, EngineResult actual, CheckOptions options)
    {
        var mapping = MatchColumns(expected, actual, options);
        var missingColumns = mapping.Where(m => m.Actual == null).Select(m => m.Expected).ToList();
        var extraColumns = options.ExtraColumns == "allow"
            ? []
            : actual.Columns.Select(c => c.Name).Except(mapping.Select(m => m.Actual)).OfType<string>().ToList();

        if (options.ColumnOrder == "strict" && missingColumns.Count == 0)
        {
            var actualOrder = actual.Columns.Select(c => c.Name).Where(n => mapping.Any(m => m.Actual == n)).ToList();
            if (!actualOrder.SequenceEqual(mapping.Select(m => m.Actual!)))
                return new CompareOutcome(false, $"Колонки должны идти в порядке: {string.Join(", ", expected.Columns.Select(c => c.Name))}.",
                    EmptyDiff(expected, actual) with { ColumnMapping = mapping });
        }

        // Rows of the player's result projected onto the expected column order.
        var actualIndexes = mapping.Select(m => m.Actual == null ? -1 : IndexOf(actual, m.Actual)).ToArray();
        var expectedRows = expected.Rows;
        var actualRows = actual.Rows.Select(r => actualIndexes.Select(i => i < 0 ? null : r[i]).ToArray()).ToList();

        var missing = Subtract(expectedRows, actualRows);
        var extra = Subtract(actualRows, expectedRows);
        int? firstMismatch = null;
        if (options.Mode == CheckModes.Ordered && missing.Count == 0 && extra.Count == 0)
        {
            for (var i = 0; i < expectedRows.Count; i++)
            {
                if (Key(expectedRows[i]) == Key(actualRows[i])) continue;
                firstMismatch = i;
                break;
            }
        }

        var diff = new ResultDiff(
            expected.Columns.Select(c => c.Name).ToList(),
            missing.Take(MaxDiffRows).ToList(), extra.Take(MaxDiffRows).ToList(), missing.Count, extra.Count,
            missingColumns, extraColumns, mapping, expected.TotalRows, actual.TotalRows, null, null, firstMismatch);

        if (missingColumns.Count > 0 || extraColumns.Count > 0)
            return new CompareOutcome(false, ColumnMessage(missingColumns, extraColumns), diff);
        if (missing.Count > 0 || extra.Count > 0)
            return new CompareOutcome(false, $"Строки не совпадают: не хватает {missing.Count}, лишних {extra.Count} (ожидалось строк: {expected.TotalRows}, получено: {actual.TotalRows}).", diff);
        if (firstMismatch is { } at)
            return new CompareOutcome(false, $"Строки те же, но порядок другой: первое расхождение в строке {at + 1}. Здесь порядок важен.", diff);
        return new CompareOutcome(true, "Верно!", diff);
    }

    /// <summary>
    /// Maps every expected column to a column of the player's result: first by exact name, then
    /// (unless names are strict) case-insensitively, then by identical content — so a column named
    /// differently but holding the same values still matches.
    /// </summary>
    public static IReadOnlyList<ColumnMatch> MatchColumns(EngineResult expected, EngineResult actual, CheckOptions options)
    {
        var result = new string?[expected.Columns.Count];
        var used = new HashSet<string>();
        for (var i = 0; i < expected.Columns.Count; i++)
        {
            var name = expected.Columns[i].Name;
            if (actual.Columns.Any(c => c.Name == name) && used.Add(name)) result[i] = name;
        }

        if (options.ColumnNames != "strict")
        {
            for (var i = 0; i < result.Length; i++)
            {
                if (result[i] != null) continue;
                var name = expected.Columns[i].Name;
                var candidate = actual.Columns.FirstOrDefault(c => !used.Contains(c.Name) && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && used.Add(candidate.Name)) result[i] = candidate.Name;
            }

            for (var i = 0; i < result.Length; i++)
            {
                if (result[i] != null) continue;
                var signature = ColumnSignature(expected, i);
                var candidate = actual.Columns
                    .Select((c, index) => (c.Name, index))
                    .FirstOrDefault(c => !used.Contains(c.Name) && ColumnSignature(actual, c.index) == signature);
                if (candidate.Name != null && used.Add(candidate.Name)) result[i] = candidate.Name;
            }
        }

        return expected.Columns.Select((c, i) => new ColumnMatch(c.Name, result[i])).ToList();
    }

    private static string ColumnSignature(EngineResult result, int column) =>
        string.Join("\u001e", result.Rows.Select(r => KqlValues.Canonical(r[column])).Order(StringComparer.Ordinal));

    private static List<object?[]> Subtract(IReadOnlyList<object?[]> from, IReadOnlyList<object?[]> what)
    {
        var counts = new Dictionary<string, int>();
        foreach (var row in what)
        {
            var key = Key(row);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var rest = new List<object?[]>();
        foreach (var row in from)
        {
            var key = Key(row);
            if (counts.TryGetValue(key, out var n) && n > 0) counts[key] = n - 1;
            else rest.Add(row);
        }
        return rest;
    }

    private static string Key(object?[] row) => string.Join("\u001f", row.Select(KqlValues.Canonical));

    private static int IndexOf(EngineResult result, string name)
    {
        for (var i = 0; i < result.Columns.Count; i++)
            if (result.Columns[i].Name == name) return i;
        return -1;
    }

    private static string ColumnMessage(IReadOnlyList<string> missing, IReadOnlyList<string> extra)
    {
        var parts = new List<string>();
        if (missing.Count > 0) parts.Add($"не хватает колонок: {string.Join(", ", missing)}");
        if (extra.Count > 0) parts.Add($"лишние колонки: {string.Join(", ", extra)}");
        return "Набор колонок не совпадает — " + string.Join("; ", parts) + ".";
    }

    private static ResultDiff EmptyDiff(EngineResult expected, EngineResult actual) => new(
        expected.Columns.Select(c => c.Name).ToList(), [], [], 0, 0, [], [], [], expected.TotalRows, actual.TotalRows, null, null, null);

    private static string Show(object? value) => value == null ? "пусто" : $"«{KqlValues.Canonical(value)}»";
}
