// File: Infrastructure/Documents/WordSectionDetector.cs
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HbELearning.Infrastructure.Documents;

/// <summary>
/// Tìm những đoạn văn mở đầu một bài học mới trong một file Word gộp nhiều bài.
///
/// Không có quy ước bắt buộc nào trong file Word nói "chỗ này sang bài khác", nên phải suy từ cách người
/// soạn thường làm. Giáo trình thật gặp đủ kiểu: có file đặt Heading 1 chuẩn, có file để cả tài liệu trong
/// Heading 1 rồi chia bài bằng Heading 2, có file chỉ gõ "BÀI 1:" in đậm chứ không đặt Heading nào. Bộ dò
/// này xử lý được cả ba, và khi không chắc thì KHÔNG tách — thà để người soạn tự chia còn hơn cắt bừa một
/// tài liệu liền mạch thành nhiều mảnh vụn.
/// </summary>
public static class WordSectionDetector
{
    /// <summary>Dòng mở đầu một bài: "Bài 1", "BÀI 2:", "Chương III", "Phần 3", "Chuyên đề 4". Cho phép số
    /// thường lẫn số La Mã, và dấu chấm / hai chấm / gạch ngang ngay sau số.</summary>
    private static readonly Regex LessonTitlePattern = new(
        @"^\s*(bài|chương|phần|chuyên\s*đề)\s*(\d{1,3}|[ivxlc]{1,7})\s*([.:)\-–—]|\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Trần số bài tách ra từ một file. Vượt ngưỡng này gần như chắc chắn là dò nhầm (mỗi dòng một
    /// "bài"), tách ra chỉ tạo rác cho người soạn phải xóa.</summary>
    private const int MaxSection = 200;

    /// <summary>Độ dài tối đa của một dòng còn được coi là dòng tiêu đề — câu văn dài mở đầu bằng "Phần lớn
    /// ..." thì không phải tên bài.</summary>
    private const int MaxTitleLength = 200;

    /// <returns>Tập các đoạn mở đầu bài mới (rỗng nghĩa là không tách), kèm tên dấu hiệu đã dùng.</returns>
    public static (HashSet<OpenXmlElement> SplitAt, string Rule) Detect(Body body, WordStyleSheet styles)
    {
        // Chỉ xét đoạn ở cấp ngoài cùng: tiêu đề nằm trong ô bảng không phải chỗ sang bài mới.
        var paragraphList = body.Elements<Paragraph>()
            .Select(x => (Paragraph: (OpenXmlElement)x, Level: styles.HeadingLevelOf(x), Text: NormalizeTitle(x.InnerText)))
            .Where(x => x.Text.Length > 0)
            .ToList();

        var empty = new HashSet<OpenXmlElement>();
        if (paragraphList.Count == 0) return (empty, "none");

        // 1. Cấp tiêu đề mà phần lớn các dòng của nó trông như tên bài ("Bài 1", "Chương 2"...). Ưu tiên cấp
        //    có nhiều dòng khớp mẫu nhất; bằng nhau thì lấy cấp cao hơn (Heading 1 trước Heading 2).
        var levelGroupList = paragraphList
            .Where(x => x.Level is not null)
            .GroupBy(x => x.Level!.Value)
            .Where(x => x.Count() >= 2)
            .OrderBy(x => x.Key)
            .ToList();

        var matchedLevel = levelGroupList
            .Select(x => (Level: x.Key, Matched: x.Count(p => IsLessonTitle(p.Text)), Total: x.Count()))
            .Where(x => x.Matched * 2 >= x.Total)
            .OrderByDescending(x => x.Matched)
            .ThenBy(x => x.Level)
            .FirstOrDefault();

        if (matchedLevel.Matched > 0)
            return Build(paragraphList.Where(x => x.Level == matchedLevel.Level), "heading");

        // 2. Không cấp nào như vậy: lấy chính những dòng mang mẫu tên bài, kể cả khi người soạn gõ chữ
        //    thường không đặt Heading.
        var patternList = paragraphList.Where(x => IsLessonTitle(x.Text)).ToList();
        if (patternList.Count >= 2) return Build(patternList, "pattern");

        // 3. Cuối cùng mới tới cấp tiêu đề cao nhất — tài liệu đặt Heading chuẩn nhưng tên bài không theo mẫu
        //    nào ("An toàn điện", "Sơ cấp cứu"...).
        if (levelGroupList.Count > 0) return Build(levelGroupList[0], "heading");

        return (empty, "none");

        (HashSet<OpenXmlElement>, string) Build(IEnumerable<(OpenXmlElement Paragraph, int? Level, string Text)> pointList, string rule)
        {
            var set = pointList.Select(x => x.Paragraph).ToHashSet();
            return set.Count is >= 2 and <= MaxSection ? (set, rule) : (empty, "none");
        }
    }

    private static bool IsLessonTitle(string text) =>
        text.Length <= MaxTitleLength && LessonTitlePattern.IsMatch(text);

    /// <summary>Gộp mọi khoảng trắng (kể cả tab và khoảng trắng cứng) thành một dấu cách — chữ trong Word hay
    /// bị cắt thành nhiều đoạn nhỏ và dính thêm khoảng trắng thừa.</summary>
    public static string NormalizeTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text.Replace(' ', ' '))
        {
            if (char.IsWhiteSpace(character))
            {
                if (builder.Length > 0 && !previousWasSpace) builder.Append(' ');
                previousWasSpace = true;
                continue;
            }
            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}
