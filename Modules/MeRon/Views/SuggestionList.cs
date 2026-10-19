using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MeRon.Views;

/// <summary>
/// Danh sách gợi ý của AutoSuggestBox (ô khách / ô món): luôn thấy gợi ý đầu - cái Enter / Tab sẽ chọn.
/// Danh sách mở lên trên (ô ở thấp, cửa sổ nhỏ): WinUI đảo thứ tự cho gợi ý đầu nằm sát ô nhưng có lúc cuộn khuất nó
/// (2026-10-19) → cuộn tới gợi ý đầu sau khi danh sách hiện. Mở xuống dưới thì gợi ý đầu vốn ở trên cùng, không đổi gì.
/// </summary>
internal static class SuggestionList
{
    /// <summary>Gọi sau khi đặt ItemsSource / mở danh sách.</summary>
    public static void ShowFirst(AutoSuggestBox box) =>
        box.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (box.ItemsSource is not System.Collections.IList { Count: > 0 } items
                || Ui.FindByName(box, "SuggestionsPopup") is not Popup { IsOpen: true, Child: { } child }
                || Ui.FindByName(child, "SuggestionsList") is not ListView list)
            {
                return;
            }
            list.UpdateLayout();
            list.ScrollIntoView(items[0]);
        });

    /// <summary>Đang giữ Shift (Shift+Tab: chọn gợi ý nhưng con trỏ lùi như thường, không nhảy theo onChosen).</summary>
    public static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
