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

| Trang (Tag) | Tên trên giao diện | ViewModel | Nội dung |
|---|---|---|---|
| Dashboard | Tổng quan | `DashboardViewModel` | Hero header + thẻ thống kê + lưới `ModuleCard` (xem bên dưới) |
| Modules | Danh sách module | `ModuleListViewModel` | `GridView` các `ModuleCard`, nút Tải lại |
| Logs | Nhật ký | `LogsViewModel` | Log runtime live (tối đa 500 dòng gần nhất) |
| Settings | Cài đặt | `SettingsViewModel` | Đường dẫn cấu hình, nút Tải lại modules.json, khi đóng MainLauncher, ô tích module hiện trên màn hình |

**Giao diện tiếng Việt có dấu (2026-10-22)**: mọi chữ trên giao diện (menu, tiêu đề trang, thẻ thống kê, nút Chạy / Dừng / Chạy
lại, trạng thái `ModuleViewModel.StatusText` = Đang chạy / Đang mở / Đang đóng / Đã dừng / Lỗi / Chưa rõ, mô tả module trong
`Config\modules.json`). Tên trong code / Tag / enum giữ tiếng Anh; nội dung dòng nhật ký giữ tiếng Anh (để tra lỗi). Thẻ thống
kê chia đều bề ngang (`ColumnDefinition Width="*" MaxWidth="200"`) - rộng cố định 180 thì cửa sổ hẹp bị cắt mất nút Làm mới.

**Chỉ 1 bản, đóng / mở lại khi còn module chạy** (2026-10-22):
- `App.TryBecomePrimaryInstance` (đầu `OnLaunched`): named mutex `Local\AppSuite.MainLauncher`. Đã có bản chạy → bản mới
  `AllowSetForegroundWindow(ASFW_ANY)`, `Set()` event `Local\AppSuite.MainLauncher.Activate` rồi `Environment.Exit(0)`; bản
  đầu có 1 luồng nền chờ event → `MainWindow.BringToFrontWithNotice` (bỏ thu nhỏ, `SetForegroundWindow`, InfoBar "AppSuite
  đang chạy" góc dưới phải, nền đặc, tự ẩn 6 giây).
- `MainWindow.AppWindow_Closing`: còn module chạy thì theo `LauncherSettings.CloseModulesOnExit` (Ask / CloseAll /
  KeepRunning, Settings có RadioButtons, lưu `launcher-settings.json` dạng chữ). Ask = ContentDialog "Đóng hết / Để chạy tiếp
  / Huỷ" + "Nhớ lựa chọn"; Đóng hết = `StopProcess` song song (Task.Run) rồi mới `Close()`.
- `IProcessManager.AttachRunningProcesses` (gọi trong `ModuleListViewModel.LoadModulesCoreAsync` trên luồng nền, lúc khởi động
  và Reload): tìm tiến trình cùng tên file và đúng đường dẫn .exe (`MainModule.FileName`) → gắn `Exited`, trạng thái Running →
  thẻ hiện Running, Stop được, AutoStart không mở bản thứ 2.

**Hướng dẫn (F1)** (2026-10-22): mục "Hướng dẫn (F1)" ở cuối menu trái (`SelectsOnInvoked="False"`, mở ở
`NavView_ItemInvoked` - không đổi trang) hoặc phím F1 (`KeyboardAccelerator` trên NavigationView) mở cửa sổ Hướng dẫn
dùng chung `SharedUI.Help.HelpWindow` (1 cửa sổ, đóng launcher thì đóng theo). Nội dung ở `Views\HelpContent.cs` - thêm /
đổi tính năng thì cập nhật cả 2 chỗ.

`ModuleCard` (trong `Controls/`) là `UserControl` nhận `ModuleViewModel` qua `DependencyProperty
Module`, hiển thị tên/mô tả/trạng thái (chấm màu qua `ModuleStatusToBrushConverter`) và 3 nút
Start/Stop/Restart gọi thẳng `RelayCommand` trên `ModuleViewModel`.

### Dashboard - modeled on WinUI Gallery's HomePage

`DashboardPage` được thiết kế theo đúng cấu trúc trang Home của WinUI Gallery: một hero header
(`Controls/DashboardHeader.xaml`) rồi đến nội dung chính bên dưới trong cùng một `ScrollViewer`.

- **DashboardHeader**: dùng lại **chính asset gốc** của WinUI Gallery - `Assets/GalleryHeaderImage.png`
  làm ảnh nền, làm mờ dần bằng `SharedUI.Controls.OpacityMaskView` (đúng kỹ thuật `HomePageHeader`),
  tiêu đề "AppSuite" + subtitle động (`DashboardViewModel.HeaderSubtitle`, ví dụ "1/2 module đang
  chạy"). Hàng tile liên kết tài liệu (WinUI / Windows App SDK / Gallery / Toolkit) kiểu HomePage gốc
  đã bỏ (2026-09-29, người dùng thấy thừa) - ảnh nền header thu còn cao 170.
- **Card module**: chiều cao cố định (`ModuleCard` Height 186) - `GridView` (ItemsWrapGrid) lấy kích
  thước ô theo card đầu tiên, card cao hơn sẽ bị cắt mất hàng nút Start/Stop/Restart. Tên module 1
  dòng, mô tả tối đa 2 dòng, quá thì `…` + tooltip đầy đủ.
- Cửa sổ launcher mở ở trạng thái maximize (`App.OnLaunched`).
- **Bên dưới header**: thẻ thống kê Total/Running/Stopped (không đổi so với trước), rồi một
  `GridView` các `ModuleCard` - tương đương phần "Recently added samples" GridView của HomePage,
  nhưng hiển thị module thay vì control sample. Mỗi `ModuleCard` có icon vuông màu (màu suy ra từ
  tên module qua `ModuleNameToAccentBrushConverter`) và chấm trạng thái ở góc trên-phải, giống style
  card của Gallery. `DashboardViewModel.Modules` chỉ là tham chiếu tới `ModuleListViewModel.Modules`
  (cùng một collection instance) nên Dashboard và Module List luôn đồng bộ trạng thái mà không cần
  logic tải riêng.
- **Ẩn / hiện module (Dashboard + Module List, 2026-10-22)**: lưới của cả 2 trang bind `DashboardViewModel.VisibleModules`
  = các module không có trong `LauncherSettings.HiddenModules`, cộng module bị ẩn nhưng đang chạy (để còn bấm Stop; tính
  lại ở mỗi `StatusChanged`). `Controls\ModuleVisibilityButton` (cạnh tiêu đề "Modules" của Dashboard và cạnh nút Reload của
  Module List): `VisibleSummary` ("Đang hiện 10 / 12") + `DropDownButton` "Ẩn / hiện" (flyout ô tích `VisibilityItems` +
  "Hiện tất cả"); trang Settings bind cùng `VisibilityItems` và luôn liệt kê đủ để bật lại. Đổi là lưu ngay
  `Config\launcher-settings.json` (`Services\LauncherSettingsStore`, ghi file tạm rồi thay; thiếu / hỏng = hiện hết) - tách
  khỏi `modules.json` để cập nhật / publish lại không mất lựa chọn. Thẻ thống kê Total / Running / Stopped vẫn tính đủ mọi
  module. Ẩn hết thì hiện dòng nhắc.

Các file `.png` dùng cho header được copy trực tiếp từ
`D:\Project\Tantq\WinUI\WinUI-Gallery\WinUIGallery\Assets\` (Microsoft, MIT license, dự án mẫu công
khai) vào `MainLauncher/Assets/`.

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
của mọi module vào đúng vị trí `modules.json` trỏ tới (xem README gốc). Nếu không, các nút
Start trên trang Module List sẽ set trạng thái `Error` với lý do "Executable not found" - đây là xử
lý lỗi mong đợi (không throw, không crash launcher), không phải bug.
