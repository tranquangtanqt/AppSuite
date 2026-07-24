using System;
using Common.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MainLauncher.Converters;

/// <summary>Maps a <see cref="ModuleStatus"/> to a status-dot color for ModuleCard.</summary>
public sealed class ModuleStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is ModuleStatus s ? s : ModuleStatus.Unknown;
        var color = status switch
        {
            ModuleStatus.Running => Colors.LimeGreen,
            ModuleStatus.Starting or ModuleStatus.Stopping => Colors.Orange,
            ModuleStatus.Error => Colors.Red,
            _ => Colors.Gray
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
