using CsvEditor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CsvEditor;

/// <summary>Bản khôi phục (<see cref="RecoveryStore"/>): app sắp tắt vì lỗi mà còn thay đổi chưa lưu thì chép bảng ra
/// Data\Recovery; lần mở sau hỏi Mở bản khôi phục / Xoá / Để sau. Mở thì bảng gắn lại vào file gốc ở trạng thái chưa lưu (như
/// Word): người dùng xem lại rồi Lưu mới ghi vào file gốc, app không tự ghi đè; bản khôi phục không vào danh sách gần đây.</summary>
public sealed partial class MainWindow
{
    private static readonly ILogger RecoveryLog = AppLog.For("Recovery");
    private readonly RecoveryStore _recovery = new();

    /// <summary>Gọi từ App khi gặp lỗi không xử lý được. Đường dẫn bản khôi phục, null nếu không có gì chưa lưu / ghi lỗi.</summary>
    public string? SaveRecoveryIfDirty()
    {
        try
        {
            return ViewModel.IsDirty ? _recovery.Save(ViewModel.Document, DateTime.Now) : null;
        }
        catch (Exception ex)
        {
            RecoveryLog.LogError(ex, "Không lưu được bản khôi phục");
            return null;
        }
    }

    private void InitRecovery()
    {
        var offered = false;
        RootGrid.Loaded += async (_, _) =>
        {
            if (!offered)
            {
                offered = true;
                await OfferRecoveryAsync();
                await OpenStartupFileAsync();
            }
        };

        // Chỉ để test (GUI Sandbox): biến môi trường CSVEDITOR_CRASH_TEST=1 + Ctrl+Shift+F12 trên bảng gây lỗi chưa xử lý thật.
        // (KeyboardAccelerator không nhận khi tiêu điểm ở DataGrid - đã thử trong Sandbox.)
        if (Environment.GetEnvironmentVariable("CSVEDITOR_CRASH_TEST") == "1")
        {
            Grid.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.F12 && IsDown(VirtualKey.Control) && IsDown(VirtualKey.Shift))
                {
                    throw new InvalidOperationException("Lỗi thử (CSVEDITOR_CRASH_TEST)");
                }
            };
        }
    }

    private async Task OfferRecoveryAsync()
    {
        _recovery.Cleanup(DateTime.Now);
        foreach (var entry in _recovery.Pending())
        {
            var info = entry.Info;
            var name = info.OriginalPath is { } original ? Path.GetFileName(original) : "Bảng mới (chưa lưu lần nào)";
            var text = $"Lần trước CsvEditor bị tắt do lỗi khi {name} còn thay đổi chưa lưu. Các thay đổi đó đã được chép ra bản khôi phục "
                + $"lúc {info.SavedAt:HH:mm dd/MM/yyyy} ({info.RowCount:N0} dòng, {info.ColumnCount:N0} cột).\n\n"
                + (info.OriginalPath is { } path ? $"File gốc (không bị đổi): {path}\n" : string.Empty)
                + $"Bản khôi phục: {entry.Path}\n\n"
                + "Mở bản khôi phục: bảng hiện lại đúng như lúc lỗi, ở trạng thái chưa lưu. Kiểm tra rồi bấm Lưu (Ctrl+S) để ghi vào "
                + "file gốc, hoặc Xuất ▾ → Lưu thành file khác... để giữ file gốc.";
            var choice = await new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Có bản khôi phục chưa lưu",
                Content = SharedUI.Helpers.DialogContent.Scrollable(text),
                PrimaryButtonText = "Mở bản khôi phục",
                SecondaryButtonText = "Xoá bản khôi phục",
                CloseButtonText = "Để sau",
                DefaultButton = ContentDialogButton.Primary,
            }.ShowAsync();

            try
            {
                if (choice == ContentDialogResult.Secondary)
                {
                    _recovery.Dismiss(entry, deleteFile: true);
                    RecoveryLog.LogInformation("Xoá bản khôi phục: {Path}", entry.Path);
                    continue;
                }
                if (choice != ContentDialogResult.Primary)
                {
                    return; // Để sau: lần mở sau hỏi lại
                }

                ViewModel.HasHeader = info.HasHeader;
                if (await RunOpenAsync(entry.Path, ct => ViewModel.OpenAsync(entry.Path, info.GetEncoding(), info.Delimiter, ConfirmLargeFileAsync, ct), remember: false))
                {
                    ViewModel.AdoptRecoveredDocument(info.OriginalPath);
                    _recovery.Dismiss(entry, deleteFile: false);
                    RecoveryLog.LogInformation("Mở bản khôi phục: {Path} (thuộc {Original})", entry.Path, info.OriginalPath ?? "bảng mới");
                    ViewModel.StatusMessage = $"Đã khôi phục thay đổi chưa lưu của {name} - kiểm tra rồi Lưu (Ctrl+S) để ghi vào file gốc.";
                }
                return; // mở 1 bản; các bản khác (nếu có) hỏi lần mở sau
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                RecoveryLog.LogError(ex, "Không xử lý được bản khôi phục {Path}", entry.Path);
                ViewModel.StatusMessage = $"Không xử lý được bản khôi phục: {ex.Message}";
                return;
            }
        }
    }
}
