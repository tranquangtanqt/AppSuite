using SharedUI.Help;

namespace ImageCompare.Models;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút ? / F1) - mọi tính năng của ImageCompare cho người dùng cuối. Nguồn: README.md; thêm /
/// đổi tính năng thì cập nhật cả 2 chỗ. Tiêu đề danh mục chế độ xem trùng tên tab (ShowSection mở đúng phần đang dùng).
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Đưa 2 ảnh vào → chọn chế độ xem → xem chỗ khác, xuất ảnh / báo cáo.",
        [
            new("1. Đưa ảnh vào", "Ô A (ảnh gốc) và ô B (ảnh mới) ở trên cùng: nút Mở… / Dán, kéo-thả file vào ô, hoặc Ctrl+V dán ảnh trong clipboard.", "Ctrl+V"),
            new("2. Chọn chế độ xem", "Khác biệt (tô đỏ chỗ khác), Cạnh nhau, Chồng mờ, Thanh trượt, Tìm ảnh con, Tìm chữ, So chữ - các tab ngay dưới 2 ô ảnh."),
            new("3. Xem và xuất kết quả", "Bấm 1 mục trong bảng bên phải để phóng tới chỗ đó. Copy / Lưu PNG / Xuất báo cáo HTML để gửi người khác."),
            new("Mở lại hướng dẫn này", "Nút ? ở góc trên-phải, hoặc phím F1.", "F1"),
        ]),

        new("Đưa ảnh vào", "", "Mở file, dán từ clipboard, kéo-thả, đổi chỗ / bỏ ảnh.",
        [
            new("Mở… / Dán trong ô", "Mỗi ô A / B có nút Mở… (chọn file PNG / JPG / BMP…) và Dán (ảnh trong clipboard). Dùng lại để thay ảnh của ô đó."),
            new("Dán bằng phím", "Ctrl+V: dán vào A nếu A còn trống, không thì vào B (2 ô đều có ảnh → thay B). Ctrl+Shift+V: dán thẳng vào A. Dán được ảnh copy từ ScreenCapture / app khác hoặc file ảnh copy trong Explorer.", "Ctrl+V", "Ctrl+Shift+V"),
            new("Kéo-thả", "Thả file vào 1 ô để đặt ảnh ô đó. Thả vào vùng xem: 2 file cùng lúc = A và B; 1 file ở chế độ Cạnh nhau = nửa trái → A, nửa phải → B; chế độ khác → A nếu còn trống, không thì B."),
            new("Đổi chỗ / bỏ ảnh", "Nút ⇄ giữa 2 ô đổi chỗ A và B. Nút ✕ trên ô (hiện khi ô có ảnh) bỏ ảnh đó."),
            new("Mở từ dòng lệnh", "ImageCompare.exe [ảnh A] [ảnh B] mở sẵn 2 ảnh."),
        ]),

        new("Khác biệt", "", "So từng pixel: chỗ khác tô đỏ, mỗi vùng khác có khung và số.",
        [
            new("Đọc kết quả", "Ảnh B làm nhạt, pixel khác tô đỏ, mỗi vùng khác có khung + số. Bảng bên phải: kết luận, % pixel giống, SSIM (độ giống về cấu trúc), danh sách vùng - bấm 1 vùng để phóng tới (khung vàng)."),
            new("Căn chỉnh: Tự căn chỉnh", "Mặc định. Tự tìm độ lệch - ảnh chụp lệch vài px, khác lề, trang cuộn 1 đoạn."),
            new("Căn chỉnh: Căn theo dòng (trang dài)", "Trang dài mà B thêm / bớt 1 đoạn ở giữa. Dải cam = chỉ có ở B (thêm vào), dải xanh = chỉ có ở A (bị bỏ), phần còn lại so từng pixel."),
            new("Căn chỉnh: Soi 1 vùng (khoanh trên ảnh)", "2 ảnh lệch bố cục dần (khác font / trình duyệt: dòng cao hơn, ô rộng hơn) mà Tự căn tô đỏ gần hết. Kéo chuột trái khoanh 1 vùng (1 khối, 1 bảng): chỉ so vùng đó, vùng tự tìm chỗ khớp ở B và từng ô (nhãn, ô nhập) căn riêng; ngoài vùng phủ tối. Khoanh lại bất cứ lúc nào. Chữ / viền ô vẽ khác nhau giữa 2 trình duyệt thì vẫn bị tô đỏ - xem chữ nào khác bằng So chữ.", "Kéo chuột trái", "Chuột phải / giữa: cuộn"),
            new("Căn chỉnh: Không căn / Chỉnh tay", "Không căn: trùng góc trên-trái. Chỉnh tay: Alt + mũi tên dịch ảnh B 1 px, Alt+Shift + mũi tên dịch 10 px.", "Alt+←↑→↓", "Alt+Shift+←↑→↓"),
            new("Ngưỡng", "Chênh lệch màu tối thiểu để tính là khác (0–50%, mặc định 8%). Ảnh PNG chụp màn hình: 5–10%. Ảnh JPG: khoảng 20% (nhiễu nén quanh chữ hay bị tính là khác)."),
            new("Bỏ qua răng cưa", "Không tính khác biệt chỉ do viền chữ / hình vẽ mịn lệch nhẹ. Ảnh bị dịch lẻ pixel thì vẫn còn nhiễu - tăng ngưỡng."),
            new("Vùng bỏ qua", "Bật nút rồi kéo chuột trái trên ảnh để khoanh vùng không so (đồng hồ, ngày giờ, avatar…). Bấm vào 1 vùng bỏ qua để xoá; nút 🗑 xoá hết. Áp dụng cả cho So chữ; không dùng được khi Căn theo dòng."),
            new("Phần không chồng nhau", "2 ảnh khác kích thước / bị dịch: phần chỉ có ở 1 ảnh tô sọc - xanh = chỉ có ở A, cam = chỉ có ở B."),
            new("Gần như cả ảnh khác", "1 vùng khác phủ ≥ 60% ảnh thường là 2 ảnh lệch bố cục (khác font / trình duyệt) - bảng kết quả gợi ý thử So chữ."),
            new("Xuất kết quả", "Copy / Lưu PNG: ảnh khác biệt 1:1 có khung đánh số. Xuất báo cáo HTML: 1 file tự chứa (kết luận, % giống, bảng từng vùng kèm ảnh cắt A | B, 2 ảnh gốc) - mở bằng trình duyệt ở máy nào cũng được."),
        ]),

        new("Cạnh nhau & Chồng mờ", "", "Cạnh nhau, Chồng mờ, Thanh trượt: 3 cách đặt 2 ảnh cạnh nhau / chồng lên nhau để tự soi bằng mắt.",
        [
            new("Cạnh nhau", "A bên trái, B bên phải - zoom và cuộn đồng bộ."),
            new("Chồng mờ", "B đặt chồng lên A; thanh trượt chỉnh độ trong suốt A ↔ B."),
            new("Thanh trượt", "Vạch chia kéo được (hoặc bấm vào ảnh để đặt vạch): bên trái là A, bên phải là B."),
        ]),

        new("Tìm ảnh con", "", "Tìm 1 ảnh nhỏ (nút, icon cắt ra) trong ảnh lớn hơn.",
        [
            new("Cách dùng", "Đặt ảnh lớn và ảnh nhỏ vào A / B (thứ tự nào cũng được). Chỗ tìm thấy có khung xanh + % khớp; danh sách bên phải xếp khớp nhất trước - bấm để phóng tới."),
            new("Độ khớp tối thiểu", "Thanh trượt, mặc định 90%. Giảm khi ảnh con hơi khác (nén JPG, khác nền) mà không tìm thấy."),
        ]),

        new("Tìm chữ", "", "Đọc chữ trong 1 ảnh (OCR tiếng Việt / tiếng Anh) và tìm chữ trong đó - chỉ cần 1 ảnh.",
        [
            new("Cách dùng", "Chọn Ảnh A hoặc Ảnh B. Ô tìm trống: liệt kê mọi dòng đọc được (khung xanh mảnh). Gõ chữ: các chỗ khớp tô vàng + số, danh sách bên phải - bấm để phóng tới.", "Ctrl+F"),
            new("Cách so khớp", "Mặc định không phân biệt hoa / thường và dấu (\"thanh toan\" khớp \"Thanh toán\"); bật Phân biệt dấu / Phân biệt hoa/thường khi cần. Khoảng trắng không tính (OCR hay dính từ)."),
            new("Copy toàn bộ chữ", "Copy chữ đọc được của cả ảnh, mỗi dòng 1 dòng."),
            new("Lưu ý", "Chữ đọc từ ảnh có thể sai vài ký tự - không thấy thì thử tìm đoạn ngắn hơn. Chạy offline (Tesseract), không gửi ảnh đi đâu. 1 màn hình mất ~0,5 giây; mỗi ảnh chỉ đọc 1 lần."),
        ]),

        new("So chữ", "", "Đọc chữ cả 2 ảnh và liệt kê chữ / giá trị khác nhau - cho 2 ảnh cùng 1 màn hình chụp ở môi trường khác (font, trình duyệt) mà so pixel tô đỏ gần hết.",
        [
            new("Cách đọc kết quả", "A trái, B phải; khung màu quanh chỗ khác: đỏ = đổi chữ, xanh = chỉ có ở A, cam = chỉ có ở B, tím = khác màu chữ (vd ô bị khoá chữ xám ↔ chữ đen). Danh sách bên phải dạng 「A」→「B」 - bấm để phóng tới chỗ đó trên cả 2 ảnh."),
            new("Ngôn ngữ", "Chọn Tiếng Nhật (mặc định) hoặc Tiếng Việt / English theo chữ trên màn hình."),
            new("Gần giống", "Ẩn mặc định (tick Hiện gần giống để xem): lệch ít ký tự mà chữ số giống hệt - thường do OCR đọc lệch. Chữ số khác (giá trị, ngày, số tiền) luôn báo là đổi chữ."),
            new("Kiểm tra lại từng chỗ", "Sau khi so, mỗi chỗ nghi khác được cắt riêng và đọc lại vài cách; đọc lại ra giống thì bỏ khỏi danh sách. Bảng kết quả ghi số chỗ đã bỏ."),
            new("Xuất kết quả", "Copy (cả danh sách, dán thẳng vào Excel), Lưu CSV (mở bằng Excel), Xuất báo cáo HTML (bảng các chỗ khác kèm ảnh cắt A | B). Chỉ xuất các mục đang hiện."),
            new("Chuột phải danh sách", "Copy mục này / Copy cả danh sách (dùng được ở mọi chế độ có danh sách).", "Chuột phải"),
            new("Chữ đọc có thể sai", "Danh sách là \"các chỗ cần soi lại\", không phải kết luận cuối cùng. Thanh tiêu đề / URL trình duyệt hay bị báo - khoanh Vùng bỏ qua ở chế độ Khác biệt để bỏ."),
            new("Cài OCR tiếng Nhật", "Máy chưa có gói OCR tiếng Nhật của Windows thì app dùng Tesseract (kém hơn - bảng kết quả ghi rõ). Cài: Settings → Time & language → Language & region → thêm 日本語 (Japanese) → Language options → Optical character recognition (cần mạng)."),
        ]),

        new("Xem ảnh: zoom & cuộn", "", "Thao tác chung ở mọi chế độ xem.",
        [
            new("Zoom", "Ctrl + lăn chuột: zoom quanh con trỏ. Vừa cửa sổ: Ctrl+0. Kích thước thật 100%: Ctrl+1. Từ 200% trở lên ảnh vẽ không làm mịn để thấy rõ từng pixel.", "Ctrl+lăn", "Ctrl+0", "Ctrl+1"),
            new("Cuộn", "Lăn chuột: cuộn dọc; Shift + lăn: cuộn ngang; kéo chuột phải / giữa (hoặc chuột trái ở chế độ không dùng chuột trái) để kéo ảnh.", "Lăn", "Shift+lăn"),
            new("Màu pixel", "Thanh trạng thái hiện toạ độ và màu pixel ở A và B dưới con trỏ."),
        ]),

        new("Phím tắt", "", "Tổng hợp phím tắt của ImageCompare.",
        [
            new("Dán ảnh (vào ô trống / thay B)", "", "Ctrl+V"),
            new("Dán ảnh vào A", "", "Ctrl+Shift+V"),
            new("Tìm chữ", "", "Ctrl+F"),
            new("Vừa cửa sổ / 100%", "", "Ctrl+0", "Ctrl+1"),
            new("Dịch ảnh B 1 px / 10 px (Khác biệt)", "", "Alt+←↑→↓", "Alt+Shift+←↑→↓"),
            new("Hướng dẫn", "", "F1"),
        ]),
    ];
}
