// File: Domain/Common/OrgCodeHelper.cs
using System.Globalization;
using System.Text;

namespace HbELearning.Domain.Common;

/// <summary>Sinh mã tự động cho phòng ban / vị trí công việc.
///
/// Tách ra khỏi DepartmentService vì cả nhập liệu hàng loạt từ Excel (Services/ExcelImportService.cs) lẫn
/// màn hình thêm từng dòng đều phải sinh mã theo ĐÚNG một cách. Để mỗi nơi một bản sao thì chỉ cần sửa quy
/// tắc ở một chỗ là dữ liệu nhập bằng Excel và dữ liệu nhập tay có mã khác kiểu nhau.</summary>
public static class OrgCodeHelper
{
    /// <summary>Từ đệm tổ chức, bỏ đi khi ghép chữ cái đầu — "Phòng Công nghệ" phải ra "CN" chứ không phải "PCN".</summary>
    private static readonly HashSet<string> DepartmentStopWordSet = new()
    {
        "PHONG", "BAN", "TRUNG", "TAM", "TO", "BO", "PHAN", "KHOI", "CONG", "TY"
    };

    /// <summary>Ghép chữ cái đầu của các từ có nghĩa trong tên (bỏ từ đệm tổ chức và dấu tiếng Việt).
    /// Từ đã viết hoa toàn bộ trong tên gốc (VD "IT", "HR") được coi là chữ viết tắt có sẵn và giữ nguyên
    /// cả từ thay vì chỉ lấy 1 chữ cái đầu — tránh trường hợp "Phòng IT" bị rút gọn nhầm thành "I".</summary>
    public static string InitialsFromName(string name)
    {
        var ascii = RemoveDiacritics(name);
        var wordList = ascii.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !DepartmentStopWordSet.Contains(w.ToUpperInvariant()))
            .ToList();
        if (wordList.Count == 0)
            wordList = ascii.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        var initials = string.Concat(wordList.Select(w =>
            w.Length > 1 && w.All(char.IsUpper) ? w : char.ToUpperInvariant(w[0]).ToString()));
        return initials.Length > 6 ? initials[..6] : initials;
    }

    /// <summary>Bỏ dấu tiếng Việt để dùng làm mã (VD "Công nghệ" -> "Cong nghe").</summary>
    public static string RemoveDiacritics(string text)
    {
        var normalized = text.Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
