# ModuleD

Ứng dụng WinUI 3 độc lập - module "D" với một form nhập liệu đơn giản (Họ tên / Email / Ghi chú).
Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới
`..\..\Common\Common.csproj` để dùng chung model/logging.

## Chạy độc lập (không cần MainLauncher)

1. Mở `ModuleD.csproj` trực tiếp trong Visual Studio (hoặc mở cả solution và chọn ModuleD làm
   Startup Project).
2. F5, hoặc từ dòng lệnh:

   ```powershell
   dotnet run --project Modules\ModuleD\ModuleD.csproj
   ```

Cửa sổ hiện form nhập liệu với các trường Họ tên, Email, Ghi chú và nút Lưu/Xóa. Đây là ví dụ tối
giản, chưa lưu dữ liệu ra ngoài tiến trình (chỉ validate và hiện thông báo qua `InfoBar`).

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- `App.xaml.cs`/`MainWindow.xaml.cs` không có logic gì phụ thuộc vào việc ai đã khởi động tiến trình
  này.
