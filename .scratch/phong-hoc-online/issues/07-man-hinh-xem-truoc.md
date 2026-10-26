# 07: Màn hình xem trước trước khi vào phòng

**What to build:** Trước khi vào một Buổi học, mọi người, kể cả Chủ trì, đi qua Màn hình xem trước: khung xem trước camera, thanh đo mức mic theo thời gian thực, ô chọn camera và mic khi máy có nhiều thiết bị, và nút bật hoặc tắt camera và mic trước khi vào. Chủ trì mặc định bật cả hai, học viên mặc định tắt cả hai như hiện nay. Thiết bị đã chọn được mang vào phòng. Nếu trình duyệt từ chối quyền camera hoặc mic thì vẫn cho vào ở chế độ chỉ nghe, kèm giải thích rõ. Buổi học chỉ chính thức bắt đầu khi Chủ trì rời màn hình này để vào phòng. Học viên đến sớm thấy "Chủ trì chưa bắt đầu", trang tự kiểm tra lại sau mỗi vài giây, và nút Tham gia chỉ sáng khi Buổi học đang diễn ra. Với liên kết mời có mã, màn hình này xác minh mã và trạng thái Buổi học trước khi hiện nút Tham gia.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] Mở một Buổi học thì hiện Màn hình xem trước trước khi mở kết nối thời gian thực; Buổi học chưa bắt đầu cho tới khi Chủ trì vào phòng.
- [x] Khung xem trước camera và thanh đo mic phản hồi theo thiết bị thật; chọn thiết bị khác thì xem trước đổi theo.
- [x] Mặc định theo vai trò (Chủ trì bật, học viên tắt) và nút bật tắt hoạt động; lựa chọn này được mang vào phòng, không phải chọn lại.
- [x] Từ chối quyền camera hoặc mic: vẫn vào được ở chế độ chỉ nghe, có thông báo giải thích, không treo và không lỗi.
- [x] Máy không có camera hoặc mic nào cũng không làm hỏng màn hình.
- [x] Học viên vào sớm thấy trạng thái chờ, trang tự kiểm tra lại, và nút Tham gia sáng lên đúng lúc Buổi học bắt đầu; mã sai, Buổi đã kết thúc hoặc đã hủy được báo ngay ở màn hình này.
- [x] Các luồng camera và mic dùng để xem trước được giải phóng đúng khi vào phòng, không bị giữ hai lần.
- [x] Đi được bằng bàn phím; thanh đo mic có chữ mô tả cho trình đọc màn hình; theme sáng và tối; dùng được trên điện thoại.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** Kiểm bằng hai trình duyệt: chủ trì ở màn hình xem trước thì buổi vẫn "Scheduled" tới khi bấm vào; học viên chờ rồi tự được mở nút khi buổi bắt đầu; mã sai bị chặn; mic bật ở bước xem trước được mang vào phòng; bị từ chối quyền thì vẫn vào được ở chế độ chỉ nghe. Phát hiện và sửa một lỗi: phím Tab "rò" ra khung chat phía sau (nay khóa bằng `inert`). **Chưa thử với camera/micro thật**: toàn bộ kiểm tra dùng thiết bị giả bằng canvas và bộ dao động âm thanh. Thanh đo mic cần một thao tác của người dùng để trình duyệt cho phép âm thanh chạy; đã gắn sẵn bước đánh thức ở lần chạm/bấm phím đầu tiên.
