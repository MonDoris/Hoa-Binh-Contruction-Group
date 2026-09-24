// File: Domain/Common/PhoneHelper.cs
namespace HbELearning.Domain.Common;

public static class PhoneHelper
{
    /// <summary>"0912345678" / "+84912345678" / "84912345678" -> "84912345678"; null nếu không hợp lệ.</summary>
    public static string? NormalizeVietnamPhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length is >= 11 and <= 12) return digits;
        if (digits.StartsWith("0") && digits.Length is 9 or 10) return "84" + digits[1..];
        return null;
    }
}
