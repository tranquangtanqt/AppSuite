using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace FileTools.Views;

/// <summary>Kéo-thả file / thư mục từ Explorer vào trang (chỉ lấy đường dẫn, không có logic nghiệp vụ).
/// Nhận cả sự kiện con đã xử lý (handledEventsToo): ListView cho kéo đổi thứ tự tự đánh dấu Handled khi file thả lên nó.</summary>
internal static class DropHelper
{
    public static void Attach(UIElement target, Action<IReadOnlyList<string>> onDrop)
    {
        target.AllowDrop = true;
        target.AddHandler(UIElement.DragOverEvent, new DragEventHandler((_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Thả để chọn";
            }
        }), true);
        target.AddHandler(UIElement.DropEvent, new DragEventHandler(async (_, e) =>
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
        }), true);
    }
}
