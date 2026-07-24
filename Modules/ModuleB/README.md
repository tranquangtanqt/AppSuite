# ModuleB

Ứng dụng WinUI 3 độc lập - module mẫu "B", cùng khuôn mẫu với [ModuleA](../ModuleA/README.md).
Trong `modules.json`, ModuleB có `AutoStart: false` và `Arguments: "--mode=test"` để minh hoạ
trường hợp module không tự khởi động cùng launcher và có nhận tham số dòng lệnh.

## Chạy độc lập

```powershell
dotnet run --project Modules\ModuleB\ModuleB.csproj
```

Hoặc mở `ModuleB.csproj` trực tiếp trong Visual Studio và F5. Xem README của
[ModuleA](../ModuleA/README.md) để biết chi tiết về nguyên tắc độc lập (không reference
MainLauncher, chỉ reference `Common`).
