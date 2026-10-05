using SharedUI.Help;

namespace ModuleB.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Viết theo hành vi hiện tại của code (lập chỉ mục
/// Excel vào SQLite, hộp thoại các ô khớp) - README.md / PLAN.md phần đầu mô tả bản cũ (đọc cả .docx, không chỉ mục).
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Tìm file Excel trong 1 thư mục tài liệu theo nhóm (thư mục con) và theo chữ có trong nội dung file - không phải mở từng file.",
        [
            new("1. Chọn thư mục tài liệu", "Nút \"Cai dat thu muc\" (dưới cây thư mục): chọn thư mục ở ô Document Folder (nút ...) rồi Luu, hoặc chọn 1 thư mục trong danh sách đã lưu, bấm OK (hoặc bấm đúp)."),
            new("2. Chờ lập chỉ mục", "Lần đầu, mọi file .xlsx / .xlsm trong thư mục (cả thư mục con) được đọc và lưu nội dung để các lần tìm sau nhanh. Vòng xoay cạnh chữ Explorer = đang lập chỉ mục; nút Tìm kiếm bật lại khi xong."),
            new("3. Nhập điều kiện rồi bấm Tìm kiếm", "Ô Nhom / thu muc và / hoặc ô Nội dung tìm kiếm. Kết quả: danh sách Thư mục (nhóm) | Tên file."),
            new("4. Xem chỗ khớp", "Bấm 1 file trong kết quả: hộp thoại liệt kê các ô chứa từ khoá (sheet, ô, nội dung). \"Mo tai day\" mở file trong Excel và nhảy đến đúng ô đó."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Tìm kiếm", "", "2 ô điều kiện, cú pháp AND / OR, không phân biệt hoa / thường.",
        [
            new("Nhom / thu muc", "Lọc theo thư mục con cấp 1 ngay dưới thư mục tài liệu (nhóm), vd 10.PD. Bấm 1 thư mục trong cây Explorer bên trái = điền nhóm của thư mục đó và tìm luôn."),
            new("Nội dung tìm kiếm", "Chữ có trong ô Excel hoặc trong hình vẽ / text box của sheet, vd GetBunData. Để trống = mọi file của nhóm."),
            new("AND / OR", "A AND B: chứa cả A và B. A OR B: chứa A hoặc B. A AND B OR C = (A và B) hoặc C. AND / OR cách 2 bên bằng dấu cách, viết hoa hay thường đều được - nên chữ \"and\" / \"or\" đứng riêng trong từ khoá luôn bị hiểu là toán tử. So khớp kiểu \"chứa chuỗi\"."),
            new("Xóa", "Xoá 2 ô điều kiện và kết quả hiện tại."),
        ]),

        new("Kết quả và mở file", "", "Từ danh sách kết quả tới đúng ô trong Excel.",
        [
            new("Các ô khớp", "Bấm 1 file: liệt kê sheet, địa chỉ ô và nội dung của mọi ô chứa từ khoá. Chữ trong hình vẽ được gán cho ô gần góc trên-trái của hình."),
            new("Mo tai day", "Mở file trong Excel và chọn sẵn đúng sheet / ô. Cần Excel cài trên máy."),
            new("Open File", "Chuột phải 1 file trong kết quả → Open File: mở file bằng ứng dụng mặc định."),
        ]),

        new("Thư mục đã lưu và chỉ mục", "", "Hộp thoại Cai dat thu muc.",
        [
            new("Danh sách thư mục đã lưu", "Các thư mục tài liệu đã dùng được nhớ để chọn lại nhanh. Nút bút chì = sửa đường dẫn, nút thùng rác = xoá khỏi danh sách (đang mở đúng thư mục đó thì cây Explorer và kết quả được xoá theo)."),
            new("Cập nhật chỉ mục", "Mỗi lần chọn thư mục, chỉ file mới / đã sửa (theo ngày sửa) được đọc lại - file không đổi dùng nội dung đã lưu nên rất nhanh. File mới thêm vào thư mục: chọn lại thư mục để cập nhật."),
            new("Loại file", "Chỉ đọc nội dung .xlsx / .xlsm. File Excel đang mở / bị khoá hoặc lỗi không làm hỏng lượt tìm."),
            new("Dữ liệu lưu ở đâu", "Data\\DataFromExcel.db cạnh ModuleB.exe (thư mục đã lưu + nội dung đã lập chỉ mục)."),
        ]),
    ];
}
