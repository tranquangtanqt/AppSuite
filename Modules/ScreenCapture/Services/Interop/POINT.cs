using System.Runtime.InteropServices;

namespace ScreenCapture.Services.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}
