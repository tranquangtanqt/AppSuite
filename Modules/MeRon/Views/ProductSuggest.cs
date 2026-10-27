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
/// không đi qua ô này. Gõ kèm số lượng ("2 chao" / "chao 2", <see cref="QuantityText"/>) → gợi ý theo tên, chọn được món thì
/// đặt luôn số lượng đó và con trỏ sang nút Thêm món (bỏ qua ô Số lượng). Tên món có số ở cuối ("Combo 2") vẫn ưu tiên khớp nguyên chữ.
/// </summary>
internal static class ProductSuggest
{
    private const int MaxSuggestions = 50;

    /// <summary>Gắn trong Loaded của ô (ô nằm trong DataTemplate, DataContext = dòng). Gắn 1 lần / ô.
    /// <paramref name="onChosen"/>: gọi sau khi chọn được món (bấm gợi ý / Enter / Tab) - form đưa con trỏ sang ô Số lượng.
    /// <paramref name="onChosenWithQuantity"/>: thay cho onChosen khi đã gõ kèm số lượng - form đưa con trỏ sang nút Thêm món.</summary>
    public static void Attach(AutoSuggestBox box, Action? onChosen = null, Action? onChosenWithQuantity = null)
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

        // Gợi ý theo chữ gõ: tách được số lượng mà không món nào có đúng số đó trong tên ("Combo 2") → gợi ý theo phần tên,
        // kèm số lượng ("4 yen" không bị "Yến chưng 40k" giữ lại vì 4 nằm trong 40).
        (List<ProductOption> Matches, int? Quantity) Suggest(string text)
        {
            var matches = Matches(text);
            if (QuantityText.Split(text) is not (var name, { } quantity))
            {
                return (matches, null);
            }
            var named = matches.Where(p => QuantityText.HasNumber(p.Name, quantity)).ToList();
            if (named.Count > 0)
            {
                return (named, null);
            }
            var byName = Matches(name);
            return byName.Count > 0 ? (byName, quantity) : (matches, null);
        }

        // Món + số lượng cho chữ gõ (Enter / Tab chưa tô gợi ý): trùng nguyên tên trước, rồi trùng tên sau khi tách số, rồi gợi ý đầu.
        (ProductOption? Product, int? Quantity) Pick(string text)
        {
            if (Exact(text) is { } exact)
            {
                return (exact, null);
            }
            var (matches, quantity) = Suggest(text);
            return quantity is null
                ? (matches.FirstOrDefault(), null)
                : (Exact(QuantityText.Split(text).Name) ?? matches.FirstOrDefault(), quantity);
        }

        // Số lượng gõ kèm ở lần gõ phím gần nhất (↑ / ↓ tô gợi ý thì ô hiện tên món, mất số → nhớ ở đây).
        int? typedQuantity = null;

        void Choose(ProductOption product, int? quantity)
        {
            box.Text = product.Name;
            line.ChooseProduct(product);
            if (quantity is { } q)
            {
                line.Quantity = q;
            }
            typedQuantity = null;
            CloseList();
        }

        void AfterChosen(int? quantity) =>
            (quantity is not null && onChosenWithQuantity is not null ? onChosenWithQuantity : onChosen)?.Invoke();

        // Gợi ý đang tô bằng ↑ / ↓ (ô hiện tên của nó); gõ tiếp thì bỏ.
        ProductOption? highlighted = null;
        box.SuggestionChosen += (_, args) => highlighted = args.SelectedItem as ProductOption;

        // Bấm chuột vào gợi ý sau khi gõ phím: ô tự mở lại danh sách sau QuerySubmitted (phải bấm lần 2 mới đóng - người
        // dùng báo 2026-10-23, cùng lỗi ô khách đã sửa 2026-10-14) → đóng thêm 1 lần sau 1 nhịp và bỏ gợi ý cũ (gõ tiếp
        // thì TextChanged nạp lại, ô trống thì GotFocus mở lại cả danh sách).
        void CloseList()
        {
            highlighted = null;
            box.IsSuggestionListOpen = false;
            box.DispatcherQueue.TryEnqueue(() =>
            {
                box.IsSuggestionListOpen = false;
                box.ItemsSource = null;
            });
        }

        // Tab: nhận gợi ý đang tô bằng ↑ / ↓, chưa tô thì món trùng tên / gợi ý đầu (như Enter) → con trỏ sang Số lượng.
        // Shift+Tab: cũng nhận nhưng con trỏ lùi như thường. Không ra món nào → để LostFocus báo lỗi.
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Tab || string.IsNullOrWhiteSpace(box.Text))
            {
                return;
            }
            ProductOption? pick;
            int? quantity = null;
            if (highlighted is not null && highlighted.Name == box.Text)
            {
                pick = highlighted;
                quantity = typedQuantity;
            }
            else if (line.SelectedProduct is { } selected && selected.Name == box.Text)
            {
                return;
            }
            else
            {
                (pick, quantity) = Pick(box.Text);
            }
            if (pick is null)
            {
                return;
            }
            Choose(pick, quantity);
            if (!SuggestionList.IsShiftDown())
            {
                e.Handled = true;
                AfterChosen(quantity);
            }
        };

        box.TextChanged += (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            highlighted = null;
            var (matches, quantity) = Suggest(sender.Text);
            typedQuantity = quantity;
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
            // ↑ / ↓ rồi Enter: ChosenSuggestion là null, ô đang hiện tên gợi ý đã tô → nhận gợi ý đó kèm số đã gõ.
            var suggestion = args.ChosenSuggestion as ProductOption ?? (highlighted?.Name == args.QueryText ? highlighted : null);
            var (chosen, quantity) = suggestion is not null ? (suggestion, typedQuantity) : Pick(args.QueryText);
            if (chosen is null)
            {
                line.RejectProductText(args.QueryText);
                return;
            }
            Choose(chosen, quantity);
            AfterChosen(quantity);
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
            // Gõ đúng nguyên tên 1 món (không dấu cũng được, kèm số lượng cũng được) mà chưa bấm chọn → nhận luôn; còn lại là lỗi.
            if (Exact(text) is { } exact)
            {
                Choose(exact, null);
            }
            else if (QuantityText.Split(text) is (var name, { } quantity) && Exact(name) is { } named)
            {
                Choose(named, quantity);
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
