using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MainLauncher.Converters;

/// <summary>Picks a stable accent color per module name, so each ModuleCard's icon square reads as visually distinct.</summary>
public sealed class ModuleNameToAccentBrushConverter : IValueConverter
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x0F, 0x6C, 0xBD), // blue
        Color.FromArgb(255, 0x8B, 0x5C, 0xF6), // purple
        Color.FromArgb(255, 0x00, 0x89, 0x7B), // teal
        Color.FromArgb(255, 0xCA, 0x5A, 0x0A), // orange
        Color.FromArgb(255, 0xC4, 0x2B, 0x1C), // red
        Color.FromArgb(255, 0x2A, 0x8A, 0x2A), // green
    ];

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var name = value as string ?? string.Empty;
        var index = Math.Abs(name.GetHashCode()) % Palette.Length;
        return new SolidColorBrush(Palette[index]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
