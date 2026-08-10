using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModuleB.Models;
using ModuleB.Services;

namespace ModuleB;

public sealed partial class FileMatchesDialog : ContentDialog
{
    public string FileName { get; }

    public List<CellMatchItem> Matches { get; }

    public FileMatchesDialog(string fileName, List<CellMatchItem> matches)
    {
        FileName = fileName;
        Matches = matches;
        InitializeComponent();
    }

    private async void OpenCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CellMatchItem match })
        {
            return;
        }

        try
        {
            await ExcelInteropService.OpenAndFocusCellAsync(match.FullPath, match.SheetName, match.CellReference);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Khong the mo Excel",
                Content = ex.Message,
                CloseButtonText = "Dong",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }
}
