using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace ModuleB.Models;

public sealed class SearchResultItem
{
    private SearchResultItem(string groupName, string fileName, string fullPath, string iconGlyph, SolidColorBrush iconBrush)
    {
        GroupName = groupName;
        FileName = fileName;
        FullPath = fullPath;
        IconGlyph = iconGlyph;
        IconBrush = iconBrush;
    }

    public string GroupName { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public string IconGlyph { get; }

    public SolidColorBrush IconBrush { get; }

    public static SearchResultItem FromFile(string groupName, string fullPath)
    {
        const string documentGlyph = "\uE8A5";
        var (glyph, color) = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" or ".xlsm" => (documentGlyph, Colors.SeaGreen),
            ".docx" or ".doc" => (documentGlyph, Colors.RoyalBlue),
            ".pdf" => (documentGlyph, Colors.IndianRed),
            _ => (documentGlyph, Colors.Gray),
        };

        return new SearchResultItem(groupName, Path.GetFileName(fullPath), fullPath, glyph, new SolidColorBrush(color));
    }
}
