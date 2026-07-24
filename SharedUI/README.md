# SharedUI

Thư viện WinUI 3 Class Library chứa các control tái sử dụng, chuyển thể từ
[WinUI Gallery](https://github.com/microsoft/WinUI-Gallery) (`Controls/` folder) tại
`D:\Project\Tantq\WinUI\WinUI-Gallery\WinUIGallery`. `MainLauncher` và bất kỳ Module nào muốn dùng
chỉ cần `ProjectReference` tới đây — việc này **không vi phạm** nguyên tắc độc lập giữa Module và
MainLauncher, vì luật chỉ cấm Module reference `MainLauncher`, không cấm reference project dùng
chung khác (giống cách mọi project đã reference `Common`).

## Cách dùng

1. Thêm `<ProjectReference Include="..\SharedUI\SharedUI.csproj" />` vào project của bạn.
2. Merge `Themes/Generic.xaml` **một lần** trong `App.xaml` — bắt buộc, vì cơ chế tự tìm
   `Themes/Generic.xaml` mặc định của WinUI không đáng tin cậy với app unpackaged
   (`WindowsPackageType=None`, mọi project trong solution này đều dùng kiểu này):

   ```xml
   <ResourceDictionary.MergedDictionaries>
       <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
       <ResourceDictionary Source="ms-appx:///SharedUI/Themes/Generic.xaml" />
   </ResourceDictionary.MergedDictionaries>
   ```

   (MainLauncher đã merge sẵn; nếu một Module dùng `CopyButton` hoặc `OpacityMaskView`, module đó
   cũng phải tự merge dòng này trong App.xaml riêng của nó.)

## Danh sách control đã port

| Control | Namespace | Ghi chú |
|---|---|---|
| `ColorSelector` | `SharedUI.Controls` | SplitButton mở flyout ColorPicker, xem trước màu đã chọn |
| `InlineColorPicker` | `SharedUI.Controls` | Header + ô màu + textbox hex, có flyout ColorPicker |
| `HorizontalScrollContainer` | `SharedUI.Controls` | Container cuộn ngang với nút trái/phải tự ẩn/hiện |
| `OpacityMaskView` | `SharedUI.Controls` | ContentControl áp opacity mask (custom control, style ở `Themes/Generic.xaml`) |
| `CopyButton` | `SharedUI.Controls` | Button có animation dấu tích khi copy, custom control (style ở `Themes/Generic.xaml`) |
| `Tile` | `SharedUI.Controls` | Thẻ bấm được (icon/content + title + description), điều hướng theo `Link` |
| `ColorTile` | `SharedUI.Controls.DesignGuidance` | Hiển thị 1 brush màu kèm tên, mô tả, nút copy tên brush |
| `TypographyControl` | `SharedUI.Controls.DesignGuidance` | 1 dòng trong bảng typography ramp, kèm nút copy tên resource |
| `ColorPageExample` | `SharedUI.Controls.DesignGuidance` | Khung thẻ tiêu đề/mô tả/nội dung ví dụ |

`Helpers/UIHelper.AnnounceActionForAccessibility` và `Converters/BrushToColorConverter` là phần phụ
trợ nhỏ được port kèm để các control trên hoạt động độc lập, không cần thêm gì khác.

## Những control KHÔNG port và lý do

WinUI Gallery's `Controls/` còn có `ControlExample`, `SampleCodePresenter`, `PageHeader`,
`HomePageHeader`, và toàn bộ `DesignGuidance/ColorSections/*`. Những control này bị bỏ qua vì gắn
chặt với hạ tầng "trang tài liệu mẫu" riêng của Gallery, không có ý nghĩa trong AppSuite:

- **`ControlExample` + `SampleCodePresenter`**: hiển thị "control demo trực tiếp + tab xem mã
  XAML/C#", cần package `ColorCode.WinUI` (syntax highlighting), quy ước nạp file `.txt` mẫu, và
  `NativeMethods.IsAppPackaged`. AppSuite không có khái niệm "xem mã nguồn của control".
- **`PageHeader`**: header cho từng trang mẫu — cần model `ControlInfoDataItem`, cơ chế Favorites
  (`SettingsHelper.Current.Favorites`), sinh link GitHub tới mã nguồn (`ProtocolActivationClipboardHelper`),
  converter riêng của Gallery.
- **`HomePageHeader`**: chỉ hiển thị banner "phiên bản Windows App SDK đang dùng" của Gallery — nội
  dung đặc thù, không đáng port riêng cho AppSuite.
- **`DesignGuidance/ColorSections/*`** (`BackgroundSection`, `FillSection`, `HighContrastSection`,
  `SignalSection`, `StrokeSection`, `TextSection`): thực chất là các **Page** điều hướng
  (`App.MainWindow.Navigate(typeof(ItemPage), ...)`) trong hệ thống sample-navigation của Gallery,
  không phải control tái sử dụng.

Nếu sau này cần một control cụ thể trong nhóm này, port riêng lẻ và bỏ phần phụ thuộc vào hạ tầng
Gallery (tương tự cách `ColorPageExample` đã được đơn giản hoá: thay `GalleryBackgroundBrush`/
`GalleryBorderBrush` bằng brush chuẩn `SolidBackgroundFillColorBaseBrush`/`CardStrokeColorDefaultBrush`).

## Build

```powershell
dotnet build SharedUI\SharedUI.csproj
```

Build độc lập, không phụ thuộc `Common` hay bất kỳ project nào khác trong solution.
