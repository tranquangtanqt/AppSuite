using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using ScreenCapture.Models;
using ScreenCapture.Services.Interop;

namespace ScreenCapture.Services;

/// <summary>
/// Phím tắt toàn cục (bấm được cả khi app đang thu nhỏ / không focus) qua Win32 <c>RegisterHotKey</c>,
/// gắn vào HWND của cửa sổ launcher. Windows báo phím bằng message WM_HOTKEY tới HWND đó → bắt qua
/// window subclass (<c>SetWindowSubclass</c>) vì WinUI 3 không có WndProc cho code ứng dụng.
///
/// Phím đã bị app khác giữ (vd PrintScreen khi bật "dùng phím Print Screen để mở Snipping Tool" của
/// Windows 11) thì RegisterHotKey thất bại → <see cref="Apply"/> trả về danh sách phím lỗi để báo.
/// Phím tắt chỉ hoạt động khi cửa sổ launcher còn mở (kể cả thu nhỏ).
/// </summary>
public sealed unsafe class HotkeyService : IDisposable
{
    private const nuint SubclassId = 0x5343; // "SC"
    private static readonly Dictionary<IntPtr, HotkeyService> Instances = [];

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<int> _registeredIds = [];

    public event EventHandler<HotkeyAction>? Pressed;

    public HotkeyService(IntPtr hwnd, DispatcherQueue dispatcher)
    {
        _hwnd = hwnd;
        _dispatcher = dispatcher;
        Instances[hwnd] = this;
        NativeMethods.SetWindowSubclass(hwnd, &SubclassProc, SubclassId, 0);
    }

    /// <summary>Huỷ đăng ký cũ rồi đăng ký lại theo <paramref name="bindings"/>. Trả về các binding
    /// không đăng ký được (đã bị app khác / Windows giữ).</summary>
    public List<HotkeyBinding> Apply(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        var failed = new List<HotkeyBinding>();
        foreach (var binding in bindings.Where(b => b.IsEnabled))
        {
            uint modifiers = NativeMethods.MOD_NOREPEAT
                | (binding.Alt ? NativeMethods.MOD_ALT : 0)
                | (binding.Ctrl ? NativeMethods.MOD_CONTROL : 0)
                | (binding.Shift ? NativeMethods.MOD_SHIFT : 0);
            int id = (int)binding.Action + 1;
            if (NativeMethods.RegisterHotKey(_hwnd, id, modifiers, HotkeyBinding.VirtualKeyOf(binding.Key)))
            {
                _registeredIds.Add(id);
            }
            else
            {
                failed.Add(binding);
            }
        }
        return failed;
    }

    private void UnregisterAll()
    {
        foreach (int id in _registeredIds)
        {
            NativeMethods.UnregisterHotKey(_hwnd, id);
        }
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        NativeMethods.RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
        Instances.Remove(_hwnd);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint refData)
    {
        if (msg == NativeMethods.WM_HOTKEY && Instances.TryGetValue(hwnd, out var service))
        {
            var action = (HotkeyAction)((int)wParam - 1);
            // Không chạy thao tác chụp ngay trong WndProc - xếp hàng lên UI thread rồi return.
            service._dispatcher.TryEnqueue(() => service.Pressed?.Invoke(service, action));
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
