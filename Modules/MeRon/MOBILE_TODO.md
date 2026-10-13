# App điện thoại me-ron: các điểm nên cải thiện

Ghi lại từ lúc port sang MeRon (máy tính), 2026-10-12 → 13. Bản máy tính đã tránh / sửa các điểm này. Mỗi mục ghi:
hiện tượng, hậu quả và cách sửa gợi ý. File Flutter nằm ở `D:\tantq\src\me-ron\lib`.

## A. Lỗi làm sai số liệu (nên sửa trước)

1. **Lọc theo ngày bỏ sót đơn.** Ngày giờ được lưu lẫn 2 dạng: `2026-10-11T14:20:53.242992` (`toIso8601String()`) và
   `2026-10-11 07:20:53` (`CURRENT_TIMESTAMP`). Phần lọc lại so chuỗi ngày giờ, nên bỏ sót các đơn dạng có dấu cách đúng
   ngày "Từ ngày". Sắp xếp theo ngày cũng sai thứ tự giữa 2 dạng.
   → Lọc bằng `substr(deliveryDatetime, 1, 10)` (hoặc `date(...)`), sắp theo `julianday(...)`. Lâu dài thì chỉ ghi 1 dạng.
   (`order_local_datasource_impl.dart`)
2. **Đơn Huỷ bị tính vào tổng**, trái với hướng dẫn của chính app: đơn Huỷ bị đếm vào "Tổng số đơn" (Theo đơn hàng)
   và báo cáo Tổng hợp cộng cả tiền đơn Huỷ.
   → Loại `status = Huỷ` khỏi mọi tổng; nếu cần thì hiện riêng 1 ô "Huỷ".
3. **Xoá đơn để lại dòng món mồ côi** (`OrderItems` vẫn còn), làm sai báo cáo theo món.
   → Xoá `OrderItems` theo `orderId` trong cùng transaction, hoặc bật khoá ngoại `ON DELETE CASCADE` (xem mục 7).
4. **Thanh toán toàn bộ đổi cả đơn Huỷ thành Đã thanh toán.** → Chỉ chuyển các đơn đang "Đã lên đơn".
5. **Sửa rồi Lưu một đơn đã Huỷ thì âm thầm bỏ trạng thái Huỷ.** → Giữ nguyên trạng thái khi lưu.
6. **Giá cũ áp theo productId thay vì theo từng dòng.** Đơn có 1 dòng món đã đổi giá (bị khoá giá cũ) mà thêm dòng mới
   cùng món thì dòng mới cũng bị tính giá cũ. → Khoá giá theo từng dòng (dòng cũ giữ đơn giá lúc đặt, dòng mới lấy giá
   hiện tại). (`order_form_notifier.dart`)

## B. Toàn vẹn dữ liệu

7. **SQLite không bật khoá ngoại** (`PRAGMA foreign_keys` tắt), nên xoá khách còn đơn hoặc xoá danh mục còn món sẽ để
   lại dữ liệu mồ côi: báo cáo hiện đơn của "khách không tên", món không có danh mục.
   → Chặn xoá khi còn dữ liệu liên quan (bản máy tính đang làm vậy), hoặc bật `foreign_keys = ON` + migration.
8. **Báo cáo theo món lấy `MIN(snapshotName)`**: đổi tên món thì tên cũ và tên mới lẫn lộn trong báo cáo.
   → Gộp theo `productId`, hiện tên món hiện tại; món tự do gộp theo tên.

## C. Nhập / xuất backup (TSV)

9. **App không nhập được file do chính nó xuất ra khi bảng trống.** Bảng trống được xuất thành file rỗng 0 byte,
   trong khi màn Nhập báo lỗi "File rỗng". → Khi xuất thì luôn ghi dòng tiêu đề; khi nhập thì coi file chỉ có tiêu đề
   (hoặc rỗng) là bảng trống.
10. **Màn Nhập đọc bằng `readAsLines`**, hàm này tách dòng cả ở `\r`, nên ghi chú có xuống dòng kiểu Windows (`\r\n`
    trong ô) làm lệch dòng. → Xuất thì thay `\r`/`\n` trong ô bằng dấu cách hoặc escape; nhập thì chỉ tách dòng ở `\n`.
    (`import_data_screen.dart`)
11. **Nhập từng bảng riêng lẻ, xoá bảng trước rồi mới nhập.** Có 2 rủi ro:
    - nhập lệch bộ (đơn mới + khách cũ) làm hỏng liên kết;
    - lỗi giữa chừng thì bảng đã bị xoá mà dữ liệu mới chưa vào.

    → Kiểm tra cả bộ trước khi ghi, nhập trong 1 transaction (lỗi thì rollback), và tự sao lưu database trước khi nhập.
12. **Không có bản sao an toàn trước khi "Xoá toàn bộ" hay "Nhập".** → Chép file `.db` ra thư mục sao lưu trước, để
    lấy lại được khi lỡ tay.

## D. Tìm kiếm / tiện dụng

13. **Tìm kiếm bằng `LIKE`** chỉ bỏ qua hoa thường với chữ ASCII, nên gõ "huyen" không ra "Huyền" và "HUYỀN" không ra
    "huyền". → Thêm cột tên đã bỏ dấu (chữ thường, không dấu) để tìm, hoặc lọc trong Dart sau khi bỏ dấu cả hai vế.
14. **Danh sách đơn phân trang "Xem thêm"**: tổng "N đơn · Tổng X" và Chi tiết chỉ đúng khi đã tải hết. Đo trên máy
    tính, 2.617 đơn tải trong 20 ms. → Tính tổng bằng SQL (`COUNT`/`SUM`) thay vì cộng các đơn đã tải; nhẹ hơn nữa thì
    bỏ phân trang.
15. **Báo cáo Tổng hợp tải hết đơn rồi cộng trong Dart.** → Dùng truy vấn tổng hợp SQL (`GROUP BY`): nhanh hơn và
    không phụ thuộc phân trang.
16. **Trùng tên khách** so bằng `toLowerCase`, vẫn phân biệt dấu ("Huyen" và "Huyền" được coi là 2 người khác nhau).
    Nên cân nhắc so sánh sau khi bỏ dấu và cảnh báo (không chặn).

## E. Bảo mật

17. **Folder Google Drive chứa backup đang để "ai có link cũng xem được"**
    (id `1c0cznqHuK3hCUdLC5LRtSJoSGb1qM_qT`). Folder này có tên, số điện thoại, địa chỉ của khách.
    → Giới hạn quyền folder; app điện thoại và máy tính đọc qua đăng nhập Google (OAuth, scope `drive.file`).
