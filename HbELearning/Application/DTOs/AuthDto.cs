// File: Application/DTOs/AuthDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

public class LoginRequest
{
    /// <summary>Định danh đăng nhập: ĐỊA CHỈ EMAIL hoặc TÊN TÀI KHOẢN (User.UserName) — người dùng gõ cái nào
    /// cũng được. Tên trường giữ nguyên "email" để không phá các bản giao diện/kịch bản gọi API đã có; cố ý
    /// KHÔNG gắn [EmailAddress] nữa, nếu không tên tài khoản sẽ bị chặn ngay từ khâu kiểm tra mô hình.</summary>
    [Required(ErrorMessage = "Email hoặc tên tài khoản là bắt buộc"), MaxLength(150)]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
    public string Password { get; set; } = null!;
}

public record LoginResponse(string Token, DateTime ExpiresAt, UserProfileDto Profile);

/// <summary>Phản hồi khi đăng nhập KHÔNG thành công.
///
/// <see cref="Message"/> đứng đầu và giữ đúng tên "message" như MessageResponse — js/api.js đọc data.message
/// cho mọi lỗi, đổi tên là trang đăng nhập rơi về câu thông báo chung chung "Máy chủ trả về lỗi 401".
/// Hai trường còn lại là phần thêm: giao diện dùng để đếm ngược số lần còn lại và để khóa hẳn biểu mẫu.</summary>
/// <param name="RemainingAttempt">Số lần gõ sai còn được phép trước khi khóa tài khoản; 0 = vừa bị khóa.
/// Null khi không có gì để đếm (định danh không tồn tại) — giao diện giữ nguyên con số đang hiện.</param>
/// <param name="IsLockedOut">Tài khoản đang bị khóa, phải nhờ phòng IT mở lại.</param>
public record LoginFailedResponse(string Message, int? RemainingAttempt, bool IsLockedOut);

public class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = null!;

    // Quy tắc đầy đủ nằm ở PasswordPolicy và được kiểm ở tầng nghiệp vụ — ở đây chỉ chặn chuỗi rỗng.
    // Đặt luật độ dài tại chỗ này nữa là có hai nguồn sự thật, sửa một bên quên bên kia.
    [Required(ErrorMessage = "Chưa nhập mật khẩu mới")]
    public string NewPassword { get; set; } = null!;
}

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc"), EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = null!;

    /// <summary>Kênh gửi OTP: "zalo" (mặc định), "sms" hoặc "email".</summary>
    public string? Channel { get; set; }
}

public class VerifyOtpRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Mã OTP là bắt buộc")]
    public string Code { get; set; } = null!;
}

public record VerifyOtpResponse(string ResetToken);

/// <summary>Vé vào kênh thời gian thực. Xem <see cref="ITokenService.CreateHubTicket"/> để biết vì sao nó
/// không dùng chung thẻ đăng nhập.</summary>
public record HubTicketResponse(string Ticket, DateTime ExpiresAt);

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Thiếu mã xác thực")]
    public string ResetToken { get; set; } = null!;

    [Required(ErrorMessage = "Chưa nhập mật khẩu mới")]
    public string NewPassword { get; set; } = null!;
}
