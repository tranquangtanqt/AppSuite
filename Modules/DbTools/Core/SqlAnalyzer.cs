namespace DbTools.Core;

public enum StatementKind
{
    Other,
    CreateSchema,
    CreateType,
    CreateDomain,
    CreateSequence,
    CreateTable,
    CreateView,
    CreateFunction,
    CreateProcedure,
    CreateTrigger,
    CreateIndex,
    AddConstraint,
    AlterOther,
    Comment,
    Insert,
    Update,
    Delete,
    Truncate,
    Drop,
    /// <summary>BEGIN / COMMIT / ROLLBACK... trong file: phá transaction chung của cả lô.</summary>
    TransactionControl,
    /// <summary>Lệnh PostgreSQL không cho chạy trong transaction (CREATE DATABASE, VACUUM, ... CONCURRENTLY...).</summary>
    NotInTransaction,
}

/// <summary>Tên đối tượng (có thể kèm schema). <see cref="Key"/> = tên không schema để so khớp: tên thường viết thường,
/// tên trong "..." giữ nguyên.</summary>
public sealed record QualifiedName(string? Schema, string Name, bool Quoted)
{
    public string Key => Name;

    /// <summary>Viết lại để đưa vào câu SQL (DROP ...).</summary>
    public string Sql => (Schema is null ? string.Empty : Quote(Schema, false) + ".") + Quote(Name, Quoted);

    public override string ToString() => Sql;

    private static string Quote(string name, bool quoted) =>
        quoted || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '$') || char.IsUpper(c))
            ? "\"" + name.Replace("\"", "\"\"") + "\""
            : name;
}

/// <summary>Kết quả đọc 1 câu: loại lệnh + đối tượng nó tạo / đụng tới + các tên được nhắc tới.</summary>
public sealed class StatementInfo
{
    public required SqlStatement Statement { get; init; }
    public StatementKind Kind { get; init; }

    /// <summary>Đối tượng được tạo (bảng, view, hàm, trigger, index, ràng buộc...).</summary>
    public QualifiedName? Object { get; init; }

    /// <summary>Bảng liên quan: trigger / index / ràng buộc gắn vào bảng nào; INSERT / UPDATE / DELETE vào bảng nào.</summary>
    public QualifiedName? Table { get; init; }

    /// <summary>CREATE OR REPLACE / IF NOT EXISTS: chạy lại không lỗi khi đối tượng đã có.</summary>
    public bool Rerunnable { get; init; }

    public bool Materialized { get; init; }

    /// <summary>Danh sách tham số của hàm / procedure đã bỏ giá trị mặc định - để viết DROP FUNCTION f(...).</summary>
    public string? FunctionArgs { get; init; }

    /// <summary>Mọi tên (không schema) xuất hiện trong câu, kể cả trong thân hàm.</summary>
    public required IReadOnlySet<string> Names { get; init; }

    /// <summary>Từ khoá đầu câu (vd "CREATE TABLE", "DELETE") để báo cho người dùng.</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>Xoá / sửa dữ liệu hoặc xoá đối tượng - cần cảnh báo trước khi chạy.</summary>
    public bool ChangesData => Kind is StatementKind.Delete or StatementKind.Update or StatementKind.Truncate or StatementKind.Drop
        || (Kind == StatementKind.AlterOther && Names.Contains("drop"));

    public bool CreatesObject => Kind is StatementKind.CreateSchema or StatementKind.CreateType or StatementKind.CreateDomain
        or StatementKind.CreateSequence or StatementKind.CreateTable or StatementKind.CreateView or StatementKind.CreateFunction
        or StatementKind.CreateProcedure or StatementKind.CreateTrigger or StatementKind.CreateIndex or StatementKind.AddConstraint;
}

/// <summary>Nhận loại lệnh của 1 câu SQL (PostgreSQL) chỉ bằng vài từ đầu - không cần server.</summary>
public static class SqlAnalyzer
{
    private static readonly HashSet<string> TransactionWords = ["begin", "commit", "rollback", "end", "savepoint", "release", "abort"];

    public static StatementInfo Analyze(SqlStatement statement)
    {
        var tokens = SqlLexer.Tokenize(statement.Text);
        var names = tokens.Where(t => t.IsName).Select(t => t.Value).ToHashSet(StringComparer.Ordinal);
        var r = new Reader(tokens);
        StatementInfo Make(StatementKind kind, QualifiedName? obj = null, QualifiedName? table = null, bool rerunnable = false,
            bool materialized = false, string? args = null) => new()
            {
                Statement = statement,
                Kind = kind,
                Object = obj,
                Table = table,
                Rerunnable = rerunnable,
                Materialized = materialized,
                FunctionArgs = args,
                Names = names,
                Verb = Verb(tokens),
            };

        if (tokens.Count == 0)
        {
            return Make(StatementKind.Other);
        }
        var first = tokens[0];
        if (first.Kind != SqlTokenKind.Word)
        {
            return Make(StatementKind.Other);
        }

        switch (first.Value)
        {
            case "start" when r.At(1, "transaction"):
                return Make(StatementKind.TransactionControl);
            case var w when TransactionWords.Contains(w):
                // "BEGIN" của 1 khối DO / hàm không đứng đầu câu nên không lẫn.
                return Make(StatementKind.TransactionControl);
            case "vacuum":
                return Make(StatementKind.NotInTransaction);
            case "insert":
                r.Pos = 1;
                r.Accept("into");
                return Make(StatementKind.Insert, table: r.ReadName());
            case "update":
                r.Pos = 1;
                r.Accept("only");
                return Make(StatementKind.Update, table: r.ReadName());
            case "delete":
                r.Pos = 1;
                r.Accept("from");
                r.Accept("only");
                return Make(StatementKind.Delete, table: r.ReadName());
            case "truncate":
                r.Pos = 1;
                r.Accept("table");
                r.Accept("only");
                return Make(StatementKind.Truncate, table: r.ReadName());
            case "comment":
                return Make(StatementKind.Comment);
            case "drop":
                return r.At(1, "database") || r.At(1, "tablespace") || (r.At(1, "index") && r.At(2, "concurrently"))
                    ? Make(StatementKind.NotInTransaction)
                    : Make(StatementKind.Drop);
            case "alter":
                return AnalyzeAlter(r, Make);
            case "create":
                return AnalyzeCreate(r, Make);
            case "reindex" when r.Has("concurrently"):
                return Make(StatementKind.NotInTransaction);
            default:
                return Make(StatementKind.Other);
        }
    }

    private delegate StatementInfo Maker(StatementKind kind, QualifiedName? obj = null, QualifiedName? table = null,
        bool rerunnable = false, bool materialized = false, string? args = null);

    private static StatementInfo AnalyzeAlter(Reader r, Maker make)
    {
        if (r.At(1, "system") || r.At(1, "database") && r.Has("tablespace"))
        {
            return make(StatementKind.NotInTransaction);
        }
        if (r.At(1, "type") && r.Has("add") && r.Has("value"))
        {
            return make(StatementKind.Other);
        }
        if (!r.At(1, "table"))
        {
            return make(StatementKind.AlterOther);
        }
        r.Pos = 2;
        if (r.Accept("if"))
        {
            r.Accept("exists");
        }
        r.Accept("only");
        var table = r.ReadName();
        // ALTER TABLE t ADD CONSTRAINT c ... (1 thao tác) - các dạng khác (nhiều thao tác, DROP...) là AlterOther.
        if (r.Accept("add") && r.Accept("constraint"))
        {
            var constraint = r.ReadName();
            if (constraint is not null && !r.Rest().Any(t => t.IsSymbol(',') && r.Depth(t) == 0))
            {
                return make(StatementKind.AddConstraint, constraint, table);
            }
        }
        return make(StatementKind.AlterOther, table: table);
    }

    private static StatementInfo AnalyzeCreate(Reader r, Maker make)
    {
        r.Pos = 1;
        var orReplace = r.Accept("or") && r.Accept("replace");
        // Từ bổ nghĩa đứng trước loại đối tượng.
        var materialized = false;
        while (true)
        {
            if (r.Accept("temp") || r.Accept("temporary") || r.Accept("unlogged") || r.Accept("global") || r.Accept("local")
                || r.Accept("recursive") || r.Accept("trusted") || r.Accept("procedural") || r.Accept("constraint")
                || r.Accept("unique"))
            {
                continue;
            }
            if (r.Accept("materialized"))
            {
                materialized = true;
                continue;
            }
            break;
        }
        var kindWord = r.Next()?.Value;
        switch (kindWord)
        {
            case "database" or "tablespace":
                return make(StatementKind.NotInTransaction);
            case "schema":
            {
                var ine = r.IfNotExists();
                return make(StatementKind.CreateSchema, r.ReadName(), rerunnable: ine);
            }
            case "sequence":
            {
                var ine = r.IfNotExists();
                return make(StatementKind.CreateSequence, r.ReadName(), rerunnable: ine);
            }
            case "table":
            {
                var ine = r.IfNotExists();
                var name = r.ReadName();
                return make(StatementKind.CreateTable, name, rerunnable: ine);
            }
            case "view":
            {
                var ine = r.IfNotExists();
                return make(StatementKind.CreateView, r.ReadName(), rerunnable: orReplace || ine, materialized: materialized);
            }
            case "function" or "procedure":
            {
                var name = r.ReadName();
                var args = r.ReadParenthesized();
                return make(kindWord == "function" ? StatementKind.CreateFunction : StatementKind.CreateProcedure, name,
                    rerunnable: orReplace, args: args is null ? null : CleanArgs(args));
            }
            case "trigger":
            {
                var name = r.ReadName();
                // ... BEFORE INSERT OR UPDATE ON <bảng>
                QualifiedName? table = null;
                if (r.SkipTo("on"))
                {
                    table = r.ReadName();
                }
                return make(StatementKind.CreateTrigger, name, table, rerunnable: orReplace);
            }
            case "index":
            {
                if (r.Accept("concurrently"))
                {
                    return make(StatementKind.NotInTransaction);
                }
                var ine = r.IfNotExists();
                QualifiedName? name = null;
                if (!r.AtWord("on"))
                {
                    name = r.ReadName();
                }
                QualifiedName? table = null;
                if (r.Accept("on"))
                {
                    r.Accept("only");
                    table = r.ReadName();
                }
                return make(StatementKind.CreateIndex, name, table, rerunnable: ine);
            }
            case "type":
            {
                var ine = r.IfNotExists();
                return make(StatementKind.CreateType, r.ReadName(), rerunnable: ine);
            }
            case "domain":
                return make(StatementKind.CreateDomain, r.ReadName());
            default:
                return make(StatementKind.Other);
        }
    }

    /// <summary>"a integer DEFAULT 0, OUT b text" → "a integer, OUT b text" (DROP FUNCTION không nhận giá trị mặc định).</summary>
    internal static string CleanArgs(string args)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= args.Length; i++)
        {
            if (i < args.Length && args[i] == '(')
            {
                depth++;
            }
            else if (i < args.Length && args[i] == ')')
            {
                depth--;
            }
            else if (i == args.Length || (args[i] == ',' && depth == 0))
            {
                parts.Add(args[start..i]);
                start = i + 1;
            }
        }
        var cleaned = parts
            .Select(p =>
            {
                var tokens = SqlLexer.Tokenize(p);
                var cut = tokens.FindIndex(t => t.IsWord("default") || t.IsSymbol('='));
                var text = cut < 0 ? p : p[..tokens[cut].Offset];
                return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            })
            .Where(p => p.Length > 0);
        return string.Join(", ", cleaned);
    }

    private static string Verb(List<SqlToken> tokens)
    {
        var words = tokens.TakeWhile(t => t.Kind == SqlTokenKind.Word).Take(4).Select(t => t.Value.ToUpperInvariant()).ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }
        if (words[0] is "CREATE" or "ALTER" or "DROP" or "COMMENT")
        {
            var skip = new HashSet<string> { "OR", "REPLACE", "UNIQUE", "TEMP", "TEMPORARY", "UNLOGGED", "MATERIALIZED", "CONSTRAINT", "IF", "NOT", "EXISTS", "ON" };
            var kind = words.Skip(1).FirstOrDefault(w => !skip.Contains(w));
            var materialized = words.Contains("MATERIALIZED") ? "MATERIALIZED " : string.Empty;
            return kind is null ? words[0] : $"{words[0]} {materialized}{kind}";
        }
        return words[0];
    }

    /// <summary>Đọc token tuần tự (bỏ qua chữ hoa / thường - token Word đã viết thường).</summary>
    private sealed class Reader(List<SqlToken> tokens)
    {
        public int Pos { get; set; }

        public bool At(int index, string word) => index < tokens.Count && tokens[index].IsWord(word);

        public bool AtWord(string word) => At(Pos, word);

        public bool Has(string word) => tokens.Any(t => t.IsWord(word) || (word.Length == 1 && t.IsSymbol(word[0])));

        public SqlToken? Next() => Pos < tokens.Count ? tokens[Pos++] : null;

        public bool Accept(string word)
        {
            if (AtWord(word))
            {
                Pos++;
                return true;
            }
            return false;
        }

        public bool IfNotExists()
        {
            if (AtWord("if") && At(Pos + 1, "not") && At(Pos + 2, "exists"))
            {
                Pos += 3;
                return true;
            }
            return false;
        }

        /// <summary>Tên [schema.]tên ở vị trí hiện tại.</summary>
        public QualifiedName? ReadName()
        {
            if (Pos >= tokens.Count || !tokens[Pos].IsName)
            {
                return null;
            }
            var parts = new List<SqlToken> { tokens[Pos++] };
            while (Pos + 1 < tokens.Count && tokens[Pos].IsSymbol('.') && tokens[Pos + 1].IsName)
            {
                parts.Add(tokens[Pos + 1]);
                Pos += 2;
            }
            var last = parts[^1];
            var schema = parts.Count >= 2 ? parts[^2].Value : null;
            return new QualifiedName(schema, last.Value, last.Kind == SqlTokenKind.QuotedName);
        }

        /// <summary>Đọc "( ... )" ở vị trí hiện tại, trả nội dung bên trong (dạng chữ gốc ghép lại từ token).</summary>
        public string? ReadParenthesized()
        {
            if (Pos >= tokens.Count || !tokens[Pos].IsSymbol('('))
            {
                return null;
            }
            var depth = 0;
            var parts = new List<string>();
            while (Pos < tokens.Count)
            {
                var t = tokens[Pos++];
                if (t.IsSymbol('('))
                {
                    depth++;
                    if (depth == 1)
                    {
                        continue;
                    }
                }
                else if (t.IsSymbol(')'))
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
                parts.Add(t.Kind switch
                {
                    SqlTokenKind.QuotedName => "\"" + t.Value.Replace("\"", "\"\"") + "\"",
                    SqlTokenKind.String => "'" + t.Value.Replace("'", "''") + "'",
                    _ => t.Value,
                });
            }
            // Ghép lại: dấu ( ) , . [ ] không cần khoảng trắng.
            var text = string.Join(' ', parts);
            foreach (var (from, to) in new[] { (" ( ", "("), ("( ", "("), (" (", "("), (" )", ")"), (" , ", ", "), (" . ", "."), (" [ ", "["), (" ]", "]") })
            {
                text = text.Replace(from, to);
            }
            return text;
        }

        public bool SkipTo(string word)
        {
            while (Pos < tokens.Count)
            {
                if (tokens[Pos].IsWord(word))
                {
                    Pos++;
                    return true;
                }
                Pos++;
            }
            return false;
        }

        public IEnumerable<SqlToken> Rest() => tokens.Skip(Pos);

        /// <summary>Độ sâu ngoặc tại token (tính từ vị trí hiện tại).</summary>
        public int Depth(SqlToken token)
        {
            var depth = 0;
            for (var i = Pos; i < tokens.Count && tokens[i].Offset < token.Offset; i++)
            {
                if (tokens[i].IsSymbol('('))
                {
                    depth++;
                }
                else if (tokens[i].IsSymbol(')'))
                {
                    depth--;
                }
            }
            return depth;
        }
    }
}
