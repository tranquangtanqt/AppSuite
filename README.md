# AppSuite

Một "Application Hub" viết bằng WinUI 3: **MainLauncher** khởi động, theo dõi và điều khiển
nhiều ứng dụng con (**Module**) độc lập, mỗi module là một exe WinUI 3 có thể mở, build và chạy
riêng mà không cần biết đến MainLauncher.

```
MainLauncher.exe
    |
    |-- Modules\ModuleA\ModuleA.exe
    |-- Modules\ModuleB\ModuleB.exe
    |-- ... (them ModuleC theo cung mot khuon mau)
```

## Cấu trúc solution

```
AppSuite.sln
|
|-- Common/                    Thu vien dung chung (khong phu thuoc WinUI)
|   |-- Interfaces/             IModuleInfo, IModuleService, IProcessManager
|   |-- Models/                 ModuleConfig, ModuleStatus, ModuleProcessInfo, LogEntry
|   |-- Utilities/              JsonConfigLoader (doc/ghi modules.json)
|   |-- Communication/          IModuleCommunicationChannel + Named Pipe reference impl
|   `-- Logging/                RollingFileLoggerProvider, InMemoryLoggerProvider
|
|-- SharedUI/                   WinUI 3 Class Library - control tai su dung (tu WinUI Gallery)
|   |-- Controls/                ColorSelector, CopyButton, Tile, OpacityMaskView, v.v.
|   `-- Themes/Generic.xaml      Style mac dinh cho CopyButton/OpacityMaskView (custom controls)
|
|-- MainLauncher/               Ung dung chinh (WinUI 3, MVVM, DI)
|   |-- App.xaml(.cs)           Dang ky DI container, logging, cau hinh
|   |-- MainWindow.xaml(.cs)    NavigationView shell
|   |-- Services/               ProcessManager, ModuleManager
|   |-- ViewModels/             DashboardViewModel, ModuleListViewModel, LogsViewModel, SettingsViewModel, ModuleViewModel
|   |-- Views/                  DashboardPage, ModuleListPage, LogsPage, SettingsPage
|   |-- Controls/               ModuleCard (UserControl hien thi 1 module)
|   |-- Converters/             ModuleStatusToBrushConverter
|   `-- Config/                 appsettings.json, modules.json
|
|-- Modules/
|   |-- ModuleA/                WinUI 3 app doc lap - vi du "Module A"
|   `-- ModuleB/                WinUI 3 app doc lap - vi du "Module B"
|
`-- build/
    |-- Sync-Modules-Dev.ps1    Tien ich cho F5/debug local (xem ben duoi)
    `-- Publish-AppSuite.ps1    Dong goi ra thu muc "Application\" de deploy
```

## Nguyên tắc kiến trúc

- **MainLauncher không có `ProjectReference` tới bất kỳ Module nào.** Nó chỉ biết module qua:
  1. File cấu hình `Config/modules.json` (đường dẫn exe, tham số, AutoStart).
  2. Contract dùng chung trong `Common` (`IModuleInfo`, `ModuleConfig`).
  3. Vị trí thư mục deploy quy ước (`.\Modules\<Tên module>\<Tên module>.exe`).
- **Mỗi Module chỉ `ProjectReference` tới `Common`** (để dùng chung model/logging), không bao giờ
  reference `MainLauncher`. Vì vậy mỗi module mở được, build được, chạy được hoàn toàn độc lập -
  đã kiểm chứng bằng cách mở `Modules\ModuleA\ModuleA.csproj` và chạy riêng.
- **`SharedUI` (control tái sử dụng) không tính vào luật trên** - luật chỉ cấm Module reference
  `MainLauncher`, không cấm reference project dùng chung khác. `MainLauncher` và bất kỳ Module nào
  đều có thể `ProjectReference` tới `SharedUI` mà không phá vỡ tính độc lập. Xem `SharedUI/README.md`
  để biết danh sách control đã port từ WinUI Gallery và cách merge `Themes/Generic.xaml`.
- **Giao tiếp launcher <-> module** được trừu tượng hoá qua `Common.Communication.IModuleCommunicationChannel`
  để sau này đổi từ Named Pipe (cài đặt mẫu có sẵn) sang REST local API hay gRPC mà không phải sửa
  code gọi nó.

## Build & chạy

Yêu cầu: Visual Studio 2022 17.14+ với workload ".NET Desktop Development" + "Windows App SDK", hoặc
.NET SDK 8.0+ với Windows App SDK đã cài qua NuGet (tự động khi restore).

### Chạy toàn bộ hệ thống (khuyến nghị khi phát triển)

```powershell
.\build\Sync-Modules-Dev.ps1
```

Script này build MainLauncher + ModuleA + ModuleB (cấu hình Debug mặc định) và copy output của
từng module vào đúng thư mục `Modules\<Tên module>\` bên cạnh `MainLauncher.exe` (giống layout lúc
deploy), để `modules.json` (đường dẫn tương đối `.\Modules\ModuleA\ModuleA.exe`) resolve đúng.
Sau đó mở solution trong Visual Studio, đặt **MainLauncher** làm Startup Project và nhấn F5.

> Đây chỉ là tiện ích cho local dev - **không** phải điều kiện bắt buộc để build. Mỗi module vẫn
> build/run độc lập bình thường dù bạn không chạy script này.

### Chạy riêng một module

Mở `Modules\ModuleA\ModuleA.csproj` (double-click hoặc "Open Project" trong VS), đặt làm Startup
Project, F5. Cửa sổ hiện PID và tham số dòng lệnh nhận được (rỗng khi chạy trực tiếp, có giá trị
khi MainLauncher truyền qua trường `Arguments` trong `modules.json`).

### Đóng gói để triển khai

```powershell
.\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64
```

Kết quả nằm ở `Application\`:

```
Application\
|-- MainLauncher.exe
|-- Modules\ModuleA\ModuleA.exe
|-- Modules\ModuleB\ModuleB.exe
`-- Config\modules.json, appsettings.json
```

## Cấu hình module (`MainLauncher/Config/modules.json`)

```json
[
  {
    "Name": "ModuleA",
    "Description": "Application quan ly A",
    "Path": ".\\Modules\\ModuleA\\ModuleA.exe",
    "Arguments": "",
    "AutoStart": true
  },
  {
    "Name": "ModuleB",
    "Description": "Application quan ly B",
    "Path": ".\\Modules\\ModuleB\\ModuleB.exe",
    "Arguments": "--mode=test",
    "AutoStart": false
  }
]
```

`Path` được resolve tương đối với thư mục chứa `MainLauncher.exe` (`ModuleConfig.ResolveExecutablePath`).
Muốn thêm `ModuleC`: tạo project WinUI 3 mới trong `Modules\ModuleC` theo đúng khuôn của ModuleA/ModuleB
(`ProjectReference` tới `Common`, không reference `MainLauncher`), thêm một entry vào `modules.json`,
xong - không cần sửa gì trong MainLauncher.

## Chức năng MainLauncher

- **Dashboard**: tổng số module / đang chạy / đã dừng, nút Refresh.
- **Module List**: thẻ (card) cho từng module - tên, mô tả, trạng thái (chấm màu), PID, nút
  Start/Stop/Restart - gọi thẳng `IProcessManager` (`System.Diagnostics.Process`).
- **Logs**: log runtime của launcher (load config, start/stop process, lỗi) hiển thị live, đồng thời
  ghi ra file `Logs\launcher-yyyy-MM-dd.log`.
- **Settings**: xem đường dẫn `modules.json` đang dùng, nút Reload để nạp lại cấu hình mà không cần
  khởi động lại launcher.

## Coding convention

- MVVM với [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- Dependency Injection với `Microsoft.Extensions.DependencyInjection`, đăng ký trong `App.xaml.cs`.
- Nullable reference types bật (`<Nullable>enable</Nullable>`) trên mọi project.
- Không viết logic nghiệp vụ trong code-behind của View - View chỉ bind tới ViewModel qua `x:Bind`.
- Mỗi project (`Common`, `MainLauncher`, `Modules\ModuleA`, `Modules\ModuleB`) có README riêng.

## Đã kiểm thử

- `dotnet build AppSuite.sln` - build thành công cả 5 project (Common, SharedUI, MainLauncher, ModuleA, ModuleB).
- Chạy `MainLauncher.exe` thực tế: load `modules.json`, tự auto-start `ModuleA` (do `AutoStart: true`),
  ghi log ra file và hiển thị trên UI - xem `MainLauncher/README.md` để biết chi tiết log mẫu.
- Chạy `MainLauncher.exe` sau khi merge `SharedUI/Themes/Generic.xaml` - không phát sinh lỗi runtime
  (`ms-appx:///SharedUI/Themes/Generic.xaml` resolve đúng dù app unpackaged).
