using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MainLauncher.ViewModels;

/// <summary>1 ô tích trong danh sách Ẩn / hiện module (Dashboard và Settings dùng chung 1 danh sách).</summary>
public partial class ModuleVisibilityItem : ObservableObject
{
    private readonly Action<ModuleVisibilityItem> _changed;

    public ModuleVisibilityItem(string name, string description, bool isVisible, Action<ModuleVisibilityItem> changed)
    {
        Name = name;
        Description = description;
        this.isVisible = isVisible; // không gọi _changed lúc tạo
        _changed = changed;
    }

    public string Name { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool isVisible;

    partial void OnIsVisibleChanged(bool value) => _changed(this);
}
