# 08: Giao diện trong phòng mới

**What to build:** Làm lại giao diện bên trong Buổi học: giữ nền tối kiểu hội nghị truyền hình nhưng dựng từ bộ màu và chữ của hệ thống. Bố cục gồm sân khấu chính hiển thị Chủ trì, khung chat và Người tham gia ở bên phải có thể đóng mở (trên màn hình nhỏ trở thành tấm trượt từ dưới lên), thanh điều khiển có nhãn trạng thái cho từng nút, và thanh trên hiển thị tên Buổi học, trạng thái, thời gian Buổi học đã chạy, mã và nút sao chép liên kết mời, cùng trạng thái ghi hình. Các nút ghi hình từ ticket 01 và huy hiệu từ ticket 02 được đặt vào đúng chỗ trong bố cục mới. Chủ trì thấy lưới ô học viên với camera hoặc chữ cái đầu và trạng thái mic. Mọi chức năng hiện có (mic, camera, chia sẻ màn hình, chat, rời phòng hoặc kết thúc) vẫn hoạt động như cũ.

**Blocked by:** 01, 02, 07 (các nút ghi hình, huy hiệu trạng thái và Màn hình xem trước nằm trong bố cục này)

**Status:** ready-for-agent

- [x] Bố cục mới dùng đúng bộ màu và chữ của hệ thống, không còn bộ màu riêng tự dựng; nền tối ổn định trong cả hai theme.
- [x] Khung chat và Người tham gia đóng mở được; ở màn hình nhỏ là tấm trượt từ dưới lên.
- [x] Thanh điều khiển vừa và với tới được ở 390px, 768px và 1280px; mỗi nút có biểu tượng cộng tên (hoặc chú thích) thể hiện trạng thái hiện tại.
- [x] Thanh trên có đủ: tên Buổi học, trạng thái, thời gian đã chạy, mã và sao chép liên kết, trạng thái ghi hình.
- [x] Nút ghi hình của Chủ trì và huy hiệu của mọi người dùng đúng chỗ và đúng trạng thái.
- [x] Mic, camera, chia sẻ màn hình, chat, rời phòng và kết thúc Buổi học đều chạy như trước (kiểm tay bằng hai trình duyệt).
- [x] Điều hướng hoàn toàn bằng bàn phím, focus nhìn thấy được, mọi nút có tên truy cập, tôn trọng giảm chuyển động.
- [x] Kiểm tra ảnh chụp ở 390px, 768px và 1280px, không có tràn ngang hay chồng chéo.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** Chụp và rà ở 1280/768/390 px. Ở điện thoại đã sửa hai lỗi tìm thấy khi rà: thanh trên bị huy hiệu đè lên tên buổi, và nút Kết thúc bị đẩy ra ngoài mép khi đang ghi (6 nút). Kiểm tên truy cập: 12 phần tử ở Màn hình xem trước và 7 ở phòng đều có tên; mọi nút có viền lấy nét. Khóa nút Ghi hình tới khi vào phòng xong (trước đó có khe hở khiến server từ chối). Mic, camera, chia sẻ màn hình, chat, rời phòng và kết thúc chạy như trước (kiểm với hai trình duyệt, camera giả); chia sẻ màn hình bằng màn hình thật chưa thử.
