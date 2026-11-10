// File: Application/Features/Translations/UiTranslationCheck.cs
using System.Text.RegularExpressions;

namespace HbELearning.Application.Features.Translations;

/// <summary>Soát một câu AI vừa dịch TRƯỚC KHI ghi vào bảng. Model 7B chạy nội bộ thỉnh thoảng làm rơi chỗ giữ
/// chỗ, đổi con số, hoặc trả lời luôn bằng tiếng Việt — mấy lỗi này nhìn bảng dịch vẫn thấy xuôi, chỉ vỡ khi
/// chạy thật. Câu không qua được thì nơi gọi cho dịch lại, vẫn hỏng thì để trống cho người dịch tay, chứ không
/// lưu một bản sai.
///
/// Chỉ bắt lỗi chắc chắn là sai về CẤU TRÚC; còn đúng nghĩa hay không thì vẫn cần người đọc lại.</summary>
public static class UiTranslationCheck
{
    /// <summary>Cùng mẫu với PLACEHOLDER_RE ở translation.js, để màn hình và máy chủ đếm giống nhau.</summary>
    private static readonly Regex PlaceholderRegex = new(@"\{[^{}\s]*\}", RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"</?\s*([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled);

    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>Những chữ CHỈ tiếng Việt có (dấu nặng, dấu hỏi, dấu móc, đ...). Cố ý bỏ các chữ trùng với tiếng
    /// Pháp / Bồ Đào Nha (é, à, ê, ô, ã, õ...) để không bắt nhầm bản dịch sang các thứ tiếng đó.</summary>
    private static readonly Regex VietnameseOnlyRegex = new(
        "[ạảầấậẩẫăằắặẳẵẹẻẽềếệểễỉĩịọỏồốộổỗơờớợởỡụủũưừứựửữỳỵỷỹđ]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Chữ Latin mang dấu (À-ɏ, Ḁ-ỿ) — bỏ qua ×, ÷ nằm lẫn trong dải Latin-1.</summary>
    private static readonly Regex LatinAccentRegex = new("[À-ÖØ-öø-ɏḀ-ỿ]", RegexOptions.Compiled);

    /// <summary>Hệ chữ của các ngôn ngữ KHÔNG viết bằng chữ Latin. Câu nguồn là chữ tiếng Việt thật mà bản dịch
    /// không có lấy một ký tự của hệ chữ này nghĩa là model đã trả lời bằng thứ tiếng khác.</summary>
    private static readonly Dictionary<string, Regex> ScriptByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["th"] = new("[฀-๿]", RegexOptions.Compiled),
        ["lo"] = new("[຀-໿]", RegexOptions.Compiled),
        ["km"] = new("[ក-៿]", RegexOptions.Compiled),
        ["my"] = new("[က-႟]", RegexOptions.Compiled),
        ["ja"] = new("[぀-ヿ一-鿿]", RegexOptions.Compiled),
        ["zh"] = new("[一-鿿]", RegexOptions.Compiled),
        ["ko"] = new("[가-힯ᄀ-ᇿ]", RegexOptions.Compiled),
        ["ru"] = new("[Ѐ-ӿ]", RegexOptions.Compiled),
        ["uk"] = new("[Ѐ-ӿ]", RegexOptions.Compiled),
        ["bg"] = new("[Ѐ-ӿ]", RegexOptions.Compiled),
        ["el"] = new("[Ͱ-Ͽ]", RegexOptions.Compiled),
        ["ar"] = new("[؀-ۿ]", RegexOptions.Compiled),
        ["he"] = new("[֐-׿]", RegexOptions.Compiled),
        ["hi"] = new("[ऀ-ॿ]", RegexOptions.Compiled),
        ["ka"] = new("[Ⴀ-ჿ]", RegexOptions.Compiled),
    };

    /// <summary>Trả về lý do hỏng (để ghi log), hoặc null nếu câu dịch qua được mọi phép soát.</summary>
    public static string? FindProblem(string sourceText, string translatedText, string targetLang)
    {
        var source = sourceText.Trim();
        var target = translatedText.Trim();
        if (target.Length == 0) return "rỗng";

        if (!SameList(Collect(PlaceholderRegex, source, 0), Collect(PlaceholderRegex, target, 0)))
            return "chỗ giữ chỗ {…} không khớp";

        if (!SameList(Collect(TagRegex, source, 1, toLower: true), Collect(TagRegex, target, 1, toLower: true)))
            return "thẻ HTML không khớp";

        // Số trong câu nguồn phải còn đủ trong bản dịch — số nằm trong chỗ giữ chỗ đã soát ở trên nên bỏ ra.
        var targetNumberList = Collect(NumberRegex, PlaceholderRegex.Replace(target, ""), 0);
        foreach (var number in Collect(NumberRegex, PlaceholderRegex.Replace(source, ""), 0))
            if (!targetNumberList.Remove(number)) return $"mất con số {number}";

        var targetLangCode = targetLang.Split('-')[0];
        var sourceIsVietnamese = VietnameseOnlyRegex.IsMatch(source);

        if (!targetLangCode.Equals("vi", StringComparison.OrdinalIgnoreCase))
        {
            // Tên riêng như "HB Minerva" hay "Email" giữ nguyên là đúng — chỉ coi là chép lại nguồn khi câu nguồn
            // có chữ tiếng Việt thật.
            if (sourceIsVietnamese && string.Equals(source, target, StringComparison.Ordinal))
                return "chép nguyên câu nguồn";

            if (VietnameseOnlyRegex.IsMatch(target)) return "còn chữ tiếng Việt";

            if (ScriptByLanguage.TryGetValue(targetLangCode, out var script))
            {
                // Ngôn ngữ đích không viết bằng chữ Latin thì không có lý do gì còn chữ Latin CÓ DẤU — đó là tiếng
                // Việt sót lại, kể cả những chữ chỉ có dấu huyền/sắc ("hoàn thành") mà mẫu bên trên cố ý bỏ qua.
                if (LatinAccentRegex.IsMatch(target)) return "còn chữ Latin có dấu";
                if (sourceIsVietnamese && !script.IsMatch(target)) return "không đúng hệ chữ của ngôn ngữ đích";
            }
        }

        // Model lan man / lặp vòng thì bản dịch dài gấp mấy lần nguồn. Chừa rộng vì tiếng Đức, tiếng Nga dài
        // hơn tiếng Việt khá nhiều.
        if (target.Length > source.Length * 4 + 40) return "dài bất thường so với câu nguồn";

        return null;
    }

    private static List<string> Collect(Regex regex, string text, int group, bool toLower = false)
        => regex.Matches(text)
            .Select(m => toLower ? m.Groups[group].Value.ToLowerInvariant() : m.Groups[group].Value)
            .ToList();

    private static bool SameList(List<string> a, List<string> b)
        => a.Count == b.Count && a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}
