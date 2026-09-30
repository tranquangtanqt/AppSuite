namespace FileTools.Core;

/// <summary>1 mục trong cửa sổ Hướng dẫn. <paramref name="Keys"/>: phím / thao tác hiện thành các "phím" nhỏ.</summary>
public sealed record HelpItem(string Name, string Description, params string[] Keys);

/// <param name="PageTag">Tên trang (class trong FileTools.Views) mà mục này hướng dẫn - F1 trên trang đó mở thẳng mục
/// này; null = mục chung.</param>
public sealed record HelpSection(string Title, string Glyph, string Summary, string? PageTag, IReadOnlyList<HelpItem> Items);

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (mục "Hướng dẫn" cuối menu / nút ? / F1) cho người dùng cuối. Mỗi trang có đúng 1 mục
/// (unit test kiểm tra: thêm trang mới mà quên viết hướng dẫn là test đỏ). Thêm / đổi tính năng thì cập nhật cả README.
/// </summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "FileTools xử lý file text / CSV / log lớn (vài trăm MB tới vài GB) mà Excel, Notepad không mở nổi. Mọi thao tác đọc file từng đoạn nên RAM không tăng theo cỡ file.", null,
        [
            new("1. Chọn trang ở menu bên trái", "Mỗi trang làm 1 việc: Nối, Tách, Trích dòng, Tìm, Lọc, Đổi encoding, CSV, So sánh, Theo dõi log, Tìm file trùng..."),
            new("2. Chọn file", "Bấm Chọn..., dán đường dẫn vào ô, hoặc kéo-thả file / thư mục từ Explorer vào trang.", "Kéo-thả"),
            new("3. Chọn tuỳ chọn rồi bấm nút chính (nút màu xanh)", "Thao tác chạy nền: thanh tiến độ + nút Huỷ. Huỷ giữa chừng không để lại file dở."),
            new("4. Xem kết quả", "Nhật ký ở cuối trang ghi kết quả từng bước (mới nhất ở trên cùng). Mở kết quả = mở file / thư mục vừa tạo; Mở thư mục = mở Explorer và chọn sẵn file."),
            new("Mở lại hướng dẫn này", "Mục Hướng dẫn cuối menu trái, nút ? trên cùng, hoặc phím F1 (mở thẳng phần của trang đang xem).", "F1"),
        ]),

        new("Tuỳ chọn dùng chung", "", "Các ô xuất hiện trên nhiều trang.", null,
        [
            new("Encoding đầu ra", "Mặc định UTF-8 (không BOM). Chọn UTF-8 có BOM nếu file sẽ mở bằng Excel (Excel cần BOM để đọc đúng tiếng Việt / Nhật trong CSV); Shift-JIS cho hệ thống Nhật cũ; \"Giữ như file nguồn\" để không đổi gì."),
            new("Encoding file nguồn", "Tự nhận: BOM → UTF-16 → UTF-8 → Shift-JIS. Nhận sai (chữ bị lỗi font) thì dùng trang Đổi encoding với ô \"Đọc file nguồn theo\" để ép đúng."),
            new("Xuống dòng", "Giữ như nguồn (mặc định), CRLF (Windows) hoặc LF (Unix / Linux)."),
            new("File tạm", "Trích dòng, Tìm (xuất dòng khớp), So sánh (báo cáo) ghi vào %TEMP%\\AppSuite\\FileTools. Nút \"Mở thư mục tạm\" ở trang Trích dòng."),
            new("Mở kết quả CSV", "File CSV / TSV mở bằng CsvEditor nếu có trong cùng bộ AppSuite, ngược lại bằng ứng dụng mặc định của Windows."),
            new("Mẫu (preset)", "Thanh Mẫu ở đầu cửa sổ: Lưu mẫu... ghi lại mọi tuỳ chọn đang chọn trên trang (không lưu danh sách file); chọn mẫu + Nạp để dùng lại; Xoá để bỏ. Mỗi trang có danh sách mẫu riêng."),
            new("An toàn dữ liệu", "Không bao giờ sửa file gốc trừ khi bạn chọn \"Ghi đè file gốc\" - khi đó bản cũ luôn được giữ thành <tên>.bak. File đang ghi dở có đuôi .partial và bị xoá nếu huỷ / lỗi."),
        ]),

        new("Nối file", "", "Nối nội dung nhiều file thành 1 file.", "MergePage",
        [
            new("Các bước", "1) Chọn thư mục nguồn + mẫu lọc (vd *.csv;*.log), tick Gồm thư mục con nếu cần, bấm Liệt kê file. 2) Kiểm tra thứ tự trong danh sách. 3) Chọn file kết quả + encoding. 4) Nối file."),
            new("Thứ tự nối", "Theo tên hiểu số (file2 trước file10), ngày sửa hoặc dung lượng - chọn rồi bấm Sắp xếp lại. Chỉnh tay: chọn 1 file rồi ▲ Lên / ▼ Xuống, hoặc Bỏ khỏi danh sách. Kéo-thả thêm file lẻ vào danh sách."),
            new("CSV: giữ dòng tiêu đề 1 lần", "Bỏ dòng tiêu đề của file thứ 2 trở đi nếu giống hệt tiêu đề file đầu. File có tiêu đề khác vẫn được giữ và báo trong nhật ký."),
            new("Chèn tên file / dòng trống", "Chèn dòng \"===== tên file =====\" trước mỗi phần, hoặc 1 dòng trống giữa 2 file - tiện khi nối log để biết dòng nào của file nào."),
            new("Lưu ý", "Mỗi file tự nhận encoding riêng (trộn Shift-JIS + UTF-8 được); file trước không kết thúc bằng xuống dòng thì tự thêm. File kết quả nằm trong thư mục nguồn thì tự bỏ qua khỏi danh sách."),
        ]),

        new("Tách file", "", "Tách 1 file lớn thành nhiều phần, không bao giờ cắt ngang 1 dòng.", "SplitPage",
        [
            new("Các bước", "1) Chọn file nguồn. 2) Chọn cách tách + nhập số. 3) Kiểm tra thư mục chứa các phần (mặc định <tên>_parts cạnh file gốc). 4) Tách file."),
            new("3 cách tách", "Dung lượng mỗi phần (MB) - vd để gửi mail / tải lên; Số dòng mỗi phần - vd 1 triệu dòng cho Excel (giới hạn 1.048.576 dòng); Số phần - chia đều."),
            new("CSV", "File .csv / .tsv đọc theo bản ghi: ô trong ngoặc kép có xuống dòng không bị cắt đôi. Lặp dòng tiêu đề ở mọi phần (tự tick với file CSV)."),
            new("Tên các phần", "<tên>.part001.csv, part002... Nối lại đúng bản gốc bằng trang Nối file (encoding \"Giữ như file nguồn\")."),
        ]),

        new("Thông tin file", "", "Xem nhanh file trước khi xử lý.", "InfoPage",
        [
            new("Cách dùng", "Chọn hoặc kéo-thả file là tự phân tích (1 lượt đọc); bấm Phân tích lại nếu file vừa đổi."),
            new("Thông tin hiện", "Dung lượng, encoding, số dòng và dòng trống, kiểu xuống dòng (CRLF / LF / lẫn lộn), dòng cuối có xuống dòng không, dòng dài nhất."),
            new("File dạng bảng", "Dấu phân cách, tên cột, số bản ghi và các bản ghi lệch số cột so với dòng tiêu đề (kèm số thứ tự vài bản ghi đầu) - dùng để tìm dòng lỗi trước khi nhập vào hệ thống."),
        ]),

        new("Trích dòng", "", "Lấy 1 đoạn dòng của file lớn ra file tạm để mở xem.", "ExtractPage",
        [
            new("3 cách lấy", "N dòng đầu; từ dòng X đến dòng Y; N dòng cuối. Chỉ đọc đúng phần cần: file vài GB vẫn gần như tức thì (N dòng cuối đọc ngược từ cuối file)."),
            new("Kèm dòng tiêu đề", "Chèn dòng 1 lên đầu khi lấy đoạn giữa / cuối của CSV để mở bằng Excel vẫn có tên cột."),
            new("Mở file sau khi trích", "Tự mở file vừa trích (CSV bằng CsvEditor nếu có). File nằm ở thư mục tạm, xem bằng nút Mở thư mục tạm."),
        ]),

        new("Tìm", "", "Tìm chữ / regex trong file lớn.", "SearchPage",
        [
            new("Cách dùng", "Chọn file, gõ chữ cần tìm, Enter hoặc bấm Tìm. Kết quả: số dòng + nội dung dòng khớp.", "Enter"),
            new("Không phân biệt dấu (mặc định bật)", "\"thanh toan\" khớp \"Thanh toán\", \"da\" khớp \"đã\". Tắt khi cần khớp đúng dấu."),
            new("Regex", "Tick Regex để tìm theo biểu thức, vd ^ERROR, \\d{4}-\\d{2}-\\d{2}, (timeout|refused). Regex sai thì báo lỗi trong nhật ký."),
            new("Hiện tối đa / xuất file", "Chỉ hiện N dòng đầu (mặc định 10.000) nhưng vẫn đếm hết. Tick Xuất dòng khớp ra file tạm để lấy toàn bộ (kèm số dòng gốc nếu tick)."),
        ]),

        new("So sánh 2 file", "", "Tìm dòng thêm / bớt / sửa giữa 2 file text.", "ComparePage",
        [
            new("Cách dùng", "Chọn File A (cũ) và File B (mới) - hoặc kéo-thả 2 file cùng lúc - rồi bấm So sánh. ⇅ Đổi chỗ để đảo A / B.", "Kéo-thả 2 file"),
            new("Đọc kết quả", "Đỏ (−) = dòng chỉ có ở A (bị bỏ / sửa); xanh (+) = dòng chỉ có ở B (thêm / sửa); dòng trắng = giống nhau (ngữ cảnh). 2 cột bên trái là số dòng ở A và ở B."),
            new("Tuỳ chọn", "Không phân biệt hoa / thường; bỏ qua khác biệt khoảng trắng (thụt lề, dấu cách thừa); số dòng ngữ cảnh quanh mỗi chỗ khác. 2 file khác encoding vẫn so theo chữ."),
            new("Báo cáo HTML", "Mở kết quả = báo cáo đầy đủ trong trình duyệt (in / gửi được). Danh sách trong app chỉ hiện tối đa 20.000 dòng."),
            new("2 file khác nhau quá nhiều", "Hơn ~4.000 dòng sửa thì không dò thứ tự nữa mà liệt kê dòng chỉ có ở A / chỉ có ở B (không xét thứ tự) - vẫn trả lời được \"dòng nào mất / dòng nào mới\"."),
        ]),

        new("Theo dõi log", "", "Như tail -f: xem dòng mới của file log khi chương trình đang ghi.", "TailPage",
        [
            new("Cách dùng", "Chọn file log, số dòng cuối muốn xem trước, bấm ▶ Bắt đầu. Dòng mới tự hiện (kiểm tra mỗi 0,5 giây). ■ Dừng để dừng theo dõi."),
            new("Chỉ hiện dòng chứa", "Vd ERROR hoặc mã đơn hàng - không phân biệt dấu nếu tick. Bộ lọc áp dụng khi bấm Bắt đầu (đổi bộ lọc: Dừng rồi Bắt đầu lại)."),
            new("Tự cuộn xuống cuối", "Bỏ tick để đọc đoạn cũ mà không bị kéo xuống khi có dòng mới."),
            new("Lưu / xoá", "Lưu dòng đang hiện ra file tạm rồi mở; Xoá màn hình chỉ xoá phần hiển thị, không đụng file log. Chỉ giữ 5.000 dòng gần nhất trên màn hình."),
            new("Lưu ý", "Không khoá file - chương trình ghi log vẫn chạy bình thường. Dòng đang ghi dở chỉ hiện khi ghi xong. Log bị xoá nội dung / xoay vòng sang file mới cùng tên thì tự đọc lại từ đầu và báo."),
        ]),

        new("Lọc dòng", "", "Giữ hoặc bỏ các dòng chứa từ khoá, ghi ra file mới.", "FilterPage",
        [
            new("Các bước", "1) Chọn file. 2) Nhập từ khoá, mỗi dòng 1 từ. 3) Chọn khớp khi chứa 1 trong / tất cả các từ, và dòng khớp thì Giữ lại / Bỏ đi. 4) Lọc dòng."),
            new("Ví dụ", "Giữ các dòng chứa ERROR hoặc WARN của log; bỏ các dòng chứa \"healthcheck\"; giữ đơn hàng của 3 mã khách hàng."),
            new("CSV", "Lọc theo bản ghi (ô nhiều dòng không bị tách); tick Luôn giữ dòng tiêu đề để file kết quả vẫn có tên cột."),
            new("Regex", "Tick Regex thì chỉ dùng dòng đầu tiên của ô từ khoá làm biểu thức."),
        ]),

        new("Đổi encoding", "", "Chuyển nhiều file sang 1 encoding, vd Shift-JIS → UTF-8.", "EncodingPage",
        [
            new("Thêm file", "Dán đường dẫn file / thư mục rồi bấm Thêm, Thêm file..., Thêm thư mục... (theo mẫu lọc, có / không thư mục con), hoặc kéo-thả. Cột bên phải là encoding tự nhận của từng file.", "Kéo-thả"),
            new("Ghi kết quả", "Ra thư mục khác (mặc định converted, file gốc giữ nguyên) hoặc ghi đè file gốc (bản cũ giữ thành .bak; file đã đúng encoding thì bỏ qua)."),
            new("Đọc file nguồn theo", "Để Tự nhận; chỉ chọn tay khi encoding tự nhận sai (vd file Windows-1258 tiếng Việt cũ)."),
            new("Mất ký tự", "Chữ không có trong encoding đích (vd tiếng Việt → Shift-JIS) được thay bằng \"?\" và nhật ký báo số ký tự bị mất - kiểm tra trước khi dùng file."),
        ]),

        new("Đổi xuống dòng", "", "Đổi CRLF (Windows) ↔ LF (Unix / Linux) cho nhiều file.", "NewlinePage",
        [
            new("Cách dùng", "Thêm file (như trang Đổi encoding), chọn Đổi thành CRLF hoặc LF, bấm Đổi xuống dòng. Cột bên phải xem trước kiểu xuống dòng hiện tại."),
            new("Khi nào cần", "Script / file cấu hình đưa lên server Linux (cần LF); file từ Linux mở trên Windows bị dính dòng (cần CRLF)."),
            new("Lưu ý", "Giữ nguyên encoding. File đã đúng kiểu được bỏ qua, không tạo .bak thừa."),
        ]),

        new("Thay thế hàng loạt", "", "Tìm và thay chữ / regex trên nhiều file.", "ReplacePage",
        [
            new("Các bước", "1) Nhập Tìm và Thay bằng. 2) Thêm file. 3) Bấm Xem trước: cột bên phải hiện số chỗ sẽ thay từng file, nhật ký hiện vài dòng trước / sau - CHƯA ghi gì. 4) Kiểm tra xong mới bấm Thay thế."),
            new("Tuỳ chọn", "Phân biệt hoa / thường; Chỉ nguyên từ (\"cat\" không khớp \"concat\"); Regex - khi đó Thay bằng dùng được $1, $2... (vd tìm (\\d{4})-(\\d{2}), thay $2/$1)."),
            new("Ghi kết quả", "Ra thư mục khác hoặc ghi đè giữ .bak. Giữ nguyên encoding và kiểu xuống dòng từng file; file không có chỗ nào để thay thì không đụng tới."),
            new("Lưu ý", "Thay theo từng dòng: chữ cần tìm không được vắt qua xuống dòng."),
        ]),

        new("CSV: Tách theo cột", "", "Mỗi giá trị của 1 cột → 1 file riêng.", "CsvSplitPage",
        [
            new("Cách dùng", "Chọn file CSV, chọn cột (vd mã khách hàng, tháng, chi nhánh), bấm Tách theo cột. Kết quả: <tên>_<giá trị>.csv trong thư mục <tên>_theo_<cột>."),
            new("Chi tiết", "Giữ thứ tự bản ghi, lặp dòng tiêu đề ở mọi file; giá trị trống → file _(trong). Giá trị chỉ khác hoa / thường (A / a) vẫn ra 2 file riêng."),
            new("Giới hạn", "Cột có hơn 5.000 giá trị khác nhau thì dừng, không tạo file nào - thường là chọn nhầm cột (mã đơn, số tiền)."),
        ]),

        new("CSV: Bỏ dòng trùng", "", "Bỏ dòng trùng, giữ lần xuất hiện đầu tiên.", "DedupePage",
        [
            new("So cả dòng", "2 dòng giống hệt nhau thì bỏ dòng sau. Dùng được cả cho file text / log thường."),
            new("Chỉ theo các cột đã tích", "Vd tích cột Mã khách hàng: mỗi mã chỉ giữ dòng đầu tiên, dù các cột khác khác nhau."),
            new("Tuỳ chọn", "Không phân biệt hoa / thường; bỏ khoảng trắng đầu / cuối khi so. Nhật ký ghi số thứ tự vài dòng trùng đầu tiên để kiểm tra."),
            new("RAM", "So cả dòng phải nhớ mỗi dòng ~14 byte: 25 triệu dòng ≈ 360 MB. Không đủ RAM thì báo lỗi rõ - khi đó so theo cột hoặc tách file trước."),
        ]),

        new("CSV: Chọn / sắp cột", "", "Giữ các cột cần dùng, đổi thứ tự cột.", "ColumnsPage",
        [
            new("Cách dùng", "Bỏ tick cột không cần; chọn 1 cột rồi ▲ Lên / ▼ Xuống để đổi thứ tự (thứ tự trong danh sách = thứ tự ghi ra). Tích tất cả / Bỏ tích tất cả / Thứ tự như gốc để làm lại."),
            new("Dấu phân cách ghi ra", "Giữ như nguồn hoặc đổi luôn (vd chọn cột + đổi sang Tab trong 1 lần). Bọc mọi ô trong \"\" nếu hệ thống nhận yêu cầu vậy."),
            new("Lưu ý", "Bản ghi thiếu cột đã chọn thì ô đó để trống và nhật ký báo số bản ghi thiếu."),
        ]),

        new("CSV: Đổi dấu phân cách", "", "Đổi , ↔ Tab ↔ ; ↔ | cho cả file.", "DelimiterPage",
        [
            new("Cách dùng", "Chọn file (dấu phân cách hiện tại tự nhận - chọn tay ở ô Dấu phân cách nếu sai), chọn Đổi thành, bấm Đổi dấu phân cách. Đổi sang Tab thì file kết quả đuôi .tsv."),
            new("Ngoặc kép", "Ô có chứa dấu phân cách mới / ngoặc kép / xuống dòng tự được bọc ngoặc kép cho đúng chuẩn CSV; tick Bọc mọi ô để bọc hết."),
        ]),

        new("Tìm file trùng", "", "Tìm các file có nội dung giống hệt nhau (không cần cùng tên).", "DuplicatesPage",
        [
            new("Cách dùng", "Nhập 1 hoặc nhiều thư mục (cách nhau bằng ;) hoặc kéo-thả thư mục, mẫu lọc (* = mọi file), bấm Tìm file trùng. Kết quả gom theo nhóm, nhóm thừa nhiều dung lượng nhất ở trên."),
            new("Dọn bản thừa", "Tích các bản muốn bỏ (nút \"Tích các bản thừa\" giữ bản đầu mỗi nhóm) rồi Chuyển file đã tích vào Thùng rác - có hỏi xác nhận, khôi phục được từ Thùng rác. Không cho tích hết cả nhóm."),
            new("Tốc độ", "Chỉ đọc file khi có file khác cùng dung lượng, và phần lớn chỉ cần đọc 64 KB đầu. Bỏ qua file nhỏ hơn N KB để quét nhanh hơn."),
        ]),

        new("Giới hạn và mẹo", "", "Những điều nên biết khi làm với file rất lớn.", null,
        [
            new("Chỉ file chữ", "FileTools dành cho text / CSV / log. File nhị phân (zip, ảnh, Excel .xlsx) không xử lý được - riêng Tìm file trùng thì dùng được cho mọi loại file."),
            new("Tốc độ tham khảo (file 2 GB, 26 triệu dòng)", "Trích dòng đầu / cuối < 0,2 s; thông tin file ~15 s; tìm / lọc ~15-25 s; tách 10 phần ~20 s; chọn cột ~30 s. Ổ SSD nhanh hơn ổ HDD nhiều lần."),
            new("RAM", "Mọi thao tác dùng dưới ~80 MB dù file lớn cỡ nào, trừ Bỏ dòng trùng theo cả dòng (~14 byte / dòng)."),
            new("Mở file vài GB bằng Excel / CsvEditor", "Đừng mở thẳng: dùng Trích dòng để lấy 1 đoạn, hoặc Tách file theo 1 triệu dòng / phần."),
            new("Nhật ký lỗi", "Lỗi được ghi vào Logs\\filetools-<ngày>.log cạnh FileTools.exe (giữ 14 ngày) - gửi kèm khi báo lỗi."),
        ]),
    ];

    /// <summary>Mục hướng dẫn của 1 trang (F1), hoặc mục đầu tiên nếu trang không có mục riêng.</summary>
    public static HelpSection ForPage(string? pageTag) =>
        Sections.FirstOrDefault(s => s.PageTag is not null && s.PageTag == pageTag) ?? Sections[0];

    /// <summary>Các mục khớp mọi từ trong <paramref name="query"/> (không phân biệt hoa thường / dấu: "tach cot" ra
    /// "Tách theo cột"), nhóm theo danh mục.</summary>
    public static IReadOnlyList<(HelpSection Section, IReadOnlyList<HelpItem> Items)> Search(string query)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }
        var result = new List<(HelpSection, IReadOnlyList<HelpItem>)>();
        foreach (var section in Sections)
        {
            var items = section.Items
                .Where(i => words.All(Normalize($"{section.Title} {section.Summary} {i.Name} {i.Description} {string.Join(' ', i.Keys)}").Contains))
                .ToList();
            if (items.Count > 0)
            {
                result.Add((section, items));
            }
        }
        return result;
    }

    private static string Normalize(string text) => TextMatcher.FoldDiacritics(text).ToLowerInvariant();
}
