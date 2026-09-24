// File: Domain/Common/UserNameHelper.cs
using System.Text.RegularExpressions;

namespace HbELearning.Domain.Common;

/// <summary>Tên tài khoản đăng nhập (User.UserName) — chuẩn hóa và kiểm tra dạng thức.</summary>
public static class UserNameHelper
{
    public const int MinLength = 3;
    public const int MaxLength = 50;

    /// <summary>Câu nhắc dạng thức, dùng chung cho mọi chỗ báo lỗi để người dùng luôn thấy đúng một luật.</summary>
    public const string RuleMessage =
        "Tên tài khoản dài 3–50 ký tự, chỉ gồm chữ không dấu, chữ số và các ký tự . _ -";

    /// <summary>Cố ý KHÔNG cho ký tự "@": nhờ vậy một chuỗi gõ ở ô đăng nhập không thể vừa là email của người
    /// này vừa là tên tài khoản của người khác, và việc tra cứu luôn ra đúng một người.</summary>
    private static readonly Regex Pattern = new(@"^[a-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>Bỏ khoảng trắng thừa và đưa về chữ thường. Chuỗi rỗng -> null (tài khoản không đặt tên đăng nhập).</summary>
    public static string? Normalize(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Đã chuẩn hóa rồi thì có dùng làm tên tài khoản được không.</summary>
    public static bool IsValid(string normalized) =>
        normalized.Length is >= MinLength and <= MaxLength && Pattern.IsMatch(normalized);
}
