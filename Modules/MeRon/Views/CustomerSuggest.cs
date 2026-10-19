using MeRon.Core;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace MeRon.Views;

/// <summary>
/// Ô chọn khách hàng kiểu autocomplete (như AppUserAutocomplete của điện thoại): gõ tên / số điện thoại (không phân biệt
/// dấu) → chọn trong gợi ý; ↑ / ↓ tô gợi ý, Enter / Tab chọn gợi ý đang tô (chưa tô = gợi ý đầu); xoá trắng ô = bỏ chọn. Rời ô mà chữ không khớp khách nào thì trả lại
/// tên khách đang chọn - tránh tưởng đã chọn mà thật ra chưa.
/// </summary>
internal static class CustomerSuggest
{
    private const int MaxSuggestions = 50;

    public static void Attach(AutoSuggestBox box, Func<IReadOnlyList<CustomerOption>> customers,
        Func<CustomerOption?> current, Action<CustomerOption?> choose, Action? onChosen = null)
    {
        box.DisplayMemberPath = nameof(CustomerOption.Display);
        box.TextMemberPath = nameof(CustomerOption.Name);
        box.Text = current()?.Name ?? string.Empty;

        List<CustomerOption> Matches(string text) =>
            customers().Where(c => TextSearch.Matches(c.Name, text) || TextSearch.Matches(c.Phone, text)).Take(MaxSuggestions).ToList();

        // Trùng nguyên tên trước, không có thì gợi ý đầu.
        CustomerOption? Best(string text)
        {
            var matches = Matches(text);
            return matches.FirstOrDefault(c => TextSearch.Fold(c.Name) == TextSearch.Fold(text.Trim())) ?? matches.FirstOrDefault();
        }

        // Gợi ý đang tô bằng ↑ / ↓ (ô hiện tên của nó); gõ tiếp thì bỏ.
        CustomerOption? highlighted = null;
        box.SuggestionChosen += (_, args) => highlighted = args.SelectedItem as CustomerOption;

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

        // Tab / Shift+Tab: nhận gợi ý đang tô bằng ↑ / ↓, chưa tô thì gợi ý đầu (như Enter), rồi con trỏ đi tiếp như thường
        // (có onChosen thì Tab đi theo onChosen như Enter).
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Tab || string.IsNullOrWhiteSpace(box.Text))
            {
                return;
            }
            CustomerOption? pick;
            if (highlighted is not null && highlighted.Name == box.Text)
            {
                pick = highlighted;
            }
            else if (current() is { } selected && selected.Name == box.Text)
            {
                return;
            }
            else
            {
                pick = Best(box.Text);
            }
            if (pick is not null)
            {
                box.Text = pick.Name;
                choose(pick);
                CloseList();
                if (onChosen is not null && !SuggestionList.IsShiftDown())
                {
                    e.Handled = true;
                    onChosen();
                }
            }
        };

        box.TextChanged += (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            highlighted = null;
            if (string.IsNullOrWhiteSpace(sender.Text))
            {
                sender.ItemsSource = Matches(string.Empty);
                choose(null);
            }
            else
            {
                sender.ItemsSource = Matches(sender.Text);
            }
            SuggestionList.ShowFirst(sender);
        };

        box.QuerySubmitted += (sender, args) =>
        {
            var chosen = args.ChosenSuggestion as CustomerOption;
            if (chosen is null && !string.IsNullOrWhiteSpace(args.QueryText))
            {
                chosen = Best(args.QueryText);
            }
            if (chosen is not null)
            {
                sender.Text = chosen.Name;
                choose(chosen);
                onChosen?.Invoke();
            }
            // Bấm chuột vào gợi ý sau khi gõ phím: ô tự mở lại danh sách sau sự kiện này (phải bấm lần 2 mới đóng) →
            // đóng sau 1 nhịp và bỏ gợi ý cũ (gõ tiếp thì TextChanged nạp lại).
            CloseList();
        };

        box.GotFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                box.ItemsSource = Matches(string.Empty);
                box.IsSuggestionListOpen = true;
                SuggestionList.ShowFirst(box);
            }
        };

        box.LostFocus += (_, _) =>
        {
            var selected = current();
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                if (selected is not null)
                {
                    choose(null);
                }
                return;
            }
            if (selected is null || selected.Name != box.Text)
            {
                // Gõ đúng nguyên tên 1 khách mà chưa bấm chọn → nhận luôn; còn lại trả về khách đang chọn.
                var exact = customers().FirstOrDefault(c => TextSearch.Fold(c.Name) == TextSearch.Fold(box.Text.Trim()));
                if (exact is not null)
                {
                    box.Text = exact.Name;
                    choose(exact);
                }
                else
                {
                    box.Text = selected?.Name ?? string.Empty;
                }
            }
        };
    }
}
