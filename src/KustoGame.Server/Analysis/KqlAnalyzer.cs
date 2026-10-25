using System.Text.RegularExpressions;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;
using KustoGame.Server.Engines;
using KustoGame.Server.World;

namespace KustoGame.Server.Analysis;

public sealed record TextPosition(int Start, int Length, int Line, int Column);

public sealed record QueryDiagnostic(string Severity, string Code, string Message, TextPosition Position);

/// <summary>A prefix of the top-level pipeline: "T", "T | where ...", "T | where ... | summarize ...".</summary>
public sealed record PipelineStage(int Index, string Label, string Query, TextPosition Position);

public sealed record SignatureParameter(string Label, string Documentation);

public sealed record SignatureInfo(string Label, string Documentation, IReadOnlyList<SignatureParameter> Parameters);

/// <summary>Signature help in the shape monaco's SignatureHelpProvider expects.</summary>
public sealed record SignatureHelp(IReadOnlyList<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter);

/// <summary>
/// Everything that needs to understand query text is done by the official parser
/// (Microsoft.Azure.Kusto.Language): diagnostics, control command detection, pipeline stages.
/// Execution always belongs to the engines.
/// </summary>
public sealed partial class KqlAnalyzer
{
    private readonly GlobalState _globals;

    public KqlAnalyzer(IEnumerable<TableData> tables)
    {
        var database = new DatabaseSymbol("Mayak", tables.Select(t =>
            new TableSymbol(t.Name, t.Columns.Select(c => new ColumnSymbol(c.Name, KqlSymbols.ToSymbol(c.Type), c.Description)), t.Description)));
        _globals = GlobalState.Default.WithDatabase(database);
    }

    public static bool IsControlCommand(string query)
    {
        if (query.TrimStart().StartsWith('.')) return true;
        return KustoCode.Parse(query).Kind == "Command";
    }

    public IReadOnlyList<QueryDiagnostic> GetDiagnostics(string query)
    {
        var code = KustoCode.ParseAndAnalyze(query, _globals);
        return code.GetDiagnostics()
            .Select(d => new QueryDiagnostic(d.Severity, d.Code, d.Message, Position(query, d.Start, d.Length)))
            .ToList();
    }

    /// <summary>Splits the last statement's top-level pipe chain into cumulative prefixes.</summary>
    public IReadOnlyList<PipelineStage> SplitPipeline(string query)
    {
        var code = KustoCode.Parse(query);
        if (code.Syntax is not QueryBlock block) return [];

        var statement = block.Statements.Select(s => s.Element).OfType<ExpressionStatement>().LastOrDefault();
        if (statement == null) return [];

        var pipes = new List<PipeExpression>();
        var expression = statement.Expression;
        while (expression is PipeExpression pipe)
        {
            pipes.Insert(0, pipe);
            expression = pipe.Expression;
        }

        var stages = new List<PipelineStage>
        {
            Stage(0, Compact(expression.ToString(IncludeTrivia.Minimal)), expression.TextStart, expression.End),
        };
        foreach (var pipe in pipes)
            stages.Add(Stage(stages.Count, Compact(pipe.Operator.ToString(IncludeTrivia.Minimal)), pipe.Operator.TextStart, pipe.End));
        return stages;

        // Everything up to the end of the stage, so `let` statements stay in every prefix.
        PipelineStage Stage(int index, string label, int start, int end) =>
            new(index, label, query[..end].TrimEnd(), Position(query, start, end - start));
    }

    /// <summary>
    /// Signature of the innermost function call around <paramref name="offset"/>: the call is found in the
    /// parsed tree, its signatures come from the bound <see cref="FunctionSymbol"/>, the active parameter
    /// is the number of argument separators before the cursor.
    /// </summary>
    public SignatureHelp? GetSignatureHelp(string query, int offset)
    {
        var code = KustoCode.ParseAndAnalyze(query, _globals);
        var call = code.Syntax.GetDescendants<FunctionCallExpression>(c =>
                c.ArgumentList.OpenParen.TextStart < offset &&
                (c.ArgumentList.CloseParen.Width == 0 || offset <= c.ArgumentList.CloseParen.TextStart))
            .OrderByDescending(c => c.TextStart)
            .FirstOrDefault();
        if (call == null) return null;

        var symbol = call.ReferencedSymbol as FunctionSymbol
                     ?? _globals.GetFunction(call.Name.SimpleName)
                     ?? _globals.GetAggregate(call.Name.SimpleName);
        if (symbol == null) return null;

        var activeParameter = call.ArgumentList.Expressions.Count(e => e.Separator is { Width: > 0 } s && s.TextStart < offset);
        var signatures = symbol.Signatures.Where(s => !s.IsHidden).ToList();
        if (signatures.Count == 0) return null;

        var active = signatures.FindIndex(s => s.MaxArgumentCount > activeParameter);
        return new SignatureHelp(
            signatures.Select(s => Describe(symbol, s)).ToList(),
            Math.Max(0, active),
            activeParameter);
    }

    private static SignatureInfo Describe(FunctionSymbol symbol, Signature signature)
    {
        var parameters = signature.Parameters.Select(p =>
        {
            var label = $"{p.Name}: {ParameterType(p)}";
            if (p.IsRepeatable) label += ", ...";
            if (p.IsOptional) label = $"[{label}]";
            return new SignatureParameter(label, p.Description ?? "");
        }).ToList();
        var returns = signature.DeclaredReturnType is { } type ? $": {type.Name}" : "";
        return new SignatureInfo($"{symbol.Name}({string.Join(", ", parameters.Select(p => p.Label))}){returns}", symbol.Description ?? "", parameters);
    }

    private static string ParameterType(Parameter parameter) => parameter.DeclaredTypes.Count > 0
        ? string.Join(" | ", parameter.DeclaredTypes.Select(t => t.Name))
        : parameter.TypeKind switch
        {
            ParameterTypeKind.Number => "number",
            ParameterTypeKind.Summable => "number | timespan",
            ParameterTypeKind.StringOrDynamic => "string | dynamic",
            ParameterTypeKind.Tabular => "table",
            ParameterTypeKind.NotDynamic => "scalar (не dynamic)",
            _ => "scalar",
        };

    /// <summary>
    /// Engines report unsupported constructs by name ("Kusto.Language.Syntax.TopOperator",
    /// "Function hourofday(...)", "Query operator 'parse'", "Operator HasHas ..."); find that construct in the query.
    /// </summary>
    public static TextPosition? LocateEngineError(string query, string error)
    {
        var code = KustoCode.Parse(query);
        var nodes = code.Syntax.GetDescendants<SyntaxNode>();

        SyntaxNode? found = null;
        if (SyntaxTypeRegex().Match(error) is { Success: true } typeMatch)
            found = nodes.FirstOrDefault(n => n.GetType().Name == typeMatch.Groups[1].Value);
        else if (FunctionRegex().Match(error) is { Success: true } fnMatch)
            found = nodes.OfType<FunctionCallExpression>().FirstOrDefault(f => string.Equals(f.Name.SimpleName, fnMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        else if (QueryOperatorRegex().Match(error) is { Success: true } opMatch)
            found = nodes.OfType<QueryOperator>().FirstOrDefault(o => o.GetType().Name.Replace("Operator", "").Equals(opMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        else if (BinaryOperatorRegex().Match(error) is { Success: true } binMatch)
            found = nodes.FirstOrDefault(n => n.Kind.ToString() == binMatch.Groups[1].Value + "Expression");

        return found == null ? null : Position(query, found.TextStart, found.Width);
    }

    public static TextPosition Position(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < start; i++)
        {
            if (text[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }
        return new TextPosition(start, Math.Max(0, length), line, start - lineStart + 1);
    }

    private static string Compact(string text) => WhitespaceRegex().Replace(text.Trim(), " ");

    [GeneratedRegex(@"Kusto\.Language\.Syntax\.(\w+)")]
    private static partial Regex SyntaxTypeRegex();

    [GeneratedRegex(@"Function (\w+)\(")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"Query operator '([\w-]+)' not supported")]
    private static partial Regex QueryOperatorRegex();

    // "Operator HasHas is not implemented", "Operator EqualTildeEqualTilde ...": the kind name is printed twice.
    [GeneratedRegex(@"Operator (\w+?)\1 is not implemented")]
    private static partial Regex BinaryOperatorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
