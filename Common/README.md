# Common

Thư viện dùng chung giữa `MainLauncher` và mọi `Modules\*`. **Không** `UseWinUI`, không phụ thuộc
WinAppSDK - đây là điều kiện để mọi module vẫn build/run được kể cả khi chỉ cần đọc contract từ đây,
không cần load runtime WinUI.

## Nội dung

| Thư mục | Nội dung |
|---|---|
| `Interfaces/` | `IModuleInfo` (contract mô tả 1 module), `IModuleService` (nạp danh sách module), `IProcessManager` (start/stop/restart/kiểm tra process) |
| `Models/` | `ModuleConfig` (deserialize từ `modules.json`), `ModuleStatus` (enum), `ModuleProcessInfo`, `ModuleProcessStatusChangedEventArgs`, `LogEntry` |
| `Utilities/` | `JsonConfigLoader` - đọc/ghi `modules.json` bằng `System.Text.Json` |
| `Communication/` | `IModuleCommunicationChannel` (transport-agnostic) + `NamedPipeServerChannel`/`NamedPipeClientChannel` (cài đặt mẫu bằng Named Pipe) |
| `Logging/` | `RollingFileLoggerProvider` (ghi log ra file, xoay vòng theo ngày), `InMemoryLoggerProvider` (bắn event `EntryLogged` cho UI Logs page) |

## Vì sao tách riêng interface giao tiếp?

`IModuleCommunicationChannel` không gắn với Named Pipe cụ thể nào ở tầng gọi - `MainLauncher` và
module chỉ thấy `StartAsync`/`SendAsync`/`MessageReceived`. Muốn đổi sang REST local API hay gRPC,
chỉ cần viết class mới implement interface này và đăng ký thay thế trong DI container của
`MainLauncher` (`App.xaml.cs`) - không phải sửa `ProcessManager`, `ModuleManager` hay bất kỳ
ViewModel nào.

## Build

```powershell
dotnet build Common\Common.csproj
```

Không có project nào phụ thuộc Common bị buộc phải build cùng - Common build độc lập, chỉ là một
class library thuần .NET 8.
