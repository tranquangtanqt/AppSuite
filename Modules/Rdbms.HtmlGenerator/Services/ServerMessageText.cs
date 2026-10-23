using System.Collections.Generic;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>
/// Phần "Chi tiết" (thông báo gốc của máy chủ) trong hộp thoại lỗi. PostgreSQL trên Windows tiếng Nhật gửi lỗi đăng nhập bằng
/// Shift-JIS (CP932) TRƯỚC khi 2 bên thống nhất UTF-8, Npgsql đọc như UTF-8 nên chữ vỡ thành "�p�X���[�h�F..." (byte không
/// đọc được đã thay bằng U+FFFD, không khôi phục được). Khi đó thay bằng 1 dòng giải thích + mã lỗi (+ nghĩa tiếng Nhật chuẩn
/// của mã hay gặp) thay vì in chữ rác. Không đoán / ép bảng mã: máy chủ Linux tiếng Nhật lại dùng UTF-8 / EUC-JP.
/// </summary>
public static class ServerMessageText
{
    private const char Replacement = '�';

    /// <summary>Nghĩa tiếng Nhật chuẩn (như thông báo của PostgreSQL) cho mã lỗi hay gặp lúc đăng nhập.</summary>
    private static readonly Dictionary<string, string> JapaneseMeaning = new()
    {
        ["28P01"] = "パスワード認証に失敗しました",
        ["28000"] = "認証に失敗しました（ユーザー / 接続元が許可されていません）",
        ["3D000"] = "データベースが存在しません",
        ["53300"] = "接続数が上限に達しています",
        ["57P03"] = "データベースシステムは起動処理中 / 停止中です",
        ["42501"] = "権限がありません",
    };

    /// <summary>Thông báo đọc được thì trả nguyên; có ký tự vỡ (U+FFFD) thì trả dòng giải thích kèm
    /// <paramref name="sqlState"/> (nếu có).</summary>
    public static string Clean(string message, string? sqlState)
    {
        if (!message.Contains(Replacement))
        {
            return message;
        }

        var text = "Thông báo gốc của máy chủ bị sai bảng mã (máy chủ dùng tiếng Nhật, gửi lỗi trước khi thống nhất UTF-8) nên không hiện được";
        if (string.IsNullOrEmpty(sqlState))
        {
            return text + ".";
        }

        text += $" - mã lỗi PostgreSQL {sqlState}";
        return JapaneseMeaning.TryGetValue(sqlState, out var meaning) ? $"{text} ({meaning})." : $"{text}.";
    }
}
