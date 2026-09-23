namespace ScreenCapture.Models;

public enum CaptureMode
{
    FullScreen,
    Window,
    Region,
    FixedRegion,

    /// <summary>Not implemented in Phase 1 - see PLAN.md "Chưa làm". Kept as an extension point.</summary>
    Scroll,
}
