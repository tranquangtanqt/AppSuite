using CommunityToolkit.Mvvm.ComponentModel;

namespace ModuleC.Models;

/// <summary>One "Tên bảng / Alias" pair from the left-hand input list.</summary>
public sealed partial class AliasRow : ObservableObject
{
    [ObservableProperty]
    private string _tableName = string.Empty;

    [ObservableProperty]
    private string _alias = string.Empty;
}
