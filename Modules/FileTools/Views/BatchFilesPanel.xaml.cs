using FileTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class BatchFilesPanel : UserControl
{
    public static readonly DependencyProperty BatchProperty = DependencyProperty.Register(
        nameof(Batch), typeof(BatchViewModel), typeof(BatchFilesPanel), new PropertyMetadata(null, (d, _) => ((BatchFilesPanel)d).Bindings.Update()));

    public static readonly DependencyProperty DetailHeaderProperty = DependencyProperty.Register(
        nameof(DetailHeader), typeof(string), typeof(BatchFilesPanel), new PropertyMetadata(string.Empty, (d, _) => ((BatchFilesPanel)d).Bindings.Update()));

    public BatchFilesPanel()
    {
        InitializeComponent();
        DropHelper.Attach(this, paths => Batch?.AddPaths(paths));
    }

    public BatchViewModel? Batch
    {
        get => (BatchViewModel?)GetValue(BatchProperty);
        set => SetValue(BatchProperty, value);
    }

    /// <summary>Tiêu đề cột hiện trạng ("Encoding hiện tại", "Xuống dòng hiện tại").</summary>
    public string DetailHeader
    {
        get => (string)GetValue(DetailHeaderProperty);
        set => SetValue(DetailHeaderProperty, value);
    }
}
