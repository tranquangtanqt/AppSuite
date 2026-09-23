using System.Text.RegularExpressions;

namespace Mcf.Screen.HtmlGenerator.Services;

public readonly record struct ParsedFileName(string DocNumber, string ScreenCode, string Revision, string ScreenName);

/// <summary>
/// Splits a "画面説明書" file name into its parts. Observed convention across the source tree:
/// <c>{DocNumber}_{ScreenCode}_{Revision}_画面説明書（{ScreenName}）.xlsx</c>, e.g.
/// <c>M7-US-1102_MSBBP1210_r01.02_画面説明書（受注登録）.xlsx</c>. A handful of files (shared/common
/// docs like <c>M7-US-1100_M7_r01.02_画面説明書（共通）.xlsx</c>) still match this shape - they just
/// have a short "screen code" like "M7". Files that don't match at all fall back to using the file
/// name itself as the screen code, so import never fails outright on a naming outlier.
/// </summary>
public static class ScreenCodeParser
{
    private static readonly Regex Pattern = new(
        @"^(?<docno>[^_]+)_(?<code>[^_]+)_(?<rev>r[\d.]+)_画面説明書（(?<name>.+)）$",
        RegexOptions.Compiled);

    public static ParsedFileName Parse(string fileNameWithoutExtension)
    {
        var match = Pattern.Match(fileNameWithoutExtension);
        if (match.Success)
        {
            return new ParsedFileName(
                match.Groups["docno"].Value,
                match.Groups["code"].Value,
                match.Groups["rev"].Value,
                match.Groups["name"].Value);
        }

        return new ParsedFileName(string.Empty, fileNameWithoutExtension, string.Empty, fileNameWithoutExtension);
    }
}
