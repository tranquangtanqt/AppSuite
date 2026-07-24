using MainLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MainLauncher.Controls;

/// <summary>Displays one module's name, status and Start/Stop/Restart actions.</summary>
public sealed partial class ModuleCard : UserControl
{
    public static readonly DependencyProperty ModuleProperty =
        DependencyProperty.Register(nameof(Module), typeof(ModuleViewModel), typeof(ModuleCard), new PropertyMetadata(null));

    public ModuleCard()
    {
        InitializeComponent();
    }

    public ModuleViewModel Module
    {
        get => (ModuleViewModel)GetValue(ModuleProperty);
        set => SetValue(ModuleProperty, value);
    }
}
