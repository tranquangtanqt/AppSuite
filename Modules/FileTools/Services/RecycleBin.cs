using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FileTools.Services;

/// <summary>Chuyển file vào Thùng rác (khôi phục lại được) qua SHFileOperation + FOF_ALLOWUNDO - app WinUI không có
/// Microsoft.VisualBasic.FileIO (cần WinForms).</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    public static void Send(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Không còn file.", path);
        }
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // Danh sách đường dẫn kết thúc bằng 2 ký tự null.
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted != 0 || File.Exists(path))
        {
            throw new Win32Exception(result, $"Không chuyển được vào Thùng rác (mã {result}).");
        }
    }
}
