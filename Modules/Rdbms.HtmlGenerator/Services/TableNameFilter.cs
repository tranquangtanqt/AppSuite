using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rdbms.HtmlGenerator.Models;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// "Bỏ qua bảng / view khi đọc" (Cài đặt): bỏ khỏi kết quả đọc các bảng / view khớp 1 trong các mẫu tên, vd bảng tạm
/// <c>tmp_%</c>, bảng sao lưu <c>%_bk</c>. Mẫu: <c>%</c> hoặc <c>*</c> = nhiều ký tự bất kỳ, <c>?</c> = 1 ký tự; <c>_</c> là chữ
/// thật (khác LIKE của SQL - <c>tmp_%</c> không bắt <c>tmpl_x</c>). Không phân biệt hoa thường. Mẫu có dấu chấm so với
/// <c>schema.bảng</c>, không có thì so với tên bảng. Trống = không lọc.
/// </summary>
public static class TableNameFilter
{
    /// <summary>Tách chuỗi Cài đặt thành các mẫu: ngăn cách bằng dấu phẩy, chấm phẩy hoặc xuống dòng; bỏ khoảng trắng, mẫu trống.</summary>
    public static List<string> ParsePatterns(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Regex ToRegex(string pattern)
    {
        var body = string.Concat(pattern.Select(c => c switch
        {
            '%' or '*' => ".*",
            '?' => ".",
            _ => Regex.Escape(c.ToString()),
        }));
        return new Regex($"^{body}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Bỏ bảng / view khớp mẫu cùng cột, khoá ngoại (của chính bảng đó), index, ràng buộc của nó. Khoá ngoại ở bảng
    /// khác trỏ tới bảng bị bỏ vẫn giữ (hiện như bảng tham chiếu ngoài phạm vi đọc). Trả kết quả + tên các bảng đã bỏ.</summary>
    public static (SchemaImportResult Result, List<string> Excluded) Apply(SchemaImportResult import, IReadOnlyCollection<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return (import, []);
        }
        var regexes = patterns.Select(p => (Regex: ToRegex(p), Qualified: p.Contains('.'))).ToList();
        bool Matches(string schema, string table) => regexes.Any(r =>
            r.Regex.IsMatch(r.Qualified && schema.Length > 0 ? $"{schema}.{table}" : table));

        var (tables, columns, foreignKeys, indexes, constraints) = import;
        var excluded = tables.Where(t => Matches(t.SourceSheet, t.TableName)).ToList();
        if (excluded.Count == 0)
        {
            return (import, []);
        }

        // Bảng nhận theo (schema, tên): 2 schema có bảng cùng tên mà chỉ 1 bên khớp mẫu (vd "audit.orders") thì bên kia giữ.
        var keys = excluded.Select(t => (Schema: t.SourceSheet, t.TableName)).ToHashSet();
        bool Dropped(string schema, string table) =>
            keys.Contains((schema, table)) || (schema.Length == 0 && excluded.Any(t => t.TableName == table));

        var result = new SchemaImportResult(
            tables.Where(t => !keys.Contains((t.SourceSheet, t.TableName))).ToList(),
            columns.Where(c => !Dropped(c.Schema, c.TableName)).ToList(),
            foreignKeys.Where(f => !Dropped(f.Schema, f.TableName)).ToList(),
            indexes.Where(i => !Dropped(i.Schema, i.TableName)).ToList(),
            constraints.Where(c => !Dropped(c.Schema, c.TableName)).ToList());
        return (result, excluded.Select(t => t.TableName).ToList());
    }
}
