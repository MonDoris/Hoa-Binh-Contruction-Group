// File: Application/Common/Interfaces/ITokenService.cs
// Cong (port) tach ra tu Infrastructure/Identity/TokenService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);

    /// <summary>
    /// Vé vào kênh thời gian thực (SignalR) — sống rất ngắn và chỉ dùng được cho đường /hubs.
    ///
    /// VÌ SAO PHẢI CÓ RIÊNG: trình duyệt không cho đặt header Authorization trên một kết nối WebSocket, nên
    /// thẻ buộc phải đi trong ĐỊA CHỈ (?access_token=...). Mà địa chỉ thì bị ghi lại ở khắp nơi — nhật ký
    /// truy cập của máy chủ, của reverse proxy, của tường lửa, và cả lịch sử trình duyệt. Thẻ đăng nhập
    /// thường sống 8 tiếng: ai đọc được một dòng log là dùng tiếp được cả buổi làm việc.
    ///
    /// Vé này thay chỗ đó: sống 2 phút, và mang <see cref="AppClaimType.TokenUseClaimType"/> nên bị từ chối
    /// nếu đem gọi API thường (xem phần kiểm ở ApiServiceExtensions). Rò ra ngoài cũng gần như vô dụng.
    /// </summary>
    (string Token, DateTime ExpiresAt) CreateHubTicket(User user);
}
