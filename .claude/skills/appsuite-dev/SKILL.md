---
name: appsuite-dev
description: Use when working in the AppSuite solution (MainLauncher + Modules + Common + SharedUI) — adding a new module, building/running/publishing, or reusing a SharedUI control. Encodes the architecture rules from the project's README.md files so changes stay consistent with the launcher/module independence contract.
---

# AppSuite dev workflow

AppSuite là "Application Hub" WinUI 3: `MainLauncher` khởi động/điều khiển nhiều `Modules\*`
độc lập. Ba quy tắc kiến trúc bất biến (xem `README.md` gốc):

1. **`MainLauncher` không bao giờ `ProjectReference` tới bất kỳ Module nào.** Nó chỉ biết module
   qua `MainLauncher/Config/modules.json` + contract trong `Common` (`IModuleInfo`, `ModuleConfig`)
   + quy ước thư mục deploy `.\Modules\<Tên module>\<Tên module>.exe`.
2. **Mỗi Module chỉ `ProjectReference` tới `Common`** (model/logging dùng chung), **không bao giờ**
   reference `MainLauncher`. Đây là điều kiện để mở/build/chạy module hoàn toàn độc lập.
3. **`SharedUI` không tính vào luật #2** — mọi project (MainLauncher lẫn Module) đều được phép
   `ProjectReference` tới `SharedUI` mà không phá vỡ tính độc lập.

## Thêm Module mới

1. Copy cấu trúc `Modules\ModuleA` (csproj + `App.xaml(.cs)` + `MainWindow.xaml(.cs)` + Assets +
   manifest) sang `Modules\<TênMới>`, đổi tên project/namespace.
2. Csproj chỉ `ProjectReference` tới `..\..\Common\Common.csproj`. Tuyệt đối không thêm reference
   tới `MainLauncher.csproj`.
3. Không đọc `modules.json` hay bất kỳ cấu hình launcher nào từ trong code của module —
   `Environment.GetCommandLineArgs()` phải hoạt động giống nhau dù chạy từ VS, `dotnet run`, hay bị
   `MainLauncher` khởi động qua `Process.Start`.
4. Thêm một entry vào `MainLauncher/Config/modules.json`:
   ```json
   {
     "Name": "TênMới",
     "Description": "...",
     "Path": ".\\Modules\\TênMới\\TênMới.exe",
     "Arguments": "",
     "AutoStart": false
   }
   ```
   `Path` resolve tương đối với thư mục chứa `MainLauncher.exe` (`ModuleConfig.ResolveExecutablePath`).
5. Không cần sửa gì trong `MainLauncher` — nó nạp danh sách module hoàn toàn qua `modules.json`.
6. Kiểm chứng độc lập: mở `Modules\<TênMới>\<TênMới>.csproj` riêng, đặt Startup Project, F5 — phải
   chạy được mà không cần mở `MainLauncher`.

## Build / chạy / publish

- **Chạy cả hệ thống khi dev**: `.\build\Sync-Modules-Dev.ps1` — build MainLauncher + mọi Module
  (Debug) rồi copy output từng module vào `Modules\<Tên>\` cạnh `MainLauncher.exe`, đúng layout mà
  `modules.json` (đường dẫn tương đối) cần. Sau đó mở solution, đặt **MainLauncher** làm Startup
  Project, F5. Đây chỉ là tiện ích cho local dev — không bắt buộc để build, mỗi module vẫn build/run
  độc lập không cần script này.
- **Build một project riêng**: `dotnet build Modules\<Tên>\<Tên>.csproj` hoặc
  `dotnet build MainLauncher\MainLauncher.csproj -p:Platform=x64` hoặc `dotnet build Common\Common.csproj`
  hoặc `dotnet build SharedUI\SharedUI.csproj`. Mỗi project build độc lập.
- **Chạy riêng một module**: `dotnet run --project Modules\<Tên>\<Tên>.csproj`, hoặc mở `.csproj` đó
  trực tiếp trong VS và F5.
- **Đóng gói Release**: `.\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64` →
  output vào `Application\` (MainLauncher.exe + Modules\*\*.exe + Config\).
- Nếu nút Start trên Module List báo lỗi "Executable not found" khi F5 MainLauncher trong VS: chạy
  `Sync-Modules-Dev.ps1` trước — đây là xử lý lỗi mong đợi (không throw/crash), không phải bug.

## Dùng control từ SharedUI

1. Thêm `<ProjectReference Include="..\SharedUI\SharedUI.csproj" />` vào project cần dùng.
2. Merge `Themes/Generic.xaml` một lần trong `App.xaml` của project đó — **bắt buộc** vì app
   unpackaged (`WindowsPackageType=None`) không tự tìm được `Themes/Generic.xaml` mặc định:
   ```xml
   <ResourceDictionary.MergedDictionaries>
       <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
       <ResourceDictionary Source="ms-appx:///SharedUI/Themes/Generic.xaml" />
   </ResourceDictionary.MergedDictionaries>
   ```
3. Control sẵn có: `ColorSelector`, `InlineColorPicker`, `HorizontalScrollContainer`,
   `OpacityMaskView`, `CopyButton`, `Tile` (namespace `SharedUI.Controls`); `ColorTile`,
   `TypographyControl`, `ColorPageExample` (namespace `SharedUI.Controls.DesignGuidance`).
4. Control KHÔNG port (gắn chặt hạ tầng trang mẫu của WinUI Gallery — `ControlExample`,
   `SampleCodePresenter`, `PageHeader`, `HomePageHeader`, `DesignGuidance/ColorSections/*`): nếu cần,
   port riêng lẻ và bỏ phụ thuộc Gallery-specific (theo cách `ColorPageExample` đã làm — thay
   `GalleryBackgroundBrush`/`GalleryBorderBrush` bằng brush chuẩn WinUI).

## Coding convention (áp dụng toàn solution)

- MVVM với CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- DI với `Microsoft.Extensions.DependencyInjection`, đăng ký trong `App.xaml.cs`; resolve ViewModel
  trong constructor của Page/UserControl (vì `Frame.Navigate` không hỗ trợ constructor injection).
- Nullable reference types bật trên mọi project.
- Không viết logic nghiệp vụ trong code-behind của View — chỉ bind qua `x:Bind`.
- Giao tiếp launcher <-> module qua `Common.Communication.IModuleCommunicationChannel`
  (transport-agnostic, cài mẫu là Named Pipe) — không hard-code Named Pipe ở call site.
- Mỗi project có README riêng — cập nhật README tương ứng khi thay đổi kiến trúc/quy ước của project đó.
