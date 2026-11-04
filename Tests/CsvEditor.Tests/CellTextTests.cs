using CsvEditor.Services;

namespace CsvEditor.Tests;

public class CellTextTests
{
    [Theory]
    [InlineData("a\rb", "a\r\nb", "a\r\nb")]
    [InlineData("a\rb", "a\nb", "a\nb")]
    [InlineData("a\rb\rc", "a\r\nb\r\nc", "a\r\nb\r\nc")]
    [InlineData("a\r\nb", "a\nb", "a\nb")]
    [InlineData("a\rb", "a\rb", "a\rb")]
    [InlineData("a\rb", "một dòng", "a\nb")]
    [InlineData("không xuống dòng", "a\r\nb", "không xuống dòng")]
    public void Newlines_follow_the_original_cell(string edited, string original, string expected)
    {
        Assert.Equal(expected, CellText.RestoreNewlines(edited, original));
    }

    [Fact]
    public void Unchanged_text_from_the_text_box_equals_the_original()
    {
        const string original = "Dòng một\r\nDòng hai\r\nDòng ba";

        Assert.Equal(original, CellText.RestoreNewlines(original.Replace("\r\n", "\r"), original));
    }

    [Theory]
    [InlineData("a\nb", true)]
    [InlineData("a\r\nb", true)]
    [InlineData("a\rb", true)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void Detects_multi_line_cells(string value, bool expected)
    {
        Assert.Equal(expected, CellText.IsMultiLine(value));
    }

    [Fact]
    public void Text_box_binding_cannot_change_the_cell_newlines()
    {
        var row = new CsvEditor.Models.CsvRow(["Dòng một\r\nDòng hai"]);

        row[0] = "Dòng một\rDòng hai";

        Assert.Equal("Dòng một\r\nDòng hai", row.GetCell(0));
    }
}
