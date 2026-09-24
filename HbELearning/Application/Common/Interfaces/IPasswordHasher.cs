// File: Application/Common/Interfaces/IPasswordHasher.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Băm và kiểm tra mật khẩu / mã OTP.
///
/// Nghiệp vụ đăng nhập và quản lý người dùng từng gọi thẳng BCrypt.Net.BCrypt — một thư viện cụ thể. Thuật toán
/// băm là chi tiết hạ tầng: đổi sang Argon2 hay PBKDF2 sau này thì chỉ thay lớp hiện thực
/// (Infrastructure/Identity/BCryptPasswordHasher.cs), không đụng vào nghiệp vụ.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Băm một chuỗi bí mật (mật khẩu, mã OTP) để lưu xuống database.</summary>
    string Hash(string secret);

    /// <summary>Chuỗi bí mật có khớp với bản băm đã lưu không.</summary>
    bool Verify(string secret, string hash);
}
