// File: Domain/Entities/ZaloOAuthToken.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Cặp access_token/refresh_token của Zalo Official Account dùng để gọi API ZNS.
/// Zalo phát hành refresh_token dùng 1 lần (mỗi lần làm mới access_token sẽ trả refresh_token mới) nên
/// phải lưu lại trong DB thay vì để cố định trong appsettings.json — luôn chỉ giữ 1 bản ghi duy nhất (Id = 1).
/// </summary>
public class ZaloOAuthToken
{
    public int Id { get; set; }

    [Required, MaxLength(1000)]
    public string AccessToken { get; set; } = null!;

    [Required, MaxLength(1000)]
    public string RefreshToken { get; set; } = null!;

    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
