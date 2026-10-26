# 01: Chủ trì ghi hình với Tạm dừng, Tiếp tục và Dừng ghi

**What to build:** Chủ trì ghi hình một Buổi học và điều khiển được bằng ba thao tác: Tạm dừng, Tiếp tục và Dừng ghi. Chỉ Chủ trì được điều khiển ghi hình; server từ chối mọi yêu cầu khác, kể cả khi gọi thẳng vào dịch vụ. Tạm dừng giữ nguyên một Bản ghi và cắt đoạn tạm dừng khỏi video. Dừng ghi chốt Bản ghi; ghi hình lại sau đó tạo Bản ghi mới. Rời phòng hoặc kết thúc Buổi học khi đang ghi hay đang tạm dừng thì tự chốt Bản ghi. Mỗi lần chuyển trạng thái được ghi vào Diễn biến. Thời lượng thật của Bản ghi (không tính đoạn tạm dừng) được lưu lại.

Ticket này cũng dựng project kiểm thử đầu tiên của solution và đồng hồ ghi hình thuần (không I/O). Giao diện nút ghi hình ở đây chỉ cần đúng chức năng trên bố cục hiện tại; vị trí và kiểu dáng cuối cùng thuộc ticket 08. Việc hiển thị trạng thái cho những người khác trong phòng thuộc ticket 02.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] Solution có một project xUnit cho tầng Application, chạy xanh trên SQLite trong bộ nhớ với một người dùng hiện tại giả.
- [x] Chỉ Chủ trì bắt đầu được Bản ghi; yêu cầu của người khác bị từ chối (có test).
- [x] Đồng hồ ghi hình: Start, Pause, Resume, Stop; thời gian đã ghi không tính đoạn tạm dừng; các chuyển trạng thái không hợp lệ (tiếp tục khi chưa tạm dừng, tạm dừng khi chưa ghi) giữ nguyên trạng thái (có test qua hành vi của dịch vụ phiên học).
- [x] Trên giao diện, Chủ trì bấm Ghi thì thấy Tạm dừng/Tiếp tục và Dừng ghi; học viên không có nút ghi hình nào.
- [x] Kiểm tay: ghi 10 giây, tạm dừng 10 giây, tiếp tục 10 giây, dừng ghi: Bản ghi phát được, dài khoảng 20 giây, không có đoạn đứng hình ở chỗ tạm dừng, và thời lượng hiển thị khớp.
- [x] Dừng ghi chốt Bản ghi; bấm Ghi lần nữa tạo Bản ghi thứ hai của cùng Buổi học.
- [x] Kết thúc Buổi học, rời phòng, hoặc Chủ trì mất kết nối khi đang ghi hay đang tạm dừng thì Bản ghi được chốt đúng và không mất dữ liệu.
- [ ] Khi trình duyệt tự dừng chia sẻ (nút "Dừng chia sẻ" của trình duyệt), Bản ghi được chốt gọn và trạng thái đúng.
- [x] Diễn biến có các mục ghi hình bắt đầu, tạm dừng, tiếp tục, dừng, gắn với Bản ghi tương ứng; các giá trị loại sự kiện cũ không bị đổi.
- [ ] Các cột mới (thời lượng, thời điểm dừng của Bản ghi) cho phép rỗng và được tự thêm vào DB Sqlite và SqlServer đã có; Bản ghi cũ vẫn tải bình thường.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** Đã làm theo TDD ở ranh giới dịch vụ Application (xem `Tests/`). Đã kiểm chứng bằng hai trình duyệt thật (camera/mic giả): ghi 7 giây, tạm dừng 4 giây, tiếp tục 3 giây, dừng ghi cho ra Bản ghi dài 11,052 s (trình duyệt đọc được đúng giá trị đã vá vào header), Bản ghi thứ hai 3,507 s; Diễn biến có đủ 4 loại sự kiện gắn đúng Bản ghi. Cột mới (`DURATION_MS`, `STOPPED_AT`, `RECORDING_ID`) được tự thêm vào DB Sqlite đã có lúc khởi động; SqlServer dùng cùng cơ chế nhưng chưa chạy thử trên DB thật.

**Chưa kiểm chứng (nên bỏ sót có chủ đích, không đánh dấu):** (1) nút "Dừng chia sẻ" của chính trình duyệt (mã đã xử lý nhưng chưa thử); (2) cột mới trên SqlServer (cùng cơ chế với Sqlite nhưng chưa chạy trên DB SqlServer thật — lần khởi động đầu của bản mới sẽ chạy `ALTER TABLE`). Rời phòng khi đang tạm dừng đã kiểm bằng trình duyệt: Bản ghi được chốt 4,089 s và Diễn biến ghi đúng thứ tự bắt đầu → tạm dừng → dừng → kết thúc.
