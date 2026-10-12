using MeRon.Core;
using MeRon.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeRon.Views;

/// <summary>
/// Ô chọn khách hàng kiểu autocomplete (như AppUserAutocomplete của điện thoại): gõ tên / số điện thoại (không phân biệt
/// dấu) → chọn trong gợi ý; Enter chọn gợi ý đầu; xoá trắng ô = bỏ chọn. Rời ô mà chữ không khớp khách nào thì trả lại
/// tên khách đang chọn - tránh tưởng đã chọn mà thật ra chưa.
/// </summary>
internal static class CustomerSuggest
{
    private const int MaxSuggestions = 50;

    public static void Attach(AutoSuggestBox box, Func<IReadOnlyList<CustomerOption>> customers,
        Func<CustomerOption?> current, Action<CustomerOption?> choose)
    {
        box.DisplayMemberPath = nameof(CustomerOption.Display);
        box.TextMemberPath = nameof(CustomerOption.Name);
        box.Text = current()?.Name ?? string.Empty;

        List<CustomerOption> Matches(string text) =>
            customers().Where(c => TextSearch.Matches(c.Name, text) || TextSearch.Matches(c.Phone, text)).Take(MaxSuggestions).ToList();

        box.TextChanged += (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(sender.Text))
            {
                sender.ItemsSource = Matches(string.Empty);
                choose(null);
                return;
            }
            sender.ItemsSource = Matches(sender.Text);
        };

        box.QuerySubmitted += (sender, args) =>
        {
            var chosen = args.ChosenSuggestion as CustomerOption;
            if (chosen is null && !string.IsNullOrWhiteSpace(args.QueryText))
            {
                var matches = Matches(args.QueryText);
                chosen = matches.FirstOrDefault(c => TextSearch.Fold(c.Name) == TextSearch.Fold(args.QueryText.Trim())) ?? matches.FirstOrDefault();
            }
            if (chosen is not null)
            {
                sender.Text = chosen.Name;
                choose(chosen);
            }
        };

        box.GotFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                box.ItemsSource = Matches(string.Empty);
                box.IsSuggestionListOpen = true;
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
