// File: Infrastructure/Identity/BCryptPasswordHasher.cs

namespace HbELearning.Infrastructure.Identity;

/// <summary>Hiện thực IPasswordHasher bằng BCrypt — đúng lời gọi mà nghiệp vụ từng dùng trực tiếp, nên mọi
/// mật khẩu đã lưu trong database vẫn kiểm tra đúng như trước.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string secret) => BCrypt.Net.BCrypt.HashPassword(secret);

    public bool Verify(string secret, string hash) => BCrypt.Net.BCrypt.Verify(secret, hash);
}
