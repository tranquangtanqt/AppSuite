using SharedUI.Help;

namespace CsvEditor.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút ? / F1) - mọi tính năng của CsvEditor cho người dùng cuối. Nguồn: README.md mục
/// "Toolbar"; thêm / đổi tính năng thì cập nhật cả 2 chỗ. Nằm ở Views (không ở Models) vì Models được biên dịch kèm
/// vào CsvEditor.Tests - project test không reference SharedUI.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Mở file CSV / TSV → sửa như bảng tính → lưu lại đúng định dạng cũ.",
        [
            new("1. Mở file", "Bấm Open, kéo-thả file vào cửa sổ, hoặc chọn trong danh sách file gần đây (nút 🕘). Delimiter và encoding tự nhận diện.", "Ctrl+O"),
            new("2. Sửa", "Bấm đúp vào ô (hoặc chọn ô rồi gõ / F2) để sửa, Enter để xong. Thêm / xoá dòng, cột bằng các nút trên thanh công cụ hoặc chuột phải."),
            new("3. Lưu", "Save ghi lại đúng delimiter / encoding đang dùng; Save As để lưu thành file khác. Cần đưa sang Excel thì dùng Export .xlsx.", "Ctrl+S"),
            new("Mở lại hướng dẫn này", "Nút ? ở cuối thanh công cụ, hoặc phím F1.", "F1"),
        ]),

        new("Mở & lưu file", "", "Mở CSV / TSV / TXT, file không có dòng tiêu đề, file lớn; lưu và xuất Excel.",
        [
            new("Open", "Chọn file .csv / .tsv / .txt. Tự nhận diện delimiter (dấu phẩy, Tab, chấm phẩy, gạch đứng) và encoding (UTF-8, UTF-8 BOM, UTF-16).", "Ctrl+O"),
            new("Kéo-thả file", "Thả file .csv / .tsv / .txt vào bất kỳ chỗ nào trong cửa sổ để mở. Thả nhiều file thì mở file đầu tiên."),
            new("File gần đây", "Nút 🕘 cạnh Open: 10 file mở / Save As gần nhất, mới nhất trước; di chuột lên để xem đường dẫn đầy đủ. File đã bị xoá / chuyển chỗ thì báo và tự bỏ khỏi danh sách. Xoá danh sách ở cuối menu."),
            new("Nhận diện sai encoding / delimiter", "Bấm vào nhãn encoding hoặc delimiter ở thanh trạng thái (dưới cùng) để mở lại file với lựa chọn thủ công."),
            new("Dòng đầu là tiêu đề", "Checkbox cạnh Open (mặc định có tick). Bỏ tick cho file không có dòng tên cột: mọi dòng là dữ liệu, cột tự đặt tên Cột 1, Cột 2… (chỉ để hiển thị - Save không ghi dòng tên cột). Đổi khi đang mở file thì file được mở lại."),
            new("Dòng dài hơn tiêu đề", "Dòng có nhiều field hơn dòng tiêu đề: tự thêm cột Cột N cho phần dư (kèm 1 cảnh báo) để không mất dữ liệu khi lưu."),
            new("Save / Save As", "Save ghi đè file đang mở, giữ nguyên delimiter / encoding / BOM. Chỉ bọc ngoặc kép những ô cần (có delimiter, ngoặc kép, xuống dòng).", "Ctrl+S"),
            new("Hỏi lưu thay đổi", "Còn thay đổi chưa lưu mà đóng cửa sổ, mở file khác hoặc mở lại file → hỏi Lưu / Không lưu / Hủy. Lưu lỗi (file đang bị app khác khoá…) thì báo, không mất thay đổi."),
            new("Export .xlsx", "Xuất các dòng đang hiện (theo lọc / sắp xếp hiện tại) ra file Excel; file CSV đang mở không đổi. Số thật ghi thành số; mã có số 0 ở đầu (00123), số dài hơn 15 chữ số, ô bắt đầu bằng = giữ nguyên dạng chữ. Dòng tiêu đề in đậm, cố định khi cuộn, có nút lọc."),
            new("File rất lớn", "File được đọc ở nền, có thanh tiến độ và nút Hủy. Ước tính trên ~3 triệu dòng thì hỏi trước khi mở (toàn bộ file nằm trong RAM)."),
        ]),

        new("Sửa dữ liệu", "", "Sửa ô, thêm / xoá dòng và cột, điền nhanh, copy - dán với Excel.",
        [
            new("Sửa ô", "Bấm đúp vào ô, hoặc chọn ô rồi gõ / F2. Enter để xong, Esc để huỷ sửa.", "F2", "Enter", "Esc"),
            new("Add Row / Copy Row", "Chèn dòng mới (hoặc bản sao dòng đang chọn) ngay sau dòng đang chọn; không chọn gì thì thêm ở cuối bảng."),
            new("Delete Row", "Xoá mọi dòng đang chọn (giữ Ctrl / Shift khi bấm để chọn nhiều dòng)."),
            new("Add / Delete / Rename Column", "Thêm cột ở cuối; xoá hoặc đổi tên cột đang chọn (cột của ô đang chọn)."),
            new("Kéo điền (fill handle)", "Chọn 1 ô, kéo ô vuông nhỏ ở góc dưới-phải xuống các dòng dưới để chép giá trị. Giữ Ctrl khi thả chuột để tự tăng số (+1 mỗi dòng).", "Kéo", "Ctrl + kéo"),
            new("Chuột phải trên bảng", "Copy, Paste, Delete, Insert Row, Duplicate Row."),
            new("Copy / Paste với Excel", "Copy các dòng đang chọn dạng Tab - dán thẳng vào Excel đúng từng ô (ô có xuống dòng / tab được bọc ngoặc kép như Excel). Paste dán khối ô copy từ Excel vào từ ô đang chọn."),
            new("Undo / Redo", "Hoàn tác từng bước: sửa ô, thêm / xoá dòng, cột, dán, kéo điền, Replace All (mỗi thao tác là 1 bước dù đổi nhiều ô). Lọc / sắp xếp không phải thao tác sửa nên không vào Undo.", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"),
        ]),

        new("Tìm, lọc & sắp xếp", "", "Tìm / thay thế, đi tới dòng, lọc nhanh theo cột, lọc nâng cao, sắp xếp nhiều cột.",
        [
            new("Find", "Tìm với 5 kiểu: Contains, Equals, StartsWith, EndsWith, Regex; có phân biệt hoa thường. Trước / Sau để nhảy giữa các kết quả (tự cuộn tới đúng ô).", "Ctrl+F"),
            new("Replace", "Cùng hộp thoại với Find: thay thế tất cả kết quả đang tìm được - 1 bước Undo.", "Ctrl+H"),
            new("Go To", "Đi tới dòng số N (theo số ở đầu dòng của bảng đang hiện), giữ cột đang chọn. Gõ số rồi Enter.", "Ctrl+G"),
            new("Lọc nhanh theo cột", "Gõ vào ô Filter dưới tên cột: chỉ hiện dòng có chứa chữ đó (không phân biệt hoa thường). Gõ nhiều cột = phải khớp tất cả. Nút ✕ ở góc trên-trái bảng xoá mọi ô Filter."),
            new("Filter (nâng cao)", "Nhiều điều kiện == != > < >= <= Contains Regex, mỗi điều kiện có thể NOT, kết hợp bằng AND hoặc OR. Clear Filter bỏ lọc ngay không cần mở hộp thoại."),
            new("Sort", "Sắp xếp nhiều cột theo thứ tự ưu tiên. Ô là số (2.5 hoặc 2,5) thì so theo số, còn lại so chữ. Clear Sort trả về thứ tự gốc. Sắp xếp chỉ là cách hiển thị - Save vẫn giữ thứ tự gốc của file."),
        ]),

        new("Thống kê & cảnh báo", "", "Số liệu từng cột và các vấn đề phát hiện trong file.",
        [
            new("Statistics", "Số dòng, số cột, dòng trùng; theo từng cột: số ô rỗng, giá trị khác nhau, Min / Max / Trung bình / Tổng (với cột số)."),
            new("Cảnh báo", "Khung Cảnh báo (N) dưới thanh công cụ: dòng lệch số cột, ngoặc kép không đóng, cột nhiều ô rỗng, độ tin cậy thấp khi nhận diện delimiter / encoding. Bấm đúp 1 cảnh báo để nhảy tới dòng đó. Lệch cột / cột rỗng được tính lại sau khi sửa và khi lưu."),
            new("Thanh trạng thái", "Tên file, encoding, delimiter (bấm để đổi), số dòng / cột, trạng thái đã lưu / chưa lưu."),
        ]),

        new("Phím tắt", "", "Tổng hợp phím tắt của CsvEditor.",
        [
            new("Mở file", "", "Ctrl+O"),
            new("Lưu", "", "Ctrl+S"),
            new("Hoàn tác / Làm lại", "", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"),
            new("Tìm / Thay thế", "", "Ctrl+F", "Ctrl+H"),
            new("Đi tới dòng", "", "Ctrl+G"),
            new("Sửa ô / xong / huỷ", "", "F2", "Enter", "Esc"),
            new("Hướng dẫn", "", "F1"),
        ]),
    ];
}
