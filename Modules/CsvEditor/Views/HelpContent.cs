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
            new("1. Mở file", "Lúc chưa mở file, cửa sổ hiện màn hình bắt đầu: kéo-thả file vào, bấm Mở file..., hoặc bấm 1 file trong danh sách File gần đây. Dấu phân cách và bảng mã tự nhận diện.", "Ctrl+O"),
            new("2. Sửa", "Bấm đúp vào ô (hoặc chọn ô rồi gõ / F2) để sửa, Enter để xong. Thêm / xoá dòng, cột bằng nút Dòng ▾ / Cột ▾ trên thanh công cụ hoặc chuột phải."),
            new("3. Lưu", "Lưu ghi lại đúng dấu phân cách / bảng mã đang dùng. Xuất ▾ có Lưu thành file khác và Xuất Excel (.xlsx).", "Ctrl+S"),
            new("Mở lại hướng dẫn này", "Nút ? ở cuối thanh công cụ, hoặc phím F1.", "F1"),
        ]),

        new("Mở & lưu file", "", "Mở CSV / TSV / TXT, file không có dòng tiêu đề, file lớn; lưu và xuất Excel.",
        [
            new("Mở", "Chọn file .csv / .tsv / .txt. Tự nhận diện dấu phân cách (dấu phẩy, Tab, chấm phẩy, gạch đứng) và bảng mã (UTF-8, UTF-8 BOM, UTF-16, Shift-JIS của file tiếng Nhật - không cần chọn tay).", "Ctrl+O"),
            new("Màn hình bắt đầu", "Hiện khi chưa mở file: vùng kéo-thả, nút Mở file..., nút Bảng trống (đặt tên cột đầu tiên rồi nhập dữ liệu, Lưu để chọn chỗ lưu) và danh sách file gần đây (bấm 1 dòng để mở)."),
            new("Kéo-thả file", "Thả file .csv / .tsv / .txt vào bất kỳ chỗ nào trong cửa sổ để mở. Thả nhiều file thì mở file đầu tiên."),
            new("File gần đây", "Nút 🕘 cạnh Mở (và danh sách ở màn hình bắt đầu): 10 file mở / lưu thành file khác gần nhất, mới nhất trước; di chuột lên để xem đường dẫn đầy đủ. File đã bị xoá / chuyển chỗ thì báo và tự bỏ khỏi danh sách. Xoá danh sách ở cuối menu 🕘."),
            new("Nhận diện sai bảng mã / dấu phân cách (encoding / delimiter)", "Bấm nút bảng mã · dấu phân cách ở thanh trạng thái (dưới cùng) để chọn lại rồi mở lại file. Tab tới nút rồi Enter cũng được."),
            new("Dòng đầu là tiêu đề", "Ô tick ở thanh trạng thái, cạnh nút bảng mã (mặc định có tick). Bỏ tick cho file không có dòng tên cột: mọi dòng là dữ liệu, cột tự đặt tên Cột 1, Cột 2… (chỉ để hiển thị - Lưu không ghi dòng tên cột). Đổi khi đang mở file thì file được mở lại."),
            new("Dòng dài hơn tiêu đề", "Dòng có nhiều field hơn dòng tiêu đề: tự thêm cột Cột N cho phần dư (kèm 1 cảnh báo) để không mất dữ liệu khi lưu."),
            new("Lưu / Lưu thành file khác", "Lưu ghi đè file đang mở, giữ nguyên dấu phân cách / bảng mã / BOM. Chỉ bọc ngoặc kép những ô cần (có dấu phân cách, ngoặc kép, xuống dòng). Lưu thành file khác nằm trong Xuất ▾. Tiêu đề cửa sổ là tên file, có ● khi còn thay đổi chưa lưu.", "Ctrl+S"),
            new("Hỏi lưu thay đổi", "Còn thay đổi chưa lưu mà đóng cửa sổ, mở file khác hoặc mở lại file → hỏi Lưu / Không lưu / Huỷ. Lưu lỗi (file đang bị app khác khoá…) thì báo, không mất thay đổi."),
            new("Xuất Excel (.xlsx)", "Trong Xuất ▾. Xuất các dòng đang hiện (theo lọc / sắp xếp hiện tại) ra file Excel; file CSV đang mở không đổi. Số thật ghi thành số; mã có số 0 ở đầu (00123), số dài hơn 15 chữ số, ô bắt đầu bằng = giữ nguyên dạng chữ. Dòng tiêu đề in đậm, cố định khi cuộn, có nút lọc."),
            new("File rất lớn", "File được đọc ở nền, có thanh tiến độ và nút Huỷ. Ước tính trên ~3 triệu dòng thì hỏi trước khi mở (toàn bộ file nằm trong RAM)."),
        ]),

        new("Sửa dữ liệu", "", "Sửa ô, thêm / xoá dòng và cột, điền nhanh, copy - dán với Excel.",
        [
            new("Xuống dòng trong ô", "Khi sửa ô (F2 hoặc bấm đúp), Shift+Enter xuống dòng ở mọi ô. Ô nhiều dòng tự cao lên cho đủ chữ. Enter = ghi, Esc = bỏ. Ô đã có xuống dòng giữ nguyên kiểu xuống dòng của file."),
            new("Sửa ô", "Bấm đúp vào ô, hoặc chọn ô rồi gõ / F2. Enter để xong, Esc để huỷ sửa.", "F2", "Enter", "Esc"),
            new("Dòng ▾", "Thêm dòng / Nhân bản dòng đang chọn: chèn ngay sau dòng đang chọn (không chọn gì thì thêm ở cuối bảng). Xoá dòng đang chọn: xoá mọi dòng đang chọn (giữ Ctrl / Shift khi bấm để chọn nhiều dòng)."),
            new("Cột ▾", "Thêm cột ở cuối; đổi tên hoặc xoá cột của ô đang chọn (chưa chọn ô nào thì thanh trạng thái nhắc). Chuột phải tiêu đề cột cũng có các việc này."),
            new("Ẩn / hiện cột", "Chuột phải tiêu đề cột → Ẩn cột này, hoặc Cột ▾ → Ẩn / hiện cột... (danh sách tick, ô tìm cột, Hiện tất cả / Bỏ tick tất cả). Chỉ ẩn khi xem - lưu file, xuất Excel, khung Xem dọc vẫn đủ mọi cột. Thẻ \"Đang ẩn N cột\" phía trên bảng: bấm để chọn lại, ✕ hiện tất cả. Đi tới 1 cột đang ẩn thì cột đó tự hiện. Mở file khác thì hiện lại hết."),
            new("Ẩn hàng ô lọc", "Cột ▾ → bỏ tick Hiện ô lọc dưới tên cột: tiêu đề cột thấp lại, bảng thấy thêm dòng. Chữ lọc đang gõ vẫn có tác dụng, thẻ Ô lọc ✕ vẫn bỏ được. Bật lại ở cùng chỗ đó."),
            new("Cố định cột", "Chuột phải tiêu đề cột → Cố định cột 1 – N (đến cột này), hoặc Cột ▾ → Cố định đến cột đang chọn: các cột từ đầu tới cột đó đứng yên khi cuộn ngang (vd cột khoá DATA_LNO), có vạch đậm ngăn với phần cuộn. Thẻ \"Cố định N cột đầu\" ✕ hoặc Bỏ cố định cột để thôi. Mở file khác thì bỏ."),
            new("Xem dọc 1 dòng", "Nút ▤ cuối thanh công cụ (hoặc chuột phải ô → Xem dọc dòng này): khung bên phải liệt kê dòng đang chọn thành \"số. tên cột | giá trị\" - đọc bản ghi nhiều cột không phải cuộn ngang. Ô Tìm lọc theo tên cột hoặc giá trị. Sửa giá trị ngay trong khung: Enter ghi + sang ô dưới, Shift+Enter xuống dòng trong ô, Esc trả lại giá trị cũ (mỗi lần ghi là 1 bước Hoàn tác). Nút ˄ ˅ sang dòng trên / dưới; chọn dòng khác trên bảng thì khung đổi theo.", "Ctrl+R"),
            new("Kéo điền (fill handle)", "Chọn 1 ô, kéo ô vuông nhỏ ở góc dưới-phải xuống các dòng dưới để chép giá trị. Giữ Ctrl khi thả chuột để tự tăng số (+1 mỗi dòng).", "Kéo", "Ctrl + kéo"),
            new("Chuột phải trên ô", "Sao chép, Dán, Lọc theo giá trị của ô (Lọc: cột = \"giá trị\"), Lọc bỏ giá trị đó (≠), Xem dọc dòng này, Chèn dòng bên dưới, Nhân bản dòng, Xoá dòng đang chọn. Ô bấm chuột phải được chọn luôn."),
            new("Chuột phải trên tiêu đề cột", "Sắp xếp tăng dần / giảm dần theo cột đó, Bỏ ô lọc của cột, Đổi tên cột, Chèn cột bên phải, Xoá cột."),
            new("Sao chép / Dán với Excel", "Sao chép các dòng đang chọn dạng Tab - dán thẳng vào Excel đúng từng ô (ô có xuống dòng / tab được bọc ngoặc kép như Excel). Dán: dán khối ô copy từ Excel vào từ ô đang chọn."),
            new("Undo / Redo", "Hoàn tác từng bước: sửa ô, thêm / xoá dòng, cột, dán, kéo điền, Replace All (mỗi thao tác là 1 bước dù đổi nhiều ô). Lọc / sắp xếp không phải thao tác sửa nên không vào Undo.", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"),
        ]),

        new("Tìm, lọc & sắp xếp", "", "Tìm / thay thế, đi tới dòng, lọc nhanh theo cột, lọc nâng cao, sắp xếp nhiều cột.",
        [
            new("Tìm", "Thanh tìm hiện phía trên bảng, không che bảng: vừa tìm vừa xem và sửa ô được, kết quả tự tính lại khi sửa / lọc / sắp xếp. Chỉ tìm ở dòng và cột đang hiện (cột đang ẩn không tìm, không thay thế). Tìm với 5 kiểu: Chứa, Khớp toàn bộ ô, Bắt đầu bằng, Kết thúc bằng, Regex; nút Aa = phân biệt hoa thường. Enter = kết quả sau, Shift+Enter = kết quả trước (tự cuộn tới đúng ô), Esc = đóng thanh.", "Ctrl+F"),
            new("Thay thế", "Ctrl+H hoặc nút ❯ đầu thanh tìm hiện ô Thay bằng. Thay thế tất cả chỉ thay phần chữ khớp trong mọi ô đang tìm thấy, như Excel (tìm an thay em: An Bình thành em Bình); kiểu Khớp toàn bộ ô thì thay cả ô; Regex dùng được $1, $2... cho nhóm. 1 bước Hoàn tác.", "Ctrl+H"),
            new("Đi tới dòng", "Nút → cạnh Tìm: đi tới dòng số N (theo số ở đầu dòng của bảng đang hiện), giữ cột đang chọn. Gõ số rồi Enter.", "Ctrl+G"),
            new("Đi tới cột", "Cột ▾ → Đi tới cột..., hoặc Ctrl+Shift+G: gõ 1 phần tên cột (vd EXT_DATA) - danh sách gợi ý các cột khớp (số thứ tự. tên), chọn bằng chuột hoặc ↓ rồi Enter; Enter ngay thì tới cột khớp nhất (trùng tên → bắt đầu bằng → chứa). Gõ số (vd 77) = cột thứ 77. Bảng cuộn ngang tới cột đó, giữ dòng đang chọn.", "Ctrl+Shift+G"),
            new("Lọc nhanh theo cột", "Gõ vào ô Lọc... dưới tên cột: chỉ hiện dòng có chứa chữ đó (không phân biệt hoa thường). Gõ nhiều cột = phải khớp tất cả. Thẻ \"Ô lọc: ...\" phía trên bảng ✕ xoá mọi ô lọc."),
            new("Lọc (nâng cao)", "Nhiều điều kiện = != > < >= <= Chứa Regex, mỗi điều kiện có thể tick Phủ định (lấy dòng không thoả), kết hợp theo thoả mãn tất cả hoặc thoả mãn 1 trong các điều kiện. Chuột phải 1 ô → Lọc theo giá trị này thêm điều kiện cột = giá trị (thêm vào lọc đang có theo kiểu thoả mãn tất cả)."),
            new("Thẻ đang lọc / sắp xếp", "Phía trên bảng: \"Hiện x / y dòng\" và 1 thẻ cho mỗi thứ đang áp dụng (Lọc, Ô lọc, Sắp xếp, Đang ẩn cột, Cố định cột). Bấm chữ trên thẻ để sửa, bấm ✕ để bỏ ngay. Nhiều thẻ thì tự xuống hàng. Lọc ra 0 dòng thì giữa bảng hiện lời nhắc kèm nút Bỏ lọc (bỏ cả lọc nâng cao lẫn ô lọc)."),
            new("Sắp xếp", "Sắp xếp nhiều cột theo thứ tự ưu tiên; nhanh hơn: chuột phải tiêu đề cột → tăng / giảm dần. Ô là số (2.5 hoặc 2,5) thì so theo số, còn lại so chữ. ✕ trên thẻ Sắp xếp trả về thứ tự gốc. Sắp xếp chỉ là cách hiển thị - Lưu vẫn giữ thứ tự gốc của file."),
        ]),

        new("Thống kê & cảnh báo", "", "Số liệu từng cột và các vấn đề phát hiện trong file.",
        [
            new("Thống kê", "Nút 📊 cạnh Sắp xếp. Số dòng, số cột, dòng trùng; theo từng cột: số ô rỗng, giá trị khác nhau, Nhỏ nhất / Lớn nhất / Trung bình / Tổng (với cột số)."),
            new("Cảnh báo", "Khung Cảnh báo (N) dưới thanh công cụ: dòng lệch số cột, ngoặc kép không đóng, cột nhiều ô rỗng, độ tin cậy thấp khi nhận diện dấu phân cách / bảng mã. Bấm đúp 1 cảnh báo để nhảy tới dòng đó. Lệch cột / cột rỗng được tính lại sau khi sửa và khi lưu."),
            new("Cửa sổ hẹp", "Cửa sổ hẹp thì các nút Xuất, Dòng, Cột, Tìm, Lọc, Sắp xếp chỉ còn icon để mọi nút vẫn thấy; rê chuột để xem tên. Mở rộng cửa sổ thì chữ hiện lại."),
            new("Thanh trạng thái", "Tên file, nút bảng mã · dấu phân cách (bấm để đổi, chỉ bấm được khi đã mở file), ô tick Dòng đầu là tiêu đề, số dòng / cột, trạng thái đã lưu / chưa lưu, thông báo. Tên file và thông báo dài tự rút gọn \"...\" - rê chuột để xem đủ."),
        ]),

        new("Phím tắt", "", "Tổng hợp phím tắt của CsvEditor.",
        [
            new("Mở file", "", "Ctrl+O"),
            new("Lưu", "", "Ctrl+S"),
            new("Hoàn tác / Làm lại", "", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"),
            new("Tìm / Thay thế", "", "Ctrl+F", "Ctrl+H"),
            new("Đi tới dòng", "", "Ctrl+G"),
            new("Đi tới cột theo tên", "", "Ctrl+Shift+G"),
            new("Xem dọc dòng đang chọn (mở / đóng)", "", "Ctrl+R"),
            new("Sửa ô / xong / huỷ", "", "F2", "Enter", "Esc"),
            new("Hướng dẫn", "", "F1"),
        ]),
    ];
}
