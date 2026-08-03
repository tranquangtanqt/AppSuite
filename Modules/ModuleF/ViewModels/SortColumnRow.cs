using CommunityToolkit.Mvvm.ComponentModel;

namespace ModuleF.ViewModels;

/// <summary>One editable row in the Sort dialog's priority list.</summary>
public sealed partial class SortColumnRow : ObservableObject
{
    [ObservableProperty]
    private int _columnIndex;

    [ObservableProperty]
    private bool _descending;
}
