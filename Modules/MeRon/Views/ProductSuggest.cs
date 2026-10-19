using MeRon.Core;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MeRon.Views;

/// <summary>
/// Ô chọn món của 1 dòng đơn kiểu autocomplete: gõ (không phân biệt dấu, các từ không cần liền nhau: "bao xiu") → chọn
/// trong gợi ý (tên · giá); ↑ / ↓ tô gợi ý, Enter / Tab chọn gợi ý đang tô (chưa tô = gợi ý đầu). BẮT BUỘC chọn 1 món trong danh sách: rời ô mà chữ không phải tên 1 món
/// → giữ nguyên chữ, bỏ món đang chọn, báo lỗi đỏ dưới ô (Lưu cũng báo và đưa con trỏ về đây). Món tự do là dòng riêng,
/// không đi qua ô này.
/// </summary>
internal static class ProductSuggest
{
    private const int MaxSuggestions = 50;

    /// <summary>Gắn trong Loaded của ô (ô nằm trong DataTemplate, DataContext = dòng). Gắn 1 lần / ô.
    /// <paramref name="onChosen"/>: gọi sau khi chọn được món (bấm gợi ý / Enter / Tab) - form đưa con trỏ sang ô Số lượng.</summary>
    public static void Attach(AutoSuggestBox box, Action? onChosen = null)
    {
        if (box.Tag is "attached" || box.DataContext is not OrderLineViewModel line)
        {
            return;
        }
        box.Tag = "attached";
        box.DisplayMemberPath = nameof(ProductOption.Display);
        box.TextMemberPath = nameof(ProductOption.Name);
        box.Text = line.SelectedProduct?.Name ?? string.Empty;

        List<ProductOption> Matches(string text) => TextSearch.Filter(line.Products, p => p.Name, text, MaxSuggestions);

        ProductOption? Exact(string text) =>
            line.Products.FirstOrDefault(p => TextSearch.Fold(p.Name).Trim() == TextSearch.Fold(text).Trim()); // dữ liệu có tên thừa dấu cách ("Chè ")

        // Gợi ý đang tô bằng ↑ / ↓ (ô hiện tên của nó); gõ tiếp thì bỏ.
        ProductOption? highlighted = null;
        box.SuggestionChosen += (_, args) => highlighted = args.SelectedItem as ProductOption;

        // Tab: nhận gợi ý đang tô bằng ↑ / ↓, chưa tô thì món trùng tên / gợi ý đầu (như Enter) → con trỏ sang Số lượng.
        // Shift+Tab: cũng nhận nhưng con trỏ lùi như thường. Không ra món nào → để LostFocus báo lỗi.
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Tab || string.IsNullOrWhiteSpace(box.Text))
            {
                return;
            }
            ProductOption? pick;
            if (highlighted is not null && highlighted.Name == box.Text)
            {
                pick = highlighted;
            }
            else if (line.SelectedProduct is { } selected && selected.Name == box.Text)
            {
                return;
            }
            else
            {
                pick = Exact(box.Text) ?? Matches(box.Text).FirstOrDefault();
            }
            if (pick is null)
            {
                return;
            }
            highlighted = null;
            box.Text = pick.Name;
            line.ChooseProduct(pick);
            box.IsSuggestionListOpen = false;
            if (!SuggestionList.IsShiftDown())
            {
                e.Handled = true;
                onChosen?.Invoke();
            }
        };

        box.TextChanged += (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            highlighted = null;
            var matches = Matches(sender.Text);
            sender.ItemsSource = matches;
            SuggestionList.ShowFirst(sender);
            // Báo ngay khi gõ không ra món nào; gõ lại ra món thì tắt lỗi (chưa chọn thì Lưu vẫn báo).
            if (matches.Count == 0 && !string.IsNullOrWhiteSpace(sender.Text))
            {
                line.RejectProductText(sender.Text);
            }
            else if (line.HasProductError)
            {
                line.ProductError = string.Empty;
            }
        };

        box.QuerySubmitted += (sender, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.QueryText) && args.ChosenSuggestion is null)
            {
                return;
            }
            var chosen = args.ChosenSuggestion as ProductOption ?? Exact(args.QueryText) ?? Matches(args.QueryText).FirstOrDefault();
            if (chosen is null)
            {
                line.RejectProductText(args.QueryText);
                return;
            }
            highlighted = null;
            sender.Text = chosen.Name;
            line.ChooseProduct(chosen);
            sender.IsSuggestionListOpen = false;
            onChosen?.Invoke();
        };

        // Ô trống mà có con trỏ → mở sẵn cả danh sách. Mở sau 1 nhịp: ô vừa tạo (bấm Thêm món) thì popup gợi ý chưa sẵn
        // sàng, mở ngay trong GotFocus có lúc không hiện.
        void OpenAllIfEmpty() => box.DispatcherQueue.TryEnqueue(() =>
        {
            if (string.IsNullOrWhiteSpace(box.Text) && HasFocusWithin(box))
            {
                box.ItemsSource = Matches(string.Empty);
                box.IsSuggestionListOpen = true;
                SuggestionList.ShowFirst(box);
            }
        });

        box.GotFocus += (_, _) => OpenAllIfEmpty();
        OpenAllIfEmpty(); // con trỏ đã vào ô trước khi kịp gắn

        box.LostFocus += (_, _) =>
        {
            var text = box.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                line.ChooseProduct(null); // ô trống: Lưu báo "Chưa chọn món ăn."
                return;
            }
            if (line.SelectedProduct is { } selected && selected.Name == text)
            {
                return;
            }
            // Gõ đúng nguyên tên 1 món (không dấu cũng được) mà chưa bấm chọn → nhận luôn; còn lại là lỗi.
            if (Exact(text) is { } exact)
            {
                box.Text = exact.Name;
                line.ChooseProduct(exact);
            }
            else
            {
                line.RejectProductText(text);
            }
        };
    }

    /// <summary>Con trỏ đang ở trong ô (thật ra là TextBox bên trong AutoSuggestBox, nên không xem FocusState của ô được).</summary>
    private static bool HasFocusWithin(AutoSuggestBox box)
    {
        if (box.XamlRoot is null)
        {
            return false;
        }
        for (var element = FocusManager.GetFocusedElement(box.XamlRoot) as DependencyObject; element is not null;
             element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element == box)
            {
                return true;
            }
        }
        return false;
    }
}
