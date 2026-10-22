using SharedUI.Help;

namespace Rdbms.HtmlGenerator.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì cập nhật
/// cả 2 chỗ.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Đọc cấu trúc bảng / cột / khoá từ PostgreSQL, Oracle, MySQL hoặc SQL Server, xuất thành 1 trang HTML tra cứu mở trong trình duyệt.",
        [
            new("1. Chọn Nguồn", "Combobox \"Nguồn\": PostgreSQL / Oracle / MySQL / SQL Server. Nguồn đã chọn được nhớ cho lần mở sau. Nhiều môi trường (dev / test / prod): dùng combobox \"Hồ sơ\" ở trên cùng - xem mục Hồ sơ kết nối."),
            new("2. Thiết lập kết nối", "Nút \"Thiết lập kết nối\" (mở sẵn tab của nguồn đang chọn): nhập thông tin, bấm Lưu (lưu cả 4 tab, tự điền lại lần mở sau). Nút \"1. Đọc database\" chỉ bật khi nguồn đang chọn đã có Host + Database (Oracle: Host + Service Name hoặc SID)."),
            new("3. Bấm \"1. Đọc database\"", "Kết nối, đọc schema, lưu vào Data\\Database\\{tên database}.db. Khung Nhật ký bên dưới hiện tiến trình. Không kết nối được (sai Host / Port, chưa bật VPN, firewall chặn...) thì sau thời gian chờ (mặc định 10 giây, đổi trong Cài đặt) hiện hộp thoại báo lỗi kèm gợi ý cần kiểm tra; sai mật khẩu / sai tên database / Service Name cũng được báo rõ."),
            new("4. Bấm \"2. Xuất HTML\" rồi \"3. Mở file HTML\"", "Sinh và mở Data\\Database\\{tên database}.html - 1 file tự chứa, không cần mạng, gửi cho người khác được."),
            new("Nút màu xanh = bước tiếp theo", "Chưa đọc database: nút 1 xanh. Đọc xong: nút \"2. Xuất HTML\" xanh. Xuất xong: nút \"3. Mở file HTML\" xanh. Mở lại module cũng tự nhận đang ở bước nào (HTML cũ hơn dữ liệu vừa đọc thì vẫn là bước 2)."),
            new("Dòng dưới Nguồn", "Cho biết nguồn đang chọn kết nối tới đâu (Host:Port/Database, Oracle: Service Name / SID) và dữ liệu đã đọc lúc nào, hoặc \"chưa đọc lần nào\". Nguồn chưa nhập Host / Database thì nhắc bấm \"Thiết lập kết nối\"."),
            new("Copy nhật ký", "Nút \"Copy\" cạnh tiêu đề Nhật ký: chép mọi dòng nhật ký để dán vào tin nhắn / ghi chú khi cần báo lỗi."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Thiết lập kết nối", "", "Hộp thoại có 4 tab, mỗi tab lưu riêng thông tin của 1 loại database.",
        [
            new("PostgreSQL", "Host, Port (mặc định 5432), Database, Username, Password, Schema (để trống = mọi schema)."),
            new("Oracle", "Host, Port (mặc định 1521), chọn kết nối bằng Service Name hoặc SID rồi nhập ô tương ứng, Username, Password, Schema (để trống = schema trùng tên Username, theo quy ước Oracle)."),
            new("MySQL", "Host, Port (mặc định 3306), Database, Username, Password. Dùng được cho cả MariaDB. Không có ô Schema vì trong MySQL schema chính là database."),
            new("SQL Server", "Host (hoặc MAYCHU\\TenInstance - khi đó bỏ qua Port), Port (mặc định 1433), Database, Schema (vd dbo, để trống = mọi schema). Tích \"Windows Authentication\" để đăng nhập bằng tài khoản Windows đang dùng (không cần Username / Password). Mô tả bảng / cột lấy từ MS_Description (phần Description trong SSMS)."),
            new("Thử kết nối", "Nút \"Thử kết nối\" cuối hộp thoại: kết nối thử bằng thông tin đang nhập ở tab hiện tại (chưa cần Lưu). Thành công (xanh) báo phiên bản máy chủ và số bảng / view sẽ đọc; 0 bảng (vàng) thường là sai Schema hoặc thiếu quyền; lỗi (đỏ) kèm gợi ý cần sửa - sai mật khẩu, sai database, sai Service Name / SID, không tới được máy chủ. Đổi tab hoặc đóng hộp thoại thì kết quả cũ bị xoá."),
            new("Lưu ý mật khẩu", "Mật khẩu lưu dạng chữ thường trong Data\\Config\\config.xml cạnh exe - chỉ dùng trên máy cá nhân, không chép file này ra ngoài."),
        ]),

        new("Hồ sơ kết nối", "", "Lưu nhiều bộ thông tin kết nối có tên (vd dev / test / prod) rồi chọn nhanh ở combobox \"Hồ sơ\" trên cùng.",
        [
            new("Hồ sơ là gì", "Mỗi hồ sơ có nguồn đang chọn + thông tin kết nối của cả 4 tab (PostgreSQL / Oracle / MySQL / SQL Server) + file .db / .html riêng. Đổi hồ sơ là đổi luôn Nguồn, thông tin kết nối và bước đang làm của hồ sơ đó. Hồ sơ đang chọn được nhớ cho lần mở sau."),
            new("Thêm hồ sơ", "Nút ⋯ cạnh combobox Hồ sơ → Thêm hồ sơ: đặt tên (gợi ý dev, test, prod) → hồ sơ mới sao chép thông tin của hồ sơ đang chọn, rồi bấm \"Thiết lập kết nối\" để sửa Host / Database / mật khẩu cho môi trường đó. Tên tối đa 40 ký tự, không trùng (không phân biệt hoa thường), không có \\ / : * ? \" < > |."),
            new("Đổi tên / Xoá", "Nút ⋯ → Đổi tên hồ sơ, hoặc Xoá hồ sơ (có hỏi lại; không xoá được hồ sơ cuối cùng). Xoá hồ sơ không xoá file .db / .html đã có trong Data\\Database."),
            new("Tên file theo hồ sơ", "File .db / .html đặt tên \"{tên database} ({tên hồ sơ})\", vd \"orders (dev)\" và \"orders (prod)\" - cùng tên database ở 2 môi trường không ghi đè nhau. Hồ sơ \"Mặc định\" giữ tên cũ \"{tên database}\". Đổi tên hồ sơ thì tên file đổi theo - đọc lại database (bước 1)."),
            new("Từ bản cũ", "Thông tin kết nối đã lưu trước khi có hồ sơ tự thành hồ sơ \"Mặc định\" (giữ nguyên nguồn, cả 4 tab và file đã đọc)."),
        ]),

        new("So sánh schema", "", "So 2 lần đọc database - vd dev với prod trước khi deploy, hoặc bản tuần trước với bản hôm nay - ra 1 trang HTML liệt kê chỗ khác nhau.",
        [
            new("Chuẩn bị", "Mỗi lần bấm \"1. Đọc database\" của 1 hồ sơ / nguồn tạo 1 file trong Data\\Database, vd đọc database ở hồ sơ dev và ở hồ sơ prod. Bản cũ của cùng database được tự giữ trong Lịch sử (mục dưới) nên so \"bản cũ ↔ bản mới\" không cần chép file."),
            new("So với bản trước", "Đọc lại database mà schema có thay đổi thì Nhật ký ghi tóm tắt, vd \"So với lần đọc trước (21/10 09:15): 1 bảng mới, 2 bảng có thay đổi\", và dưới dòng trạng thái hiện nút \"So với bản trước (đọc lúc ...)\" - bấm để mở trang so sánh bản cũ (A) với bản đang dùng (B). Đọc lại mà không có gì thay đổi thì Nhật ký ghi \"Không có thay đổi so với lần đọc trước\"."),
            new("Lịch sử các lần đọc", "Trước khi \"1. Đọc database\" ghi đè, file .db cũ được chép vào Data\\Database\\Lịch sử\\{tên file}\\ (tên = lúc đọc bản đó). Lần đọc giống hệt lần trước thì không giữ thêm bản trùng. Mặc định giữ 10 bản mới nhất cho mỗi database - đổi ở Cài đặt (0 = không giữ)."),
            new("Chọn A và B", "Nút \"So sánh schema\" trên cùng: A (gốc) chọn sẵn file của hồ sơ / nguồn đang chọn, B là file của hồ sơ / nguồn khác mới đọc nhất. Danh sách có cả các bản cũ trong Lịch sử (ghi \"(bản cũ)\"), ghi rõ mỗi file đọc lúc nào; nút \"Đổi chỗ A ↔ B\" để đảo chiều. Bấm \"So sánh\" → trang HTML mở trong trình duyệt, lưu ở Data\\Database\\So sánh {A} - {B}.html."),
            new("Trang kết quả", "Trên cùng: số bảng chỉ có ở A, chỉ có ở B, có thay đổi, giống nhau (bấm để nhảy tới). Bên dưới: bảng chỉ có ở 1 bên, rồi từng bảng có thay đổi - cột thêm / bớt, đổi kiểu, độ dài, Null, mặc định, khoá chính, mô tả (cột A đỏ nhạt, B xanh nhạt); khoá ngoại, index, ràng buộc UNIQUE / CHECK chỉ có ở 1 bên."),
            new("So theo nội dung", "Tên bảng / cột không phân biệt hoa thường. Index, ràng buộc, khoá ngoại so theo định nghĩa (cột, loại, bảng tham chiếu) chứ không theo tên - tên do hệ thống tự đặt (SYS_C..., PK__...) khác nhau ở mỗi môi trường nên không bị báo nhầm. Thứ tự cột khác không tính là thay đổi."),
            new("Ẩn khác mô tả", "Tích \"Ẩn thay đổi chỉ khác mô tả (comment)\" trên trang để ẩn dòng mô tả và bảng chỉ khác mô tả - comment hay lệch giữa các môi trường, thường không quan trọng."),
            new("File đọc bằng bản cũ", "File .db đọc bằng bản trước khi có index / ràng buộc thì chỉ so bảng, cột, khoá ngoại - trang ghi rõ; đọc lại file đó để so đủ."),
        ]),

        new("Cài đặt", "", "Nút \"Cài đặt\" trên thanh nút. Bấm Lưu để áp dụng, \"Mặc định\" để điền lại giá trị gốc.",
        [
            new("Giới hạn thời gian kết nối", "Số giây chờ kết nối tới máy chủ (mặc định 10). Quá thời gian thì báo lỗi thay vì chờ mãi. Mạng chậm / qua VPN xa thì tăng lên."),
            new("Giới hạn thời gian truy vấn", "Số giây tối đa cho mỗi câu đọc schema (mặc định 120). Database rất nhiều bảng / máy chủ chậm mà báo \"truy vấn quá giờ\" thì tăng lên, hoặc nhập Schema để đọc ít hơn."),
            new("Tự mở file HTML sau khi xuất", "Bật thì \"2. Xuất HTML\" xong tự mở trang trong trình duyệt."),
            new("Tự mở thư mục chứa file HTML sau khi xuất", "Bật thì \"2. Xuất HTML\" xong mở Explorer tại Data\\Database, chọn sẵn file vừa xuất - tiện để gửi file cho người khác. Bật được cùng lúc với tuỳ chọn trên."),
            new("Giữ lại số bản đọc cũ", "Số bản đọc trước giữ trong Data\\Database\\Lịch sử cho mỗi database (mặc định 10, tối đa 50) - dùng cho nút \"So với bản trước\" và hộp thoại So sánh schema. Giảm số này thì bản cũ thừa của database đang chọn bị xoá ngay khi Lưu; 0 = không giữ bản nào (Nhật ký vẫn ghi tóm tắt thay đổi sau mỗi lần đọc)."),
        ]),

        new("Tra cứu trong trang HTML", "", "Menu trái là danh sách bảng (bấm để xem), bên phải là định nghĩa bảng đang chọn. Ô \"Tên bảng...\" và \"Tên cột (tuỳ chọn)...\" + nút Tìm kiếm kết hợp như dưới đây.",
        [
            new("Chỉ nhập Tên bảng", "Hiện toàn bộ cột của bảng: STT, tên cột, kiểu (đã gồm độ dài, vd VARCHAR2(100)), Null, mô tả (comment của cột trong database)."),
            new("Tên bảng + Tên cột", "Chỉ hiện các cột khớp Tên cột trong đúng bảng đó."),
            new("Chỉ nhập Tên cột", "Tìm mọi bảng có cột khớp, kết quả nhóm theo từng bảng - dùng khi không nhớ cột thuộc bảng nào. Chỗ khớp được tô vàng."),
            new("Lọc Tất cả / Bảng / View", "3 nút dưới ô tìm (kèm số lượng): chỉ hiện bảng, chỉ hiện view (gồm cả MATERIALIZED VIEW), hoặc cả hai - áp dụng cho menu trái và cho kết quả tìm cột. View có nhãn VIEW cạnh tên. Bấm link khoá ngoại tới 1 bảng đang bị lọc ẩn thì tự về \"Tất cả\". Trình duyệt nhớ lựa chọn cho lần mở sau."),
            new("Tìm cả trong mô tả", "Tích ô này thì Tên bảng khớp cả mô tả (comment) của bảng, Tên cột khớp cả mô tả của cột - vd gõ \"khách hàng\" ra các bảng / cột có comment chứa chữ đó dù tên là customers / cust_id. Bảng khớp nhờ mô tả hiện đoạn mô tả (tô chỗ khớp) dưới tên ở menu trái."),
            new("Màu nền", "Vàng nhạt = cột khoá chính."),
            new("Khoá ngoại", "Bảng khoá ngoại phía trên bảng cột: bấm tên bảng tham chiếu để mở bảng đó ở tab mới. Thêm #table=TÊN_BẢNG vào cuối đường dẫn file HTML để chia sẻ link tới 1 bảng."),
            new("Số dòng", "\"≈ 1.234 dòng\" dưới tên bảng ở menu trái và dòng \"Số dòng (ước tính...)\" trên đầu bảng: lấy từ thống kê của database (không đếm thật nên đọc nhanh cả bảng rất lớn), chỉ mới bằng lần thống kê gần nhất (PostgreSQL: ANALYZE / VACUUM; Oracle: DBMS_STATS; MySQL: ước tính của InnoDB; SQL Server: luôn cập nhật). Chưa có thống kê thì ghi rõ; view không có số dòng."),
            new("Index", "Bảng \"Index\" dưới bảng cột: tên index, các cột (theo thứ tự, DESC nếu sắp giảm; index theo biểu thức hiện biểu thức), loại (Khoá chính / UNIQUE / Thường + kiểu như btree, CLUSTERED), ghi chú INCLUDE (...) / WHERE ... (partial / filtered index). Index theo hàm (Oracle function-based) hiện biểu thức, vd COALESCE(\"A\",\"B\")."),
            new("Ràng buộc UNIQUE / CHECK", "Bảng cuối: tên ràng buộc, loại, định nghĩa (các cột của UNIQUE, điều kiện của CHECK). Ràng buộc NOT NULL đã thể hiện ở cột Null nên không lặp lại. Cần Oracle 12c+ / MySQL 8.0.16+ (MariaDB 10.2+) để đọc CHECK."),
            new("File đọc bằng bản cũ", "Dữ liệu đọc trước khi có số dòng / index / ràng buộc vẫn xuất HTML được, trang ghi nhắc bấm lại \"1. Đọc database\" để có đủ thông tin."),
        ]),

        new("Nhiều database và cập nhật", "", "Mỗi database có file dữ liệu riêng.",
        [
            new("Bảng trùng tên ở nhiều schema", "Để trống Schema mà 2 schema có bảng cùng tên (vd public.orders và audit.orders) thì chỉ những bảng trùng đó hiện dạng schema.bảng trong trang HTML (cột, khoá ngoại đổi theo); bảng không trùng giữ tên ngắn. Khung Nhật ký ghi rõ bảng nào được đổi. Khoá ngoại trỏ sang schema không đọc cũng hiện dạng schema.bảng."),
            new("Tên file theo database", "File .db / .html đặt theo tên database (PostgreSQL / MySQL / SQL Server) hoặc Service Name / SID (Oracle) kèm tên hồ sơ, vd \"orders (prod)\" - đọc nhiều database (hoặc cùng database ở dev / prod) không ghi đè lên nhau."),
            new("Đã đọc trước đó", "Mở lại module (hoặc đổi Nguồn) vẫn nhận file .db / .html đã có của database đang chọn - không cần đọc lại nếu schema chưa đổi."),
            new("Huỷ khi đang đọc", "Đang đọc database thì nút \"Huỷ\" hiện cạnh nút 1 - bấm để dừng ngay (kể cả khi còn đang chờ kết nối). Dữ liệu đọc lần trước (file .db) giữ nguyên, vẫn \"2. Xuất HTML\" được."),
            new("Đọc lỗi vẫn giữ bản cũ", "Đọc database bị lỗi (mất mạng, sai mật khẩu...) không xoá dữ liệu lần trước: nút \"2. Xuất HTML\" vẫn bật để xuất lại bản cũ nếu cần; nút 1 có màu xanh để nhắc đọc lại."),
            new("Lấy dữ liệu mới nhất", "Bấm lại \"1. Đọc database\" rồi \"2. Xuất HTML\". Mỗi lần đọc là dựng lại toàn bộ; bản trước đó được giữ trong Lịch sử để so sánh (xem So sánh schema)."),
        ]),
    ];
}
