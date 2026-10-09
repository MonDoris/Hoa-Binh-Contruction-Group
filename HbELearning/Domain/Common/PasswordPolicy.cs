// File: Domain/Common/PasswordPolicy.cs

namespace HbELearning.Domain.Common;

/// <summary>
/// Quy tắc mật khẩu của hệ thống — MỘT bản duy nhất cho mọi lối đặt mật khẩu: người dùng tự đổi, đặt lại
/// qua OTP, quản trị viên tạo tài khoản, quản trị viên đặt lại hộ, và nhập người dùng hàng loạt từ Excel.
///
/// Gom về một chỗ vì trước đây mỗi lối có một mức riêng (chỉ [MinLength(6)] rải rác ở DTO), nên siết một
/// chỗ là các lối còn lại vẫn mở — mà kẻ dò mật khẩu thì đi đúng cái cửa dễ nhất.
///
/// Nằm ở Domain vì đây là quy tắc nghiệp vụ thuần, không phụ thuộc HTTP hay database. Bản kiểm phía trình
/// duyệt (wwwroot/js/password.js) chỉ để báo sớm cho người gõ — máy chủ vẫn kiểm lại, vì ai cũng gọi thẳng
/// API được.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Câu mô tả quy tắc, hiện dưới ô nhập và trong file mẫu nhập người dùng.</summary>
    public const string Description =
        "Mật khẩu tối thiểu 8 ký tự, phải có cả chữ và số, và không được là mật khẩu quá dễ đoán.";

    /// <summary>
    /// Những mật khẩu bị từ chối thẳng dù đủ độ dài và đủ loại ký tự.
    ///
    /// Đây đúng là danh sách mà một kẻ dò mật khẩu thử TRƯỚC TIÊN, nên "đủ 8 ký tự có chữ có số" không cứu
    /// được gì: "12345678", "matkhau1", "abcd1234" đều lọt qua mọi quy tắc đếm ký tự.
    /// </summary>
    private static readonly HashSet<string> CommonPasswordSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "password", "password1", "password123", "passw0rd",
        "qwertyuiop", "qwerty123", "abcd1234", "abc12345", "1qaz2wsx", "zaq12wsx", "iloveyou",
        "matkhau", "matkhau1", "matkhau123", "matkhau@123", "khongbiet", "hoabinh", "hoabinh123",
        "admin123", "admin@123", "administrator", "welcome1", "welcome123", "letmein1", "changeme",
        "111111111", "000000000", "88888888", "66668888", "12341234", "asdfghjkl", "hbelearning"
    };

    /// <summary>
    /// Kiểm một mật khẩu sắp đặt.
    /// </summary>
    /// <param name="password">Mật khẩu người dùng gõ.</param>
    /// <param name="identifierList">Những chuỗi định danh của chính tài khoản đó (email, tên tài khoản, mã
    /// nhân viên). Mật khẩu trùng hoặc chứa nguyên một trong số đó thì bị từ chối — đây là mật khẩu đầu tiên
    /// người khác trong công ty nghĩ tới khi muốn vào nhầm tài khoản của đồng nghiệp.</param>
    /// <returns>null nếu hợp lệ; ngược lại là câu báo lỗi hiển thị thẳng cho người dùng.</returns>
    public static string? Validate(string? password, params string?[] identifierList)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Chưa nhập mật khẩu.";

        if (password.Length < MinLength)
            return $"Mật khẩu phải có ít nhất {MinLength} ký tự.";

        if (password.Length > 128)
            return "Mật khẩu không được dài quá 128 ký tự.";

        if (password.Trim() != password)
            return "Mật khẩu không được bắt đầu hoặc kết thúc bằng dấu cách — rất dễ gõ sai ở lần đăng nhập sau.";

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Mật khẩu phải có cả chữ và số.";

        if (CommonPasswordSet.Contains(password))
            return "Mật khẩu này nằm trong danh sách bị đoán ra đầu tiên. Hãy chọn mật khẩu khác.";

        // Chỉ một ký tự lặp lại ("aaaaaaaa1") hay một dãy liên tiếp ("12345678") thì dài bao nhiêu cũng vô nghĩa.
        if (password.Distinct().Count() < 4)
            return "Mật khẩu dùng quá ít ký tự khác nhau. Hãy chọn mật khẩu khác.";

        foreach (var identifier in identifierList)
        {
            if (string.IsNullOrWhiteSpace(identifier)) continue;

            // Chỉ lấy phần trước @ của email: "an.nguyen@congty.vn" thì phần đáng so là "an.nguyen".
            var value = identifier.Split('@')[0].Trim();
            if (value.Length < 4) continue;

            if (password.Contains(value, StringComparison.OrdinalIgnoreCase))
                return "Mật khẩu không được chứa email, tên tài khoản hay mã nhân viên của chính bạn.";
        }

        return null;
    }
}
