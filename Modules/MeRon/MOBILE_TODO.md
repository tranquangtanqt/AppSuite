# App điện thoại me-ron: các điểm nên cải thiện

Ghi lại từ lúc port sang MeRon (máy tính), 2026-10-12 → 14. Mục A–E: bản máy tính đã tránh / sửa các điểm này; mục F:
chức năng bản máy tính đã có mà điện thoại chưa có (thêm điểm 23–25 ngày 2026-10-15); mục G: chức năng mới cho điện thoại. Mỗi mục ghi:
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

## F. Chức năng bản máy tính đã có, nên thêm cho điện thoại

Thêm 2026-10-14. Các chức năng này làm sau khi port, điện thoại chưa có. Không ghi các tính năng chỉ hợp với máy tính
(phím tắt, gõ ngày bằng bàn phím).

18. **Sao chép đơn / Đặt lại** cho khách quen đặt lặp lại. Mở form Thêm đặt hàng điền sẵn khách, món + số lượng, món tự
    do, giảm giá, ghi chú. Đơn mới luôn là Đã lên đơn, chưa thanh toán, tính giá hiện tại (báo khi giá đổi so với đơn
    gốc; món không còn trong danh sách → món tự do). Ngày giao: Sao chép giữ ngày của đơn gốc, Đặt lại thì giao hôm nay.
    → Trên điện thoại: nhấn giữ thẻ đơn → "Sao chép đơn". (bản máy tính: `OrderCopier` trong `Core\Orders\OrderDraft.cs`)
19. **Ô chọn món gõ để lọc** thay danh sách thả xuống (`DropdownButtonFormField` trong `order_item_row.dart`): gõ không
    dấu, các từ không cần liền nhau ("bao xiu" → Bánh bao xá xíu/trứng sữa), món có tên bắt đầu bằng chữ gõ lên trước;
    bắt buộc chọn 1 món trong danh sách. Danh sách món dài thì cuộn dropdown rất mất thời gian.
    → Dùng `Autocomplete` như `app_user_autocomplete.dart` đang làm cho khách, lọc bằng hàm bỏ dấu (xem mục 13).
20. **Khách lâu không đặt** (trang mới trong Thống kê): khách đã đặt ≥ 2 đơn mà đơn gần nhất (ngày giao, bỏ đơn Huỷ)
    cách hôm nay ≥ 30 ngày. Mỗi khách hiện:
    - lần đặt cuối + số ngày;
    - thói quen: trung bình bao nhiêu ngày đặt 1 lần, cần ≥ 3 ngày đặt; nghỉ gấp ≥ 2 lần thói quen thì có nhãn cảnh báo;
    - số đơn, tổng tiền, 3 món hay đặt.

    Có nút Đặt lại (mục 18) và nút chép tin nhắn hỏi thăm soạn sẵn. Tin nhắn gọi theo tên khách (bỏ mã phòng như
    "908", "W3"; tên viết hoa hết thì đổi thành chữ hoa đầu từ) và nhắc món hay đặt nhất.
    → Trên điện thoại nên mở thẳng Zalo / màn chia sẻ (`share_plus`) thay vì chép, và gọi điện bằng `tel:`.
    (bản máy tính: `Core\Reports\LapsedCustomers.cs`)
21. **Giữ bộ lọc khi chuyển trang**: điện thoại vào lại trang từ menu thì bộ lọc về mặc định (Đã lên đơn, hôm nay).
    Bản máy tính giữ trạng thái, khách, khoảng ngày, sắp xếp đến khi tắt app; mặc định chỉ áp ở lần đầu mở trang.
    → Giữ `OrderFilterState` cả phiên (không `autoDispose`, không reset trong `initState` khi vào từ menu).

## G. Chức năng mới cho điện thoại

Thêm 2026-10-15.

22. **Nhập cả bộ backup từ Google Drive một lần.** Hiện màn Nhập dữ liệu (`import_data_screen.dart`) bắt chọn từng file
    .tsv cho từng bảng bằng `FilePicker`. Muốn lấy bộ backup bản máy tính vừa "Xuất lên Google Drive" thì phải tải từng
    file về máy rồi chọn lại từng bảng.
    → Thêm nút "Nhập từ Google Drive":
    - đăng nhập Google, scope `drive.file`, dùng lại phần đăng nhập của `drive_upload_service.dart`;
    - liệt kê các folder backup `yyyyMMdd_HHmmss`, mới nhất lên đầu, kèm số file .tsv;
    - chọn 1 folder → tải các file .tsv về thư mục tạm → kiểm tra đủ bảng → hỏi xác nhận (dữ liệu hiện có bị thay);
    - nhập lần lượt bằng `_importTsvToDatabase` sẵn có: bảng chính (khách, món…) trước, đơn hàng sau;
    - báo kết quả từng bảng; bảng nào lỗi thì nói rõ.

    Giới hạn: `drive.file` chỉ thấy file / folder do app trong project Google Cloud `me-ron` tạo **bằng chính tài khoản
    đang đăng nhập**. Folder tự tạo hoặc file tự kéo lên trang web Drive thì app không thấy.
    **Thử thật 2026-10-15 (bản máy tính):** folder backup chung có bản của nhiều tài khoản (phanphuong…, Saku Ra, chủ
    folder) → đăng nhập chỉ thấy 1 / 39 bản. Muốn thấy hết phải xin quyền đọc cả Drive (`drive.readonly`, loại
    "restricted": app để chế độ Testing thì dùng được nhưng hiện cảnh báo "app chưa xác minh"), hoặc đọc folder chung đang
    công khai. Bản máy tính hiện làm cả 2: đăng nhập (thấy bản của mình) + dán link folder công khai (thấy hết, chọn bản bất
    kỳ, không chỉ bản mới nhất) - xem `Core\Cloud\DriveBackupBrowser.cs`, `Core\Import\GoogleDriveBackupSource.cs`.

## F (tiếp). Thêm 2026-10-15 - bản máy tính đã có, điện thoại chưa có

23. **Cảnh báo trước khi nhập đè mất dữ liệu chưa xuất.** Nhập backup là thay toàn bộ bảng: sửa trên máy này mà chưa
    xuất / sao lưu thì mất, không có cảnh báo. Điện thoại cũng vậy (vd nhập bộ xuất từ máy tính đè lên đơn mới ghi trên
    điện thoại).
    → Bản máy tính (`Core\Data\MeRonDatabase.cs`): trigger SQLite AFTER INSERT / UPDATE / DELETE trên 8 bảng tăng bộ đếm
    trong bảng meta (kèm lần sửa đầu / cuối); trong transaction nhập thì tắt; xuất / nhập / xoá toàn bộ thì về 0. Màn Nhập
    hiện cảnh báo + nút sang màn Xuất; hộp xác nhận ghi rõ "sẽ mất N thay đổi", nút chọn sẵn là Huỷ.
    Trên điện thoại (sqflite) làm y hệt bằng trigger, hoặc ghi mốc thời gian sửa cuối ở các hàm insert / update / delete.
24. **Chọn bản backup bất kỳ trong thư mục cha / folder Drive**, không chỉ bản mới nhất: liệt kê hết các bản
    `yyyyMMdd_HHmmss` (mới nhất trước), bấm 1 bản để kiểm tra + xem trước. Hợp khi bản mới nhất lỗi / thiếu file, hoặc
    muốn quay lại 1 bản cũ.
25. **Số liệu thêm ở Thống kê** (nhỏ, tuỳ chọn):
    - Theo đơn hàng: ô **TB mỗi đơn** (tổng tiền ÷ số đơn), mỗi trạng thái có **% số đơn**;
    - Theo món ăn: **Tổng thành tiền**, **Số món khác nhau**;
    - Tổng hợp: ô **Chênh lệch** đổi màu - xanh khi lãi, đỏ khi lỗ;
    - bảng món có thanh dài ngắn theo số lượng (nhìn nhanh món bán chạy).

    Giao diện thẻ đơn bản máy tính (2026-10-15) khác điện thoại: thêm vạch màu trạng thái bên trái + nhãn trạng thái tròn;
    Đã thanh toán nền xanh, Đã lên đơn nền trắng (như điện thoại), **Huỷ nền xám + chữ mờ** (điện thoại: nền đỏ) - người
    dùng chọn kiểu này vì nhìn lướt dễ phân biệt hơn.
