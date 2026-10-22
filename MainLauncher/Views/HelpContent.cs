using System.Collections.Generic;
using SharedUI.Help;

namespace MainLauncher.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (mục "Hướng dẫn (F1)" ở menu trái / phím F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi
/// tính năng thì cập nhật cả 2 chỗ.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "MainLauncher là màn hình chính của AppSuite: xem các công cụ (module), mở / đóng chúng và theo dõi nhật ký.",
        [
            new("Mở 1 module", "Trang Tổng quan hoặc Danh sách module → bấm Chạy trên thẻ của module. Chấm màu góc trên phải chuyển xanh khi module đang chạy."),
            new("Đóng 1 module", "Bấm Dừng trên thẻ (hoặc đóng cửa sổ module như bình thường). Xem mục \"Chạy / Dừng / Chạy lại\" về việc lưu trước khi Dừng."),
            new("Chỉ hiện module hay dùng", "Nút \"Ẩn / hiện\" cạnh tiêu đề Module (trang Tổng quan) hoặc cạnh Tải lại (Danh sách module): bỏ tích các module không dùng."),
            new("Menu trái", "Tổng quan, Danh sách module, Nhật ký, Hướng dẫn (F1), Cài đặt. Nút ≡ trên cùng thu gọn / mở rộng menu."),
            new("Mở lại hướng dẫn này", "Mục \"Hướng dẫn (F1)\" ở menu trái, hoặc phím F1.", "F1"),
        ]),

        new("Tổng quan", "", "Trang mở đầu: số module, số đang chạy / đã dừng và lưới thẻ module.",
        [
            new("Thẻ thống kê", "Tổng số module / Đang chạy / Đã dừng - luôn tính mọi module trong modules.json (kể cả module đang ẩn). Dòng trên tiêu đề AppSuite ghi \"x/y module đang chạy\"."),
            new("Làm mới", "Kiểm tra lại module nào đang chạy và cập nhật số liệu (thường tự cập nhật khi Chạy / Dừng, không cần bấm)."),
            new("Lưới Module", "Mỗi module 1 thẻ: biểu tượng, tên, mô tả (dài thì rút gọn, rê chuột để xem đủ), trạng thái và 3 nút Chạy / Dừng / Chạy lại. Chỉ hiện module không bị ẩn."),
        ]),

        new("Danh sách module", "", "Danh sách module dạng lưới như trang Tổng quan, kèm nút tải lại cấu hình.",
        [
            new("Tải lại", "Đọc lại Config\\modules.json (vd vừa thêm / sửa module) và dựng lại danh sách. Không mở / đóng module nào - module đang chạy vẫn chạy và vẫn được nhận ra."),
            new("Ẩn / hiện", "Cùng lựa chọn với trang Tổng quan - ẩn ở đây thì trang Tổng quan cũng ẩn và ngược lại."),
        ]),

        new("Chạy / Dừng / Chạy lại", "", "3 nút trên mỗi thẻ module. Nút nào không dùng được ở trạng thái hiện tại thì mờ.",
        [
            new("Chạy", "Mở module (bấm được khi module đã dừng hoặc lỗi). Mỗi module chỉ chạy 1 bản từ MainLauncher."),
            new("Dừng", "Gửi lệnh đóng cửa sổ module như bấm nút X. Nếu sau 5 giây module chưa tắt (vd đang hiện hộp thoại hỏi lưu) thì module bị buộc tắt - phần chưa lưu sẽ mất. Nên lưu việc đang làm trong module trước khi bấm Dừng, hoặc tự đóng cửa sổ module."),
            new("Chạy lại", "Buộc tắt module ngay (không chờ, không hỏi lưu) rồi mở lại - dùng khi module bị treo hoặc vừa cập nhật bản mới. Module còn việc chưa lưu thì lưu trước, hoặc dùng Dừng rồi Chạy."),
            new("Trạng thái", "Đang chạy (chấm xanh, kèm số tiến trình); Đang mở / Đang đóng (cam); Lỗi (đỏ - vd không tìm thấy file .exe của module, xem Nhật ký); Đã dừng (xám)."),
            new("Tự đóng module", "Đóng cửa sổ module như bình thường thì MainLauncher tự nhận ra và chuyển thẻ về Đã dừng."),
            new("Khi đóng MainLauncher", "Còn module đang chạy thì MainLauncher hỏi \"Đóng cả các module đang chạy?\": Đóng hết (đóng từng module như bấm Dừng rồi mới thoát), Để chạy tiếp (module vẫn chạy, chỉ đóng MainLauncher) hoặc Huỷ (không đóng). Tích \"Nhớ lựa chọn\" để lần sau khỏi hỏi - đổi lại ở trang Cài đặt."),
            new("Mở lại MainLauncher", "Module vẫn đang chạy từ lần trước được nhận lại: thẻ hiện Đang chạy, bấm Dừng được, Chạy bị mờ (không mở bản thứ 2)."),
            new("Chỉ 1 MainLauncher", "MainLauncher đang chạy mà mở lần nữa (vd bấm lại lối tắt) thì không mở cửa sổ thứ 2: cửa sổ đang có hiện lên trước (kể cả đang thu nhỏ) kèm thông báo nhỏ \"AppSuite đang chạy\"."),
        ]),

        new("Ẩn / hiện module", "", "Chỉ hiện những module hay dùng ở trang Tổng quan và Danh sách module.",
        [
            new("Chọn module", "Bấm \"Ẩn / hiện\" → bỏ tích module muốn ẩn (có tác dụng ngay). Dòng bên cạnh ghi \"Đang hiện 10 / 12\" hoặc \"Hiện tất cả 12\"."),
            new("Hiện lại", "Tích lại module đó, hoặc bấm \"Hiện tất cả\". Trang Cài đặt luôn liệt kê đủ mọi module để bật lại."),
            new("Module ẩn đang chạy", "Module đang ẩn mà đang chạy vẫn hiện tạm để bấm Dừng được; đóng xong thì ẩn lại."),
            new("Lưu ở đâu", "Config\\launcher-settings.json cạnh MainLauncher.exe - tách khỏi modules.json nên cập nhật / cài lại AppSuite không mất lựa chọn. Mở lại MainLauncher vẫn giữ."),
            new("Ẩn hết", "Ẩn hết module thì trang hiện dòng nhắc bấm \"Ẩn / hiện\" để chọn lại."),
        ]),

        new("Nhật ký", "", "Nhật ký hoạt động của MainLauncher: nạp cấu hình, mở / đóng module, lỗi. Nội dung dòng nhật ký viết bằng tiếng Anh (để tra lỗi).",
        [
            new("Trang Nhật ký", "Dòng mới nhất ở trên cùng, giữ 500 dòng gần nhất, có cả các dòng từ lúc mở MainLauncher."),
            new("File nhật ký", "Mọi dòng cũng được ghi vào Logs\\launcher-yyyy-MM-dd.log cạnh MainLauncher.exe (mỗi ngày 1 file) - gửi file này khi cần báo lỗi."),
            new("Khi có lỗi", "Module không mở được (thẻ đỏ), trang không mở được, hoặc MainLauncher gặp lỗi bất ngờ đều được ghi vào nhật ký kèm chi tiết."),
        ]),

        new("Cài đặt và cấu hình", "", "Trang Cài đặt (cuối menu trái) và các file cấu hình trong thư mục Config cạnh MainLauncher.exe.",
        [
            new("Trang Cài đặt", "Thư mục cài đặt, File cấu hình module (modules.json), nút \"Tải lại modules.json\", \"Khi đóng MainLauncher mà còn module đang chạy\" và danh sách \"Module hiện trên màn hình\"."),
            new("Khi đóng MainLauncher mà còn module đang chạy", "Hỏi mỗi lần (mặc định) / Luôn đóng hết các module / Luôn để các module chạy tiếp. Đổi là lưu ngay."),
            new("Config\\modules.json", "Danh sách module: Name (tên), Description (mô tả trên thẻ), Path (đường dẫn file .exe, tương đối so với thư mục MainLauncher, vd .\\Modules\\ScreenCapture\\ScreenCapture.exe), Arguments (tham số khi mở), AutoStart (true = tự mở khi mở MainLauncher). Sửa xong bấm Tải lại."),
            new("Config\\appsettings.json", "ModulesConfigPath - vị trí file modules.json (mặc định Config/modules.json)."),
            new("Config\\launcher-settings.json", "Các module đang ẩn (nút Ẩn / hiện) và lựa chọn khi đóng MainLauncher - do app tự ghi, không cần sửa tay."),
        ]),
    ];
}
