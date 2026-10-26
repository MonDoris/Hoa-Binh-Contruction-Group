# 05: Bấm một dòng Diễn biến thì video nhảy tới đúng thời điểm

**What to build:** Trên trang xem lại, bấm một dòng Diễn biến thì video của Bản ghi liên quan nhảy tới đúng lúc xảy ra sự kiện đó. Vị trí được tính bằng thời điểm sự kiện trừ thời điểm bắt đầu Bản ghi, rồi trừ tổng các đoạn tạm dừng đã kết thúc trước sự kiện, suy ra từ chính các mục ghi hình trong Diễn biến của Bản ghi đó. Bản ghi cũ không có mục tạm dừng nào được coi như không có đoạn tạm dừng. Sự kiện xảy ra ngoài khoảng thời gian của mọi Bản ghi (trước khi ghi, hoặc lúc đang tạm dừng) thì không nhảy được và được đánh dấu rõ.

Đây là ticket có thể bỏ nếu thiếu thời gian: các ticket còn lại không phụ thuộc vào nó.

**Blocked by:** 01 (các mục ghi hình trong Diễn biến), 04 (trang xem lại)

**Status:** ready-for-agent

- [x] Bấm dòng Diễn biến nằm trong một Bản ghi thì video nhảy tới đúng vị trí và phát tiếp; nếu Buổi học có nhiều Bản ghi thì tự chọn đúng Bản ghi.
- [x] Vị trí nhảy tính đúng khi có một hoặc nhiều đoạn tạm dừng trước sự kiện (có test cho phép tính vị trí qua dịch vụ).
- [x] Bản ghi cũ (không có mục tạm dừng) vẫn nhảy đúng theo giờ bắt đầu Bản ghi.
- [x] Dòng Diễn biến không nằm trong Bản ghi nào (trước khi ghi, đang tạm dừng, sau khi dừng) hiển thị khác đi và không gây lỗi khi bấm.
- [x] Dòng đang tương ứng với vị trí phát hiện tại được làm nổi bật khi video chạy.
- [x] Bấm được bằng bàn phím, có tên rõ cho trình đọc màn hình.
- [x] Chuỗi mới có đủ ở vi và en.

## Comments

**Hoàn thành (2026-10-12).** 7 test cho phép tính vị trí nhảy (một và nhiều đoạn tạm dừng, sự kiện trong lúc tạm dừng, trước/sau khi ghi, nhiều Bản ghi, Bản ghi cũ, chính các dòng tạm dừng/tiếp tục). Kiểm bằng trình duyệt: bấm "tiếp tục ghi hình" đưa video tới 7,42 s; bấm dòng của Bản ghi 2 tự đổi sang "Phần 2/2". Một lưu ý nhỏ: giờ ghi "dừng" của sự kiện có thể lệch tệp vài chục mili giây (độ trễ mạng), nên bấm dòng dừng ghi sẽ đưa video tới cuối đoạn.
