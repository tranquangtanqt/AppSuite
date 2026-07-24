# MainLauncher

Ứng dụng chính (WinUI 3, .NET 8, unpackaged) - điểm khởi động của toàn bộ AppSuite. Quản lý danh
sách module, start/stop/restart process, theo dõi trạng thái, truyền tham số và ghi log.

## Kiến trúc

- **MVVM** với CommunityToolkit.Mvvm. View (`Views/*.xaml`) chỉ bind `x:Bind` tới ViewModel
  (`ViewModels/*.cs`); không có logic nghiệp vụ trong code-behind.
- **DI**: `App.xaml.cs` dựng `ServiceCollection` một lần khi khởi động (`ConfigureServices`), expose
  qua `App.Services` (static) để mỗi `Page`/`UserControl` resolve ViewModel trong constructor - đây
  là pattern chuẩn cho WinUI 3 vì `Frame.Navigate(typeof(Page))` không hỗ trợ constructor injection.
- **Services/ProcessManager.cs**: bọc `System.Diagnostics.Process` - `StartProcess`, `StopProcess`,
  `RestartProcess`, `CheckRunning`, `GetProcessStatus`, bắn event `StatusChanged` khi trạng thái đổi
  (kể cả khi module tự thoát ngoài ý muốn, qua `Process.Exited`).
- **Services/ModuleManager.cs**: đọc `Config/modules.json` (đường dẫn lấy từ `appsettings.json` ->
  khoá `ModulesConfigPath`) qua `Common.Utilities.JsonConfigLoader`.

## Điều hướng (NavigationView)

| Trang | ViewModel | Nội dung |
|---|---|---|
| Dashboard | `DashboardViewModel` | Hero header + thẻ thống kê + lưới `ModuleCard` (xem bên dưới) |
| Module List | `ModuleListViewModel` | `GridView` các `ModuleCard`, nút Reload |
| Logs | `LogsViewModel` | Log runtime live (tối đa 500 dòng gần nhất) |
| Settings | `SettingsViewModel` | Đường dẫn cấu hình, nút Reload modules.json |

`ModuleCard` (trong `Controls/`) là `UserControl` nhận `ModuleViewModel` qua `DependencyProperty
Module`, hiển thị tên/mô tả/trạng thái (chấm màu qua `ModuleStatusToBrushConverter`) và 3 nút
Start/Stop/Restart gọi thẳng `RelayCommand` trên `ModuleViewModel`.

### Dashboard - modeled on WinUI Gallery's HomePage

`DashboardPage` được thiết kế theo đúng cấu trúc trang Home của WinUI Gallery: một hero header
(`Controls/DashboardHeader.xaml`) rồi đến nội dung chính bên dưới trong cùng một `ScrollViewer`.

- **DashboardHeader**: dùng lại **chính asset gốc** của WinUI Gallery - `Assets/GalleryHeaderImage.png`
  làm ảnh nền, làm mờ dần bằng `SharedUI.Controls.OpacityMaskView` (đúng kỹ thuật `HomePageHeader`),
  tiêu đề "AppSuite" + subtitle động (`DashboardViewModel.HeaderSubtitle`, ví dụ "1/2 module đang
  chạy"), và một hàng tile liên kết cuộn ngang (`SharedUI.Controls.HorizontalScrollContainer` +
  `SharedUI.Controls.Tile`) dùng icon thật từ `Assets/HomeHeaderTiles/*.png` (WinUI, Windows Design,
  Toolkit) cộng vector GitHub octocat (`GitHubIconPath` resource, copy từ `App.xaml` của Gallery),
  trỏ tới tài liệu WinUI 3 / Windows App SDK / WinUI Gallery / Community Toolkit - đúng bộ tile
  "Getting started / Design / GitHub / Toolkit" trên HomePage gốc.
- **Bên dưới header**: thẻ thống kê Total/Running/Stopped (không đổi so với trước), rồi một
  `GridView` các `ModuleCard` - tương đương phần "Recently added samples" GridView của HomePage,
  nhưng hiển thị module thay vì control sample. Mỗi `ModuleCard` có icon vuông màu (màu suy ra từ
  tên module qua `ModuleNameToAccentBrushConverter`) và chấm trạng thái ở góc trên-phải, giống style
  card của Gallery. `DashboardViewModel.Modules` chỉ là tham chiếu tới `ModuleListViewModel.Modules`
  (cùng một collection instance) nên Dashboard và Module List luôn đồng bộ trạng thái mà không cần
  logic tải riêng.

Các file `.png` dùng cho header được copy trực tiếp từ
`D:\Project\Tantq\WinUI\WinUI-Gallery\WinUIGallery\Assets\` (Microsoft, MIT license, dự án mẫu công
khai) vào `MainLauncher/Assets/` và `MainLauncher/Assets/HomeHeaderTiles/`.

## Cấu hình

- `Config/appsettings.json`: `ModulesConfigPath` (mặc định `Config/modules.json`).
- `Config/modules.json`: danh sách module - xem README gốc của solution để biết schema.

Cả hai file được copy ra output (`CopyToOutputDirectory=PreserveNewest`) nên luôn nằm cạnh
`MainLauncher.exe` sau khi build.

## Log

Ghi đồng thời ra 2 nơi (đăng ký trong `ConfigureServices`):

1. `Common.Logging.InMemoryLoggerProvider` - nuôi trang **Logs** trong UI theo thời gian thực.
2. `Common.Logging.RollingFileLoggerProvider` - ghi ra `Logs\launcher-yyyy-MM-dd.log` cạnh exe.

## Build & chạy

```powershell
dotnet build MainLauncher\MainLauncher.csproj -p:Platform=x64
```

Trước khi F5 trong Visual Studio, chạy `..\build\Sync-Modules-Dev.ps1` một lần để copy build output
của ModuleA/ModuleB vào đúng vị trí `modules.json` trỏ tới (xem README gốc). Nếu không, các nút
Start trên trang Module List sẽ set trạng thái `Error` với lý do "Executable not found" - đây là xử
lý lỗi mong đợi (không throw, không crash launcher), không phải bug.
