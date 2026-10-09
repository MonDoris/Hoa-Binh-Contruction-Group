// File: Application/Features/Auth/IAuthService.cs
using System.Security.Cryptography;
using System.Text;

namespace HbELearning.Application.Features.Auth;

/// <summary>Các ca sử dụng của chức năng Auth. Tầng API gọi qua cổng này (xem API/Controllers/AuthService.cs).</summary>
public interface IAuthService
{
    /// <summary>Đăng nhập, trả về JWT.</summary>
    Task<Result> Login(LoginRequest request);

    /// <summary>Thông tin tài khoản đang đăng nhập.</summary>
    Task<Result> Me();

    /// <summary>Cập nhật hồ sơ cá nhân (họ tên, số điện thoại, ảnh đại diện).</summary>
    Task<Result> UpdateMe(UpdateProfileRequest request);

    Task<Result> ChangePassword(ChangePasswordRequest request);

    /// <summary>Bước 1 quên mật khẩu: gửi mã OTP 6 số qua Zalo ZNS, SMS hoặc Email tùy kênh được chọn.</summary>
    Task<Result> ForgotPassword(ForgotPasswordRequest request);

    /// <summary>Bước 2 quên mật khẩu: xác thực mã OTP, trả về resetToken ngắn hạn để đặt mật khẩu mới.</summary>
    Task<Result> VerifyOtp(VerifyOtpRequest request);

    /// <summary>Bước 3 quên mật khẩu: đặt mật khẩu mới bằng resetToken nhận được sau khi xác thực OTP.</summary>
    Task<Result> ResetPassword(ResetPasswordRequest request);

    /// <summary>Danh sách vai trò, phục vụ form tạo người dùng.</summary>
    /// <summary>Cấp một vé ngắn hạn để trình duyệt mở kết nối SignalR, thay cho việc đưa thẻ đăng nhập
    /// 8 tiếng vào địa chỉ WebSocket.</summary>
    Task<Result> CreateHubTicket();

    Task<Result> GetRole();
}
