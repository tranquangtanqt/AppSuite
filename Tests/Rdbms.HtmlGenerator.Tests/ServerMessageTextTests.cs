using Rdbms.HtmlGenerator.Services;

namespace Rdbms.HtmlGenerator.Tests;

/// <summary>Phần "Chi tiết" khi PostgreSQL tiếng Nhật gửi lỗi đăng nhập bằng Shift-JIS (Npgsql đọc như UTF-8 → U+FFFD).</summary>
public sealed class ServerMessageTextTests
{
    // "パスワード認証に失敗しました" bằng Shift-JIS, đọc như UTF-8 (byte không hợp lệ → U+FFFD) - giống Npgsql.
    private static readonly string Garbled = MakeGarbled();

    private static string MakeGarbled()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var shiftJis = System.Text.Encoding.GetEncoding(932).GetBytes("ユーザー \"sa\" のパスワード認証に失敗しました");
        return "28P01: " + new System.Text.UTF8Encoding(false, false).GetString(shiftJis);
    }

    [Fact]
    public void Chu_vo_thanh_dong_giai_thich_kem_ma_va_nghia_tieng_Nhat()
    {
        Assert.Contains('�', Garbled); // đúng là chữ vỡ như trên máy chủ thật

        var text = ServerMessageText.Clean(Garbled, "28P01");

        Assert.DoesNotContain('�', text);
        Assert.Contains("sai bảng mã", text);
        Assert.Contains("28P01", text);
        Assert.Contains("パスワード認証に失敗しました", text);
    }

    [Fact]
    public void Ma_la_khong_co_nghia_thi_chi_ghi_ma()
    {
        var text = ServerMessageText.Clean("XX000: ��", "XX000");

        Assert.EndsWith("mã lỗi PostgreSQL XX000.", text);
    }

    [Fact]
    public void Khong_co_ma_loi()
    {
        Assert.EndsWith("nên không hiện được.", ServerMessageText.Clean("��", null));
    }

    [Theory]
    [InlineData("28P01: password authentication failed for user \"sa\"")]
    [InlineData("28P01: ユーザー \"sa\" のパスワード認証に失敗しました")] // máy chủ UTF-8 tiếng Nhật: đọc được, giữ nguyên
    [InlineData("Failed to connect to 10.0.0.1:5432")]
    public void Thong_bao_doc_duoc_giu_nguyen(string message)
    {
        Assert.Equal(message, ServerMessageText.Clean(message, "28P01"));
    }
}
