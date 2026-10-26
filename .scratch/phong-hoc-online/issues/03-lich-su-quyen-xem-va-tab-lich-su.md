# 03: Lịch sử có quyền xem theo "người đã vào" (tab Lịch sử)

**What to build:** Trang Phòng học online có thêm tab Lịch sử liệt kê các Buổi học đã kết thúc mà người xem được phép xem. Người được phép là Chủ trì, Admin và những người từng vào buổi đó (có mục "vào phòng" trong Diễn biến). Mỗi thẻ cho biết tên, ngày, Chủ trì, thời lượng thật, số Người tham gia, số Bản ghi và tổng dung lượng. Có tìm kiếm theo tên hoặc tên Chủ trì, lọc theo khoảng ngày và lọc "chỉ buổi tôi chủ trì hoặc tham gia". Người chưa từng vào một Buổi học không thấy nó trong Lịch sử, và nhận lỗi 403 khi gọi thẳng vào Bản ghi hoặc Diễn biến của nó. Quy tắc này cũng áp dụng cho danh sách Buổi học đã kết thúc ở những chỗ khác.

Tab này đặt trên trang hiện tại; việc làm lại toàn bộ trang ngoài thuộc ticket 06.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] Chủ trì, Admin và người từng vào buổi đó xem được; mọi người khác bị 403 khi mở Bản ghi hoặc Diễn biến, và không thấy buổi đó trong Lịch sử hay trong danh sách Buổi học đã kết thúc (có test cho từng nhóm người).
- [x] Người chỉ mở màn hình nhập mã nhưng chưa vào phòng thì chưa được xem Lịch sử của buổi đó.
- [x] Người vào muộn hay rời sớm vẫn thấy toàn bộ Bản ghi và Diễn biến của buổi.
- [x] Thẻ Lịch sử hiện đủ tên, ngày, Chủ trì, thời lượng thật, số Người tham gia (tính theo người, không theo lượt vào), số Bản ghi và tổng dung lượng; các con số có test.
- [x] Tìm kiếm theo tên hoặc tên Chủ trì, lọc khoảng ngày, và lọc "của tôi" hoạt động và kết hợp được với nhau (có test).
- [x] Buổi chưa diễn ra, đang diễn ra và đã hủy giữ nguyên cách hiển thị như trước.
- [x] Có trạng thái trống ("chưa có buổi nào") và trạng thái lỗi rõ ràng.
- [x] Giao diện tab dùng bộ màu của hệ thống, hoạt động ở theme sáng và tối.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** 13 test ở ranh giới dịch vụ (quyền xem cho từng nhóm người, người vào muộn/rời sớm, bộ lọc, các con số). Kiểm bằng trình duyệt với 6 tài khoản: Admin thấy cả 3 buổi; người chưa từng vào nhận 403 ở chi tiết, Bản ghi và Diễn biến; học viên chỉ thấy buổi mình chủ trì hoặc đã vào. Trạng thái lỗi của tab đã viết nhưng chưa ép cho nó xảy ra.
