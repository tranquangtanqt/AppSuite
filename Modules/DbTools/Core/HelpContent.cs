namespace DbTools.Core;

/// <summary>1 mục trong cửa sổ Hướng dẫn. <paramref name="Keys"/>: phím / thao tác hiện thành các "phím" nhỏ.</summary>
public sealed record HelpItem(string Name, string Description, params string[] Keys);

/// <param name="PageTag">Tên trang (class trong DbTools.Views) mà mục này hướng dẫn - F1 trên trang đó mở thẳng mục này;
/// null = mục chung.</param>
public sealed record HelpSection(string Title, string Glyph, string Summary, string? PageTag, IReadOnlyList<HelpItem> Items);

/// <summary>Nội dung cửa sổ Hướng dẫn (F1). Mỗi trang có đúng 1 mục (unit test kiểm tra). Đổi tính năng thì cập nhật cả README.</summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "DbTools chạy cả thư mục script .sql lên PostgreSQL theo đúng thứ tự, trong 1 transaction: lỗi ở đâu là huỷ hết, DB không bị dở dang.", null,
        [
            new("1. Tạo kết nối", "Trang Kết nối → Thêm: host, port, database, user, mật khẩu → Thử kết nối. Tạo nhiều hồ sơ (dev, test...) để đổi nhanh."),
            new("2. Đọc thư mục", "Trang Chạy thư mục SQL → chọn thư mục chứa file .sql (kéo-thả thư mục vào trang cũng được) → Đọc thư mục. App tự sắp thứ tự chạy."),
            new("3. Kiểm tra", "Đối chiếu với DB mà không thay đổi gì: đối tượng nào đã có (CREATE sẽ lỗi), file nào xoá / sửa dữ liệu, lệnh nào không chạy được."),
            new("4. Chạy thử", "Chạy thật mọi câu rồi ROLLBACK ở cuối: biết chắc script chạy được trên DB đó mà DB không đổi."),
            new("5. Chạy", "Hỏi lại (ghi rõ database đích) rồi chạy trong 1 transaction và COMMIT. Lỗi ở câu nào: ROLLBACK toàn bộ, báo file + dòng lỗi."),
            new("Mở lại hướng dẫn này", "Mục Hướng dẫn cuối menu trái, nút ? hoặc phím F1 (mở thẳng phần của trang đang xem).", "F1"),
        ]),

        new("Chạy thư mục SQL", "", "Chạy mọi file .sql trong 1 thư mục (gồm thư mục con) lên database đang chọn.", "RunPage",
        [
            new("Thứ tự tự động", "Chạy theo vần ABC thường sai (CHECK / COMMENT chạy trước khi có bảng, view chạy trước bảng nó dùng). App sắp: file nhắc tới bảng / view / hàm do file khác tạo thì chạy sau file đó; file xoá dữ liệu bảng X (DELETE) chạy trước file thêm dữ liệu vào X (INSERT); còn lại theo loại: sequence → bảng → view → hàm → trigger → index / ràng buộc → comment → dữ liệu."),
            new("Schema", "Ô Schema cạnh ô database (nhớ theo từng hồ sơ kết nối; mở ô để chọn trong danh sách schema của DB hoặc gõ tên). Nút ✕ hoặc để trống = mặc định: theo search_path của server / user (thường là public). Chọn / gõ 1 schema = mọi lần Kiểm tra, Chạy thử, Chạy đặt search_path về schema đó: tên không ghi schema trong file (CREATE TABLE abc...) được tạo / tìm ở schema đã chọn; tên có ghi schema (CREATE TABLE s1.abc) vẫn theo file. Schema chưa có trong database → báo lỗi, không tự tạo (tránh chạy vào schema gõ nhầm tên)."),
            new("Tìm cả trong public", "Bật sẵn khi đã chọn schema: search_path = schema đã chọn, public - script dùng được bảng / hàm nền nằm ở public (vd bảng / hàm chung của mcframe), còn đối tượng mới vẫn tạo ở schema đã chọn. Tắt khi mọi thứ (cả bảng nền) nằm chung 1 schema."),
            new("Chỉnh thứ tự bằng tay", "Kéo dòng lên / xuống, hoặc chọn dòng rồi ▲ / ▼. Thứ tự chỉnh tay được nhớ theo thư mục (lần sau Đọc thư mục dùng lại; file mới xếp cuối). Sắp lại tự động = bỏ thứ tự đã nhớ.", "Kéo dòng"),
            new("Bỏ khỏi danh sách", "Không chạy 1 file (vd script tạm). Đọc lại thư mục vẫn bỏ qua file đó tới khi bấm Sắp lại tự động."),
            new("Cột Loại / Cảnh báo", "Loại = các loại lệnh trong file (Bảng, View, Hàm, Trigger, Comment...). Cảnh báo = file có DELETE / UPDATE / TRUNCATE / DROP (xoá / sửa dữ liệu hoặc đối tượng)."),
            new("Cột Kết quả", "Sau Kiểm tra: Đạt / số cảnh báo / lỗi. Sau Chạy: Đã chạy, Lỗi dòng N (kèm thông báo của PostgreSQL), Không chạy (file sau chỗ lỗi), Chạy được (đã rollback). Chọn 1 dòng để xem chi tiết ở ô dưới danh sách; Mở file để sửa."),
            new("Kiểm tra", "Chỉ đọc catalog của DB: CREATE TABLE / CREATE TRIGGER... mà đối tượng đã có → cảnh báo \"câu này sẽ lỗi\"; CREATE OR REPLACE đã có → thông tin \"sẽ được thay\". Không ghi gì vào DB."),
            new("Chạy thử", "Chạy hết rồi ROLLBACK. Bắt được mọi lỗi thật (thiếu bảng nền, sai cú pháp, sai kiểu...) mà DB không đổi. Nên chạy thử trước mỗi lần chạy thật."),
            new("Chạy (1 transaction)", "BEGIN → mọi câu của mọi file → COMMIT. Câu nào lỗi hoặc bấm Huỷ → ROLLBACK, DB y như trước. PostgreSQL rollback được cả CREATE / DROP / ALTER. Trước khi chạy luôn hỏi lại, ghi rõ host:port/database, file xoá / sửa dữ liệu và bảng sẽ mất dữ liệu."),
            new("Xoá đối tượng cũ rồi tạo lại", "Để chạy lại script nhiều lần khi đang phát triển: trước khi chạy, xoá các đối tượng mà thư mục tạo ra (DROP ... IF EXISTS theo thứ tự ngược, không CASCADE), trong cùng transaction. DROP TABLE xoá luôn dữ liệu - Kiểm tra / hộp thoại hỏi lại báo bảng nào đang có dữ liệu."),
            new("Xem script xoá", "Ghi script xoá ngược ra file .sql tạm (%TEMP%\\AppSuite\\DbTools) và mở ra để xem / chạy tay. Câu thêm / sửa / xoá dữ liệu không tự đảo ngược được - liệt kê cuối script để kiểm tra tay. Đã chọn schema thì bảng / view / hàm... trong script xoá ghi rõ schema (vd addon.app_order_all) - không xoá nhầm đối tượng cùng tên ở public."),
            new("Lệnh psql", "Dòng \\encoding UTF-8 đầu file được hiểu (đọc file theo encoding đó). Lệnh psql khác (\\i, \\set, \\connect...) báo lỗi - chạy file đó bằng psql. File tự BEGIN / COMMIT hoặc có lệnh không chạy được trong transaction (CREATE DATABASE, VACUUM, CREATE INDEX CONCURRENTLY) cũng báo lỗi."),
            new("Encoding", "File UTF-8 (có / không BOM) đọc thẳng. File không phải UTF-8 đọc theo Shift-JIS kèm cảnh báo - kiểm tra chữ Nhật trong COMMENT trước khi chạy."),
            new("Nhật ký", "Ghi từng bước: kết nối, từng file (số câu, thời gian), NOTICE của server, lỗi kèm file + dòng, ROLLBACK / COMMIT. Copy nhật ký để gửi; mỗi lần chạy cũng lưu thành file log (trang Lịch sử chạy)."),
        ]),

        new("Lịch sử chạy", "", "Mọi lần Chạy / Chạy thử: lúc nào, database nào, thư mục nào, bản svn / git mấy, kết quả.", "HistoryPage",
        [
            new("Bản svn / git", "Nếu thư mục script nằm trong working copy svn (hoặc git) và máy có svn.exe / git.exe: ghi số revision, kèm \"có sửa chưa commit\" nếu file đã sửa. Dùng để trả lời \"DB test đã áp bản nào?\"."),
            new("Mở log", "File log của lần chạy đó: thứ tự file + kết quả từng file + toàn bộ nhật ký. Lưu ở Data\\History\\Logs cạnh DbTools.exe; giữ 500 lần gần nhất."),
            new("Mở thư mục", "Mở thư mục script của lần chạy đó trong Explorer."),
        ]),

        new("Kết nối", "", "Hồ sơ kết nối PostgreSQL (dev, test...). Sửa là tự lưu.", "ConnectionsPage",
        [
            new("Thêm / Nhân bản / Xoá", "Nhân bản = chép hồ sơ đang chọn rồi sửa (vd cùng server, khác database)."),
            new("Thử kết nối", "Mở kết nối, báo phiên bản PostgreSQL + user@database + schema sẽ tạo đối tượng (theo ô Schema ở trang Chạy) và search_path. Lỗi thì báo lý do (sai mật khẩu, không tới được host...)."),
            new("Mật khẩu", "Lưu đã mã hoá theo tài khoản Windows (DPAPI) trong Data\\Config\\connections.json cạnh DbTools.exe. Chép file sang máy / tài khoản khác thì mật khẩu trống, nhập lại."),
        ]),
    ];

    /// <summary>Mục hướng dẫn của trang <paramref name="pageTag"/> (không có thì mục đầu).</summary>
    public static HelpSection ForPage(string? pageTag) => Sections.FirstOrDefault(s => s.PageTag == pageTag) ?? Sections[0];
}
