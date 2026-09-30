using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace FileTools.Views;

/// <summary>Kéo-thả file / thư mục từ Explorer vào trang (chỉ lấy đường dẫn, không có logic nghiệp vụ).</summary>
internal static class DropHelper
{
    public static void Attach(UIElement target, Action<IReadOnlyList<string>> onDrop)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Thả để chọn";
            }
        };
        target.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0)
            {
                onDrop(paths);
            }
        };
    }
}
