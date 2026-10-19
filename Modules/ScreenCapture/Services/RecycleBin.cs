using Microsoft.VisualBasic.FileIO;

namespace ScreenCapture.Services;

/// <summary>Xoá file bằng cách chuyển vào Thùng rác (lấy lại được) - dùng khi đóng ảnh đã tự lưu.</summary>
internal static class RecycleBin
{
    public static void Send(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
}
