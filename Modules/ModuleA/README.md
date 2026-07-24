# ModuleA

Ứng dụng WinUI 3 độc lập - module mẫu "A". Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ
`ProjectReference` tới `..\..\Common\Common.csproj` để dùng chung model/logging.

## Chạy độc lập (không cần MainLauncher)

1. Mở `ModuleA.csproj` trực tiếp trong Visual Studio (hoặc mở cả solution và chọn ModuleA làm
   Startup Project).
2. F5, hoặc từ dòng lệnh:

   ```powershell
   dotnet run --project Modules\ModuleA\ModuleA.csproj
   ```

Cửa sổ hiện Process ID và danh sách tham số dòng lệnh nhận được. Khi chạy trực tiếp, tham số sẽ là
`(none)`; khi được `MainLauncher` khởi động theo `modules.json` (trường `Arguments`), các tham số đó
sẽ hiện ra ở đây - dùng để xác nhận `ProcessManager.StartProcess` truyền tham số đúng.

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- `App.xaml.cs`/`MainWindow.xaml.cs` không có logic gì phụ thuộc vào việc ai đã khởi động tiến trình
  này - `Environment.GetCommandLineArgs()` hoạt động giống hệt dù chạy từ VS, từ `dotnet run`, hay từ
  `Process.Start` bên trong MainLauncher.

## Thêm module mới theo khuôn này

Sao chép cấu trúc `ModuleA` (csproj + App.xaml(.cs) + MainWindow.xaml(.cs) + Assets + manifest),
đổi tên/namespace, thêm entry mới vào `MainLauncher/Config/modules.json`. Không cần sửa gì trong
`MainLauncher`.
