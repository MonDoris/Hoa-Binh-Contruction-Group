# 04: Trang xem lại mới

**What to build:** Mở một Buổi học trong Lịch sử dẫn tới trang xem lại mới: trình phát video lớn, danh sách các Bản ghi để chọn từng phần (mỗi Bản ghi có thời lượng và dung lượng), thống kê tóm tắt của buổi (thời lượng, số Người tham gia, số tin nhắn chat) và Diễn biến lọc được theo loại (chat, ra và vào, mic và camera, ghi hình). Chủ trì và Admin có nút tải xuống Bản ghi. Buổi không có Bản ghi vẫn hiện tóm tắt và Diễn biến kèm ghi chú rõ ràng. Người không có quyền xem thấy thông báo không có quyền chứ không phải một trang hỏng. Bản ghi cũ chưa có thời lượng lưu sẵn lấy thời lượng từ chính video.

**Blocked by:** 03 (quy tắc quyền xem Lịch sử)

**Status:** ready-for-agent

- [x] Một truy vấn xem lại trả về tóm tắt, danh sách Bản ghi (kèm thời lượng và dung lượng) và Diễn biến cho người có quyền; người không có quyền nhận 403 (có test).
- [x] Số tin nhắn và số Người tham gia trong tóm tắt đúng (có test).
- [x] Trình phát phát được từng Bản ghi; chọn Bản ghi khác thì đổi video.
- [x] Bộ lọc Diễn biến theo loại hoạt động và có thể bỏ lọc.
- [x] Nút tải xuống chỉ hiện với Chủ trì và Admin.
- [x] Buổi không có Bản ghi và Bản ghi cũ không có thời lượng đều hiển thị tử tế, không lỗi.
- [x] Người không có quyền thấy thông báo "bạn không có quyền xem buổi này" kèm đường quay về Lịch sử.
- [x] Bố cục dùng được ở 390px, 768px và 1280px, theme sáng và tối, điều hướng được bằng bàn phím.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** Truy vấn xem lại có test (quyền, số liệu, tải xuống, buổi không có Bản ghi, Bản ghi cũ không có thời lượng). Trang kiểm ở 1280/768/390 px, nền sáng và tối, không tràn ngang. Đồng hồ ở trang này làm tròn xuống giây nên khớp với đồng hồ của trình phát (trước đây hiện 00:11 trong khi trình phát hiện 0:10). Điều hướng bàn phím mới kiểm bằng cách đọc CSS và tên truy cập, chưa dùng trình đọc màn hình thật.
