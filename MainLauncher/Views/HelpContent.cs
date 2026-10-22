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
            new("Mở 1 module", "Dashboard hoặc Module List → bấm Start trên thẻ của module. Chấm màu góc trên phải chuyển xanh khi module đang chạy."),
            new("Đóng 1 module", "Bấm Stop trên thẻ (hoặc đóng cửa sổ module như bình thường). Xem mục \"Start / Stop / Restart\" về việc lưu trước khi Stop."),
            new("Chỉ hiện module hay dùng", "Nút \"Ẩn / hiện\" cạnh tiêu đề Modules (Dashboard) hoặc cạnh Reload (Module List): bỏ tích các module không dùng."),
            new("Menu trái", "Dashboard (tổng quan), Module List (danh sách module), Logs (nhật ký), Hướng dẫn (F1), Settings (cài đặt). Nút ≡ trên cùng thu gọn / mở rộng menu."),
            new("Mở lại hướng dẫn này", "Mục \"Hướng dẫn (F1)\" ở menu trái, hoặc phím F1.", "F1"),
        ]),

        new("Dashboard", "", "Trang mở đầu: số module, số đang chạy / đã dừng và lưới thẻ module.",
        [
            new("Thẻ thống kê", "Total Modules / Running / Stopped - luôn tính mọi module trong modules.json (kể cả module đang ẩn). Dòng trên tiêu đề AppSuite ghi \"x/y module dang chay\"."),
            new("Refresh", "Kiểm tra lại module nào đang chạy và cập nhật số liệu (thường tự cập nhật khi Start / Stop, không cần bấm)."),
            new("Lưới Modules", "Mỗi module 1 thẻ: biểu tượng, tên, mô tả (dài thì rút gọn, rê chuột để xem đủ), trạng thái và 3 nút Start / Stop / Restart. Chỉ hiện module không bị ẩn."),
        ]),

        new("Module List", "", "Danh sách module dạng lưới như Dashboard, kèm nút tải lại cấu hình.",
        [
            new("Reload", "Đọc lại Config\\modules.json (vd vừa thêm / sửa module) và dựng lại danh sách. Không mở / đóng module nào - module đang chạy vẫn chạy."),
            new("Ẩn / hiện", "Cùng lựa chọn với Dashboard - ẩn ở đây thì Dashboard cũng ẩn và ngược lại."),
        ]),

        new("Start / Stop / Restart", "", "3 nút trên mỗi thẻ module. Nút nào không dùng được ở trạng thái hiện tại thì mờ.",
        [
            new("Start", "Mở module (bấm được khi module đang dừng hoặc lỗi). Mỗi module chỉ chạy 1 bản từ MainLauncher."),
            new("Stop", "Gửi lệnh đóng cửa sổ module như bấm nút X. Nếu sau 5 giây module chưa tắt (vd đang hiện hộp thoại hỏi lưu) thì module bị buộc tắt - phần chưa lưu sẽ mất. Nên lưu việc đang làm trong module trước khi bấm Stop, hoặc tự đóng cửa sổ module."),
            new("Restart", "Buộc tắt module ngay (không chờ, không hỏi lưu) rồi mở lại - dùng khi module bị treo hoặc vừa cập nhật bản mới. Module còn việc chưa lưu thì lưu trước, hoặc dùng Stop rồi Start."),
            new("Màu trạng thái", "Xanh = đang chạy; cam = đang mở / đang đóng; đỏ = lỗi (vd không tìm thấy file .exe của module - xem Logs); xám = đã dừng."),
            new("Tự đóng module", "Đóng cửa sổ module như bình thường thì MainLauncher tự nhận ra và chuyển thẻ về Stopped."),
            new("Khi đóng MainLauncher", "Các module đã mở vẫn chạy tiếp, không bị đóng theo."),
        ]),

        new("Ẩn / hiện module", "", "Chỉ hiện những module hay dùng ở Dashboard và Module List.",
        [
            new("Chọn module", "Bấm \"Ẩn / hiện\" → bỏ tích module muốn ẩn (có tác dụng ngay). Dòng bên cạnh ghi \"Đang hiện 10 / 12\" hoặc \"Hiện tất cả 12\"."),
            new("Hiện lại", "Tích lại module đó, hoặc bấm \"Hiện tất cả\". Trang Settings luôn liệt kê đủ mọi module để bật lại."),
            new("Module ẩn đang chạy", "Module đang ẩn mà đang chạy vẫn hiện tạm để bấm Stop được; đóng xong thì ẩn lại."),
            new("Lưu ở đâu", "Config\\launcher-settings.json cạnh MainLauncher.exe - tách khỏi modules.json nên cập nhật / cài lại AppSuite không mất lựa chọn. Mở lại MainLauncher vẫn giữ."),
            new("Ẩn hết", "Ẩn hết module thì trang hiện dòng nhắc bấm \"Ẩn / hiện\" để chọn lại."),
        ]),

        new("Logs", "", "Nhật ký hoạt động của MainLauncher: nạp cấu hình, mở / đóng module, lỗi.",
        [
            new("Trang Logs", "Dòng mới nhất ở trên cùng, giữ 500 dòng gần nhất, có cả các dòng từ lúc mở MainLauncher."),
            new("File nhật ký", "Mọi dòng cũng được ghi vào Logs\\launcher-yyyy-MM-dd.log cạnh MainLauncher.exe (mỗi ngày 1 file) - gửi file này khi cần báo lỗi."),
            new("Khi có lỗi", "Module không mở được (thẻ đỏ), trang không mở được, hoặc MainLauncher gặp lỗi bất ngờ đều được ghi vào nhật ký kèm chi tiết."),
        ]),

        new("Settings và cấu hình", "", "Trang Settings (cuối menu trái) và các file cấu hình trong thư mục Config cạnh MainLauncher.exe.",
        [
            new("Trang Settings", "Thư mục cài đặt (Base directory), đường dẫn file modules.json, nút \"Reload modules.json\" và danh sách \"Module hiện trên màn hình\"."),
            new("Config\\modules.json", "Danh sách module: Name (tên), Description (mô tả trên thẻ), Path (đường dẫn file .exe, tương đối so với thư mục MainLauncher, vd .\\Modules\\ScreenCapture\\ScreenCapture.exe), Arguments (tham số khi mở), AutoStart (true = tự mở khi mở MainLauncher). Sửa xong bấm Reload."),
            new("Config\\appsettings.json", "ModulesConfigPath - vị trí file modules.json (mặc định Config/modules.json)."),
            new("Config\\launcher-settings.json", "Các module đang ẩn (do nút Ẩn / hiện ghi) - không cần sửa tay."),
        ]),
    ];
}
