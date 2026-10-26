# 02: Mọi người thấy trạng thái ghi hình đúng, kể cả người vào muộn

**What to build:** Mọi Người tham gia thấy rõ Buổi học có đang được ghi hay không: "Đang ghi mm:ss" khi đang ghi, "Tạm dừng ghi" khi tạm dừng, không hiện gì khi không ghi. Đồng hồ ghi hình không tính phần tạm dừng và do server làm nguồn chuẩn, nên người vào muộn thấy đúng số thay vì 00:00. Mỗi lần Chủ trì đổi trạng thái, mọi người trong phòng được cập nhật ngay.

**Blocked by:** 01 (đồng hồ ghi hình và các lệnh ghi hình của Chủ trì)

**Status:** ready-for-agent

- [x] Khi Chủ trì ghi, mọi Người tham gia thấy "Đang ghi" kèm đồng hồ chạy; khi tạm dừng đồng hồ đứng yên và đổi thành "Tạm dừng ghi"; khi dừng ghi thì huy hiệu biến mất.
- [x] Đồng hồ giữa người xem và Chủ trì lệch nhau không quá khoảng một giây sau nhiều lần tạm dừng và tiếp tục.
- [x] Người vào muộn thấy ngay trạng thái và thời gian đúng lúc vào (có test: vào sau khi đã ghi và tạm dừng nhiều lần, số hiển thị bằng thời gian ghi thật).
- [x] Trạng thái ghi hình được xóa khi Buổi học kết thúc; Buổi học khác không bị ảnh hưởng.
- [x] Yêu cầu đổi trạng thái ghi hình không đến từ Chủ trì của phòng đó bị bỏ qua, không làm đổi trạng thái của ai (có test).
- [x] Huy hiệu có biểu tượng và chữ, không chỉ dựa vào màu; trình đọc màn hình đọc được trạng thái.
- [x] Kiểm tay bằng hai trình duyệt: Chủ trì và một học viên cùng thấy trạng thái đổi đúng.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12), làm cùng ticket 01 vì dùng chung kênh.** Học viên vào muộn thấy ngay `00:07` khi buổi đã ghi 7 giây; cả hai trình duyệt đứng yên cùng số khi tạm dừng và chạy tiếp cùng số (`00:10`) sau khi tiếp tục. Huy hiệu có chấm, chữ và thông báo `role="status"` chỉ khi trạng thái đổi. Có test: người vào muộn sau nhiều lần tạm dừng, trạng thái bị xóa khi buổi kết thúc, phòng khác không bị ảnh hưởng.
