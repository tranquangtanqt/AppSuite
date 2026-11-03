# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

# Quy ước riêng của AppSuite

## Giao tiếp
- Trả lời người dùng bằng **tiếng Việt**. Chữ trên giao diện, thông báo, F1, README, ghi chú mới trong code: tiếng Việt.
- Hỏi phạm vi bằng câu hỏi thường trong chat (không dùng hộp chọn nhiều phương án).
- **Người dùng tự commit**: không tự `git commit` / `push`. Sau mỗi thay đổi, cuối câu trả lời đưa 1 lời commit tiếng
  Việt sẵn để copy (kiểu các commit cũ: `Module: tính năng - chi tiết. Cập nhật F1, README. Unit test x/x; GUI Sandbox y/y (ngày)`).
- Không đụng `_note.txt` (ghi chú riêng của người dùng) và file lạ chưa track ở gốc repo.

## Kiến trúc (chi tiết: `README.md` gốc, README từng project, skill `appsuite-dev`)
- `MainLauncher` khởi động các module; mỗi module (`Modules\<Tên>`) là 1 exe WinUI 3 **độc lập**: không reference
  `MainLauncher`, chỉ reference `Common` / `SharedUI` (và `Common.Ocr` nếu cần OCR).
- Module mới phải đăng ký đủ: `AppSuite.sln`, `MainLauncher\Config\modules.json`, `build\Sync-Modules-Dev.ps1`,
  `build\Publish-AppSuite.ps1`, README gốc.
- .NET 8 + Windows App SDK 2.2 (component WinUI 2.2.1 + Runtime 2.2.0), Visual Studio 2022.
- Control / helper dùng chung ở `SharedUI` (quy ước ở `SharedUI\README.md`), ví dụ cửa sổ F1 `SharedUI.Help.HelpWindow`.

## Build / test
- Build 1 module: `dotnet build Modules\<Tên>\<Tên>.csproj -c Debug -p:Platform=x64`. Build cả solution đôi khi lỗi
  XAML compiler (OutOfMemory / "input does not contain any JSON tokens") do chạy song song → build lại với `-m:2`.
- Publish: `powershell -ExecutionPolicy Bypass -File .\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64 -Targets <Tên>`
  (`-OutputDir <thư mục>` để publish ra chỗ khác `Application\`).
- Unit test: `dotnet test Tests\<Tên>.Tests` (xUnit v3). Thêm / sửa logic thì thêm test; báo số test đạt trong lời commit.

## Mỗi thay đổi tính năng phải cập nhật kèm
1. Hướng dẫn F1 của module: `Views\HelpContent.cs` (hoặc file HelpContent tương ứng).
2. `README.md` của module.
3. `Tiến trình.md` ở gốc repo: việc mới → `- [ ]`, xong → `- [x]` + mô tả ngắn + kết quả test + ngày (yyyy-mm-dd).

## Giao diện (WinUI 3)
- `ContentDialog` không tự cuộn: nội dung có thể dài phải bọc `ScrollViewer` (XAML) hoặc
  `SharedUI.Helpers.DialogContent.Scrollable(...)` (dialog tạo trong code, vd thông báo lỗi `ex.Message`).
- Kiểm tra bố cục ở cửa sổ hẹp ~1000 px (Sandbox chạy 150% DPI, ~1010×600 logical).
- Muốn **thử giao diện** (đổi màu, bố cục…): làm trên bản copy trong scratchpad, chụp ảnh trước / sau trong Sandbox cho
  người dùng duyệt, rồi mới sửa code thật. Tính năng mới thì làm thẳng vào code.

## Test giao diện
- **Chỉ chạy / điều khiển app trong Windows Sandbox**, không mở cửa sổ app trên màn hình thật của người dùng.
- Không xoá dữ liệu thật của người dùng khi test (vd phiên ScreenCapture `%TEMP%\AppSuite\ScreenCapture\Session`,
  database MeRon): dùng thư mục / biến môi trường tách riêng.
