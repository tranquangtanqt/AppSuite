using FileTools.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FileTools.Views;

public sealed partial class TailPage : Page
{
    public TailViewModel ViewModel { get; } = new();

    public TailPage()
    {
        InitializeComponent();
        ScrollFit.Attach(Scroller, Root, 620);
        DropHelper.Attach(Root, paths => ViewModel.SourcePath = paths[0]);
        // Dòng mới → cuộn xuống cuối (trừ khi người dùng tắt "Tự cuộn" để đọc đoạn cũ).
        ViewModel.LinesAppended += (_, _) =>
        {
            if (ViewModel.AutoScroll && ViewModel.Lines.Count > 0)
            {
                LinesView.ScrollIntoView(ViewModel.Lines[^1]);
            }
        };
    }
}
