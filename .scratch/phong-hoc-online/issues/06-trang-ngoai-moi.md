# 06: Trang ngoài mới (Đang diễn ra, Sắp diễn ra, Lịch sử)

**What to build:** Trang Phòng học online được thiết kế lại: dải Đang diễn ra nổi bật ở đầu trang với nút Vào phòng; phần Sắp diễn ra dạng thẻ có đếm ngược tới giờ bắt đầu cùng các thao tác Bắt đầu, Sửa, Hủy và Sao chép liên kết mời; và tab Lịch sử từ ticket 03 được gộp vào bố cục mới. Hộp nhập mã 6 ký tự được làm lại cho đẹp nhưng giữ nguyên cách hoạt động. Hộp tạo Buổi học giữ nguyên chức năng, kể cả "Bắt đầu ngay". Các màu vàng, cam, đỏ ghi cứng ở nút bị bỏ để dùng bộ màu của hệ thống. Ai thấy lịch các buổi chưa diễn ra giữ nguyên như hiện nay: mọi người thấy lịch, chỉ mã tham gia bị ẩn với người không phải Chủ trì hay Admin. Admin vẫn xóa được Buổi học như trước.

**Blocked by:** 03 (tab Lịch sử)

**Status:** ready-for-agent

- [x] Buổi đang diễn ra hiện ở dải riêng trên cùng; có nút Vào phòng đúng quyền (Chủ trì và Admin vào thẳng, người khác cần mã).
- [x] Thẻ Sắp diễn ra hiện đếm ngược tự cập nhật, và các nút Bắt đầu, Sửa, Hủy, Sao chép liên kết chỉ hiện đúng với người có quyền.
- [x] Tạo, sửa, hủy và "Bắt đầu ngay" hoạt động như trước.
- [x] Hộp nhập mã vẫn có 6 ô, tự nhảy ô, dán được, tự vào khi nhập đủ; có báo lỗi rõ khi sai hoặc thiếu.
- [x] Tab Lịch sử nằm trong bố cục mới và vẫn dùng đúng quy tắc quyền của ticket 03.
- [x] Không còn màu ghi cứng ở nút; theme sáng và tối đều đẹp.
- [x] Trạng thái trống và trạng thái lỗi cho từng phần.
- [x] Dùng được ở 390px, 768px và 1280px, điều hướng bằng bàn phím, focus nhìn thấy được, tôn trọng giảm chuyển động.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** Kiểm bằng trình duyệt với Chủ trì, học viên và Admin: quyền hiển thị nút đúng, đếm ngược (kể cả "đã quá giờ bắt đầu"), tạo → sửa → hủy → mục Đã hủy, "Bắt đầu ngay" đi qua Màn hình xem trước, hộp nhập mã báo lỗi rõ. Không còn màu ghi cứng ở nút; không tràn ngang ở 5 khổ/theme. Phần giảm chuyển động chỉ kiểm bằng cách đọc CSS.
