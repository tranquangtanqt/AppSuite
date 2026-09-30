using FileTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class JobPanel : UserControl
{
    public static readonly DependencyProperty JobProperty = DependencyProperty.Register(
        nameof(Job), typeof(JobViewModel), typeof(JobPanel), new PropertyMetadata(null, (d, _) => ((JobPanel)d).Bindings.Update()));

    public JobPanel() => InitializeComponent();

    public JobViewModel? Job
    {
        get => (JobViewModel?)GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }
}
