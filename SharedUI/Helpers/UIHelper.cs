using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace SharedUI.Helpers;

public static class UIHelper
{
    /// <summary>Raises a screen-reader notification without requiring visible UI (e.g. after a copy-to-clipboard action).</summary>
    public static void AnnounceActionForAccessibility(UIElement ue, string announcement, string activityId)
    {
        if (FrameworkElementAutomationPeer.FromElement(ue) is AutomationPeer peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent, announcement, activityId);
        }
    }
}
