using System.Runtime.CompilerServices;
using System.Text;
using FileTools.Core;

namespace FileTools.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RegisterCodePages() => TextEncodings.Register();
}

/// <summary>Thư mục tạm riêng cho mỗi test, tự xoá khi xong.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FileTools.Tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Ghi chữ với encoding cho trước, kèm BOM nếu encoding có preamble.</summary>
    public string Write(string name, string text, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(false);
        var path = File(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Text
{
    public static readonly Encoding Utf8 = new UTF8Encoding(false);
    public static readonly Encoding Utf8Bom = new UTF8Encoding(true);
    public static Encoding ShiftJis => TextEncodings.ShiftJis;

    /// <summary>n dòng "line 1".."line n", xuống dòng <paramref name="nl"/>, có / không xuống dòng cuối.</summary>
    public static string Lines(int n, string nl = "\r\n", bool trailing = true, string prefix = "line ")
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= n; i++)
        {
            sb.Append(prefix).Append(i);
            if (i < n || trailing)
            {
                sb.Append(nl);
            }
        }
        return sb.ToString();
    }

    public static List<TextLine> ReadAll(string path, Encoding? encoding = null)
    {
        using var reader = LineReader.Open(path, encoding);
        var list = new List<TextLine>();
        while (reader.TryRead(out var line))
        {
            list.Add(line);
        }
        return list;
    }
}
