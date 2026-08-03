# ModuleA: module mẫu chứng minh tính độc lập

## Bối cảnh

AppSuite cần chứng minh trước (bằng code chạy được, không chỉ tài liệu) rằng một Module WinUI 3 có
thể mở/build/chạy hoàn toàn tách rời `MainLauncher`, chỉ nhờ vào 2 điểm nối: `Common` (model/logging
dùng chung) và `MainLauncher/Config/modules.json` (đường dẫn exe, không phải reference). ModuleA là
module đầu tiên, đóng vai trò khuôn mẫu (template) để copy khi tạo `ModuleB`, `ModuleC`,... - vì vậy
thiết kế cố tình tối giản, không có nghiệp vụ thật.

## Thiết kế

- Cấu trúc chuẩn: `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Assets/`, `Package.appxmanifest`,
  `app.manifest`, `ModuleA.csproj`. `ModuleA.csproj` chỉ `ProjectReference` tới
  `..\..\Common\Common.csproj` - tuyệt đối không reference `MainLauncher.csproj`.
- `MainWindow` hiện Process ID (`Environment.ProcessId`) và danh sách tham số dòng lệnh
  (`Environment.GetCommandLineArgs()`) - đây là cách kiểm chứng trực quan rằng module hoạt động giống
  hệt nhau dù bị `MainLauncher` khởi động qua `Process.Start` (truyền `Arguments` từ `modules.json`)
  hay chạy trực tiếp từ Visual Studio/`dotnet run` (tham số rỗng).
- Không đọc `modules.json` hay bất kỳ config nào của launcher trong code của module.
- `MainLauncher/Config/modules.json` có sẵn entry `ModuleA` với `AutoStart: true` (không có
  `Arguments`) để minh hoạ trường hợp module tự khởi động cùng launcher.

## Kiểm chứng

1. Mở `Modules\ModuleA\ModuleA.csproj` riêng, đặt Startup Project, F5 - chạy được không cần mở
   `MainLauncher`, tham số hiện `(none)`.
2. Chạy qua `MainLauncher` (F5 `MainLauncher`, `AutoStart: true` tự bật ModuleA) - PID hiện đúng tiến
   trình con do `ProcessManager.StartProcess` tạo ra.
3. `dotnet build AppSuite.sln` - ModuleA build cùng solution mà không kéo theo lỗi biên dịch chéo với
   `MainLauncher`.
