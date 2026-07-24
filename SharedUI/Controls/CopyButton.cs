using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SharedUI.Helpers;

namespace SharedUI.Controls;

/// <summary>
/// A Button that plays a checkmark animation and announces <see cref="CopiedMessage"/> for
/// accessibility when clicked - intended for "copy to clipboard" actions. Default style/template
/// lives in Themes/Generic.xaml - callers must merge that resource dictionary once in App.xaml.
/// </summary>
public sealed partial class CopyButton : Button
{
    public static readonly DependencyProperty CopiedMessageProperty =
        DependencyProperty.Register("CopiedMessage", typeof(string), typeof(CopyButton), new PropertyMetadata("Copied to clipboard"));

    public string CopiedMessage
    {
        get { return (string)GetValue(CopiedMessageProperty); }
        set { SetValue(CopiedMessageProperty, value); }
    }

    public CopyButton()
    {
        this.DefaultStyleKey = typeof(CopyButton);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetTemplateChild("CopyToClipboardSuccessAnimation") is Storyboard storyboard)
        {
            storyboard.Begin();
            UIHelper.AnnounceActionForAccessibility(this, CopiedMessage, "CopiedToClipboardActivityId");
        }
    }

    protected override void OnApplyTemplate()
    {
        Click -= CopyButton_Click;
        base.OnApplyTemplate();
        Click += CopyButton_Click;
    }
}
