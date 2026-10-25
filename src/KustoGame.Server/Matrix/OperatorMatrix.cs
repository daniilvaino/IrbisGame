using System.Text;
using System.Text.Json.Nodes;
using KustoGame.Server.Engines;

namespace KustoGame.Server.Matrix;

public enum SupportStatus { Supported, WrongResult, Failed }

public sealed record MatrixCase(string Feature, string Query, string[] Expected, bool Ordered = false);

public sealed record MatrixOutcome(MatrixCase Case, string Engine, SupportStatus Status, string Detail);

/// <summary>
/// Operator support matrix: every case runs on a tiny fixed dataset and its result is compared
/// with the expected rows (canonical values joined by '|'). Used for the README table and by the tests.
/// </summary>
public static class OperatorMatrix
{
    public static readonly IReadOnlyList<MatrixCase> Cases =
    [
        new("take", "M | take 2 | count", ["2"]),
        new("project", "M | where Id == 1 | project Id, Name", ["1|alpha one"]),
        new("project-away / project-rename", "M | where Id == 1 | project-away Props, Raw, Ts, Flag | project-rename Title = Name | project Id, Title", ["1|alpha one"]),
        new("where (and, ==, >)", "M | where Value > 2 and Category == \"A\" | project Id", ["3"]),
        new("sort by", "M | sort by Value desc | take 2 | project Id", ["5", "4"], Ordered: true),
        new("top", "M | top 2 by Value asc | project Id", ["1", "2"], Ordered: true),
        new("distinct", "M | distinct Category", ["A", "B", "C"]),
        new("count", "M | count", ["5"]),
        new("summarize count() by", "M | summarize C = count() by Category", ["A|2", "B|2", "C|1"]),
        new("summarize sum/avg/min/max", "M | summarize S = sum(Value), A = avg(Value), Mn = min(Value), Mx = max(Value)", ["21.5|4.3|1.5|10"]),
        new("dcount", "M | summarize dcount(Category)", ["3"]),
        new("countif", "M | summarize countif(Flag)", ["3"]),
        new("arg_max", "M | summarize arg_max(Value, Id) by Category | project Category, Id", ["A|3", "B|5", "C|4"]),
        new("make_set", "M | summarize S = make_set(Category) | project L = array_length(S)", ["3"]),
        new("bin()", "M | summarize C = count() by D = bin(Ts, 1d)", ["2024-01-01T00:00:00Z|2", "2024-01-02T00:00:00Z|1", "2024-01-03T00:00:00Z|1", "2024-01-05T00:00:00Z|1"]),
        new("has", "M | where Name has \"two\" | project Id", ["2"]),
        new("has (без учёта регистра)", "M | where Name has \"GAMMA\" | project Id", ["3"]),
        new("has (кириллица)", "M | where Name has \"пароль\" | project Id", ["5"]),
        new("!has", "M | where Name !has \"two\" | count", ["4"]),
        new("contains", "M | where Name contains \"lph\" | project Id", ["1"]),
        new("startswith", "M | where Name startswith \"del\" | project Id", ["4"]),
        new("endswith", "M | where Name endswith \"three\" | project Id", ["3"]),
        new("in (...)", "M | where Category in (\"A\", \"C\") | count", ["3"]),
        new("matches regex", "M | where Name matches regex @\"^[a-z]+-[a-z]+$\" | project Id", ["4"]),
        new("parse", "M | where Id == 4 | parse Name with First \"-\" Second | project First, Second", ["delta|four"]),
        new("extract()", "M | where Id == 4 | project X = extract(@\"^(\\w+)-(\\w+)$\", 2, Name)", ["four"]),
        new("split()", "M | where Id == 4 | project P = tostring(split(Name, \"-\")[1])", ["four"]),
        new("=~ / !=", "M | where Category =~ \"a\" and Id != 1 | project Id", ["3"]),
        new("!contains", "M | where Name !contains \"a\" | count", ["1"]),
        new("has_any", "M | where Name has_any (\"two\", \"three\") | count", ["2"]),
        new("extend", "M | where Id == 1 | extend Double = Value * 2 | project Double", ["3"]),
        new("case()", "M | extend Size = case(Value < 2, \"S\", Value < 5, \"M\", \"L\") | summarize count() by Size", ["S|1", "M|3", "L|1"]),
        new("iff()", "M | extend F = iff(Flag, \"yes\", \"no\") | summarize count() by F", ["yes|3", "no|2"]),
        new("ago() / now()", "print R = ago(1d) < now()", ["true"]),
        new("between (числа)", "M | where Value between (2.0 .. 4.5) | project Id", ["2", "3", "4"]),
        new("between (datetime)", "M | where Ts between (datetime(2024-01-01) .. datetime(2024-01-02)) | project Id", ["1", "2", "3"]),
        new("datetime - datetime", "M | where Id == 4 | project D = Ts - datetime(2024-01-01)", ["2.12:00:00"]),
        new("datetime + timespan", "print T = datetime(2024-01-01) + 36h", ["2024-01-02T12:00:00Z"]),
        new("hourofday()", "M | where Id == 2 | project H = hourofday(Ts)", ["6"]),
        new("startofday()", "M | where Id == 4 | project D = startofday(Ts)", ["2024-01-03T00:00:00Z"]),
        new("datetime_part()", "M | where Id == 2 | project H = datetime_part(\"hour\", Ts)", ["6"]),
        new("dayofweek()", "M | where Id == 4 | project D = dayofweek(Ts)", ["3.00:00:00"]),
        new("bin() по часу", "M | where Ts < datetime(2024-01-02) | summarize C = count() by H = bin(Ts, 1h)", ["2024-01-01T00:00:00Z|1", "2024-01-01T06:00:00Z|1"]),
        new("format_datetime()", "M | where Id == 4 | project F = format_datetime(Ts, \"yyyy-MM-dd HH:mm\")", ["2024-01-03 12:00"]),
        new("let", "let x = 2; M | where Id == x | project Id", ["2"]),
        new("join kind=inner", "M | join kind=inner N on Id | project Id, Label", ["1|one", "2|two"]),
        new("join kind=leftouter", "M | join kind=leftouter N on Id | project Id, Label", ["1|one", "2|two", "3|", "4|", "5|"]),
        new("join kind=leftanti", "M | join kind=leftanti N on Id | project Id", ["3", "4", "5"]),
        new("join kind=rightanti", "M | join kind=rightanti N on Id | project Id", ["9"]),
        new("join (подзапрос, $left/$right)", "M | join kind=inner (N | project Key = Id, Label) on $left.Id == $right.Key | project Id, Label", ["1|one", "2|two"]),
        new("union", "union M, N | count", ["8"]),
        new("union (подзапросы)", "union (M | project Id), (N | project Id) | count", ["8"]),
        new("parse_json", "M | where Id == 1 | extend J = parse_json(Raw) | project Ip = tostring(J.ip)", ["10.0.0.1"]),
        new("todynamic", "M | where Id == 2 | extend J = todynamic(Raw) | project N = toint(J.n)", ["2"]),
        new("dynamic: точка", "M | where Id == 1 | project K = tostring(Props.k)", ["x"]),
        new("dynamic: [\"key\"]", "M | where Id == 1 | project K = tostring(Props[\"k\"])", ["x"]),
        new("dynamic: вложенность", "M | where Id == 5 | project V = tostring(Props.inner.v)", ["deep"]),
        new("array_length()", "M | where Id == 4 | project L = array_length(Props.arr)", ["3"]),
        new("mv-expand", "M | mv-expand A = Props.arr | count", ["7"]),
        new("mv-expand + to typeof", "M | where Id == 4 | mv-expand A = Props.arr to typeof(long) | summarize S = sum(A)", ["15"]),
        new("tostring / tolong", "M | where Id == 3 | project S = tostring(Id), L = tolong(\"42\")", ["3|42"]),
        new("strcat / strlen", "M | where Id == 1 | project S = strcat(Category, \"-\", Id), L = strlen(Name)", ["A-1|9"]),
        new("isempty / isnotempty", "M | join kind=leftouter N on Id | summarize countif(isempty(Label))", ["3"]),
        new("isnotempty после leftouter с подзапросом", "M | join kind=leftouter (N | where Id > 0) on Id | summarize countif(isnotempty(Label))", ["2"]),
        new("!= \"\" после leftouter с подзапросом", "M | join kind=leftouter (N | where Id > 0) on Id | summarize countif(Label != \"\")", ["2"]),
    ];

    public static IReadOnlyList<MatrixOutcome> Run(IKqlEngine engine)
    {
        LoadData(engine);
        return Cases.Select(c => Evaluate(engine, c)).ToList();
    }

    public static MatrixOutcome Evaluate(IKqlEngine engine, MatrixCase c)
    {
        EngineResult result;
        try
        {
            result = engine.Run(c.Query, 1000);
        }
        catch (Exception ex)
        {
            return new(c, engine.Name, SupportStatus.Failed, EngineErrors.Describe(ex));
        }

        if (result.Error != null) return new(c, engine.Name, SupportStatus.Failed, result.Error);

        var actual = result.Rows.Select(r => string.Join("|", r.Select(KqlValues.Canonical))).ToList();
        var matches = c.Ordered
            ? actual.SequenceEqual(c.Expected)
            : actual.Order(StringComparer.Ordinal).SequenceEqual(c.Expected.Order(StringComparer.Ordinal));
        return matches
            ? new(c, engine.Name, SupportStatus.Supported, "")
            : new(c, engine.Name, SupportStatus.WrongResult, $"получено: {string.Join("; ", actual)}");
    }

    private static void LoadData(IKqlEngine engine)
    {
        static DateTime D(int day, int hour = 0) => new(2024, 1, day, hour, 0, 0, DateTimeKind.Utc);

        engine.LoadTable("M",
        [
            new("Id", KqlType.Long), new("Name", KqlType.String), new("Category", KqlType.String),
            new("Value", KqlType.Real), new("Ts", KqlType.DateTime), new("Flag", KqlType.Bool),
            new("Props", KqlType.Dynamic), new("Raw", KqlType.String),
        ],
        [
            [1L, "alpha one", "A", 1.5, D(1), true, JsonNode.Parse("""{"k":"x","arr":[1,2]}"""), """{"ip":"10.0.0.1","n":1}"""],
            [2L, "beta two", "B", 2.5, D(1, 6), false, JsonNode.Parse("""{"k":"y","arr":[3]}"""), """{"ip":"10.0.0.2","n":2}"""],
            [3L, "Gamma three", "A", 3.0, D(2), true, JsonNode.Parse("""{"k":"z","arr":[]}"""), """{"ip":"10.0.0.3","n":3}"""],
            [4L, "delta-four", "C", 4.5, D(3, 12), false, JsonNode.Parse("""{"k":"x","arr":[4,5,6]}"""), """{"ip":"10.0.0.4","n":4}"""],
            [5L, "пароль VPN сброс", "B", 10.0, D(5), true, JsonNode.Parse("""{"k":"w","arr":[7],"inner":{"v":"deep"}}"""), """{"ip":"10.0.0.5","n":5}"""],
        ]);
        engine.LoadTable("N",
            [new("Id", KqlType.Long), new("Label", KqlType.String)],
            [[1L, "one"], [2L, "two"], [9L, "nine"]]);
    }

    public static string ToMarkdown(IReadOnlyDictionary<string, IReadOnlyList<MatrixOutcome>> byEngine)
    {
        var engines = byEngine.Keys.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"| Возможность | Пример | {string.Join(" | ", engines)} |");
        sb.AppendLine($"|---|---|{string.Concat(engines.Select(_ => "---|"))}");
        for (var i = 0; i < Cases.Count; i++)
        {
            var c = Cases[i];
            var cells = engines.Select(e => Symbol(byEngine[e][i]));
            sb.AppendLine($"| {c.Feature} | `{c.Query.Replace("|", "\\|")}` | {string.Join(" | ", cells)} |");
        }

        var notes = byEngine.Values.SelectMany(o => o).Where(o => o.Status != SupportStatus.Supported).ToList();
        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Подробности отказов:");
            sb.AppendLine();
            foreach (var n in notes)
                sb.AppendLine($"* **{n.Engine}**, {n.Case.Feature}: {Shorten(n.Detail).Replace("|", "\\|")}");
        }

        return sb.ToString();

        static string Symbol(MatrixOutcome o) => o.Status switch
        {
            SupportStatus.Supported => "✅",
            SupportStatus.WrongResult => "⚠️ неверный результат",
            _ => "❌",
        };

        static string Shorten(string s)
        {
            var line = s.ReplaceLineEndings(" ");
            return line.Length > 220 ? line[..220] + "…" : line;
        }
    }
}
