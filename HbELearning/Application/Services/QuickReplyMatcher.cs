// File: Application/Services/QuickReplyMatcher.cs
using System.Text;
using System.Text.RegularExpressions;

namespace HbELearning.Application.Services;

/// <summary>Luật so khớp câu trả lời cài sẵn (xem <see cref="AiQuickReply"/>).
///
/// Để ở một chỗ duy nhất vì nó được gọi từ hai phía và hai bên BẮT BUỘC phải hiểu giống nhau: lúc học viên
/// hỏi thật (AiTutorService) và lúc quản trị viên bấm "Thử" trên màn hình cài đặt. Hai bản luật riêng thì
/// sớm muộn sẽ lệch, và quản trị viên sẽ thấy "Thử" báo khớp trong khi người dùng thật không bao giờ khớp.</summary>
public static class QuickReplyMatcher
{
    /// <summary>Dòng cài sẵn ĐẦU TIÊN khớp câu hỏi, hoặc null nếu không dòng nào khớp.
    ///
    /// <paramref name="replyList"/> phải đã sắp theo OrderIndex: dòng đứng trước thắng, nên câu khớp hẹp
    /// (cả câu) xếp trên câu khớp rộng (chứa từ khóa) thì mới có cơ hội được xét tới.</summary>
    public static AiQuickReply? Match(string question, IEnumerable<AiQuickReply> replyList)
    {
        var normalizedQuestion = Normalize(question);
        if (normalizedQuestion.Length == 0) return null;

        foreach (var reply in replyList)
        {
            if (!reply.IsActive) continue;

            foreach (var keyword in SplitKeywordList(reply.Keyword))
            {
                var isMatched = reply.MatchType == QuickReplyMatchType.Exact
                    ? normalizedQuestion == keyword
                    : ContainsWholeWord(normalizedQuestion, keyword);

                if (isMatched) return reply;
            }
        }

        return null;
    }

    /// <summary>Thay các chỗ giữ chỗ trong câu trả lời bằng thông tin người hỏi.
    ///
    /// Người dùng không điền gì vào ô họ tên thì <paramref name="fullName"/> rỗng — lúc đó câu chào phải là
    /// "Chào bạn" chứ không phải "Chào ," với một khoảng trống giữa câu.</summary>
    public static string ApplyPlaceholder(string answer, string? fullName, string? roleName)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "bạn" : fullName.Trim();

        // Tên gọi của người Việt là chữ CUỐI trong họ tên ("Nguyễn Văn An" -> "An") — ngược với cách gọi
        // theo tên đầu của tiếng Anh.
        var shortName = name == "bạn" ? name : name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

        return answer
            .Replace("{ten_ngan}", shortName)
            .Replace("{ten}", name)
            .Replace("{vai_tro}", string.IsNullOrWhiteSpace(roleName) ? "nhân viên" : roleName.Trim());
    }

    /// <summary>Tách danh sách từ khóa của một dòng. Ngăn bằng "|"; xuống dòng cũng được coi là dấu ngăn để
    /// quản trị viên dán cả một danh sách từ trên xuống mà không phải tự thêm dấu gạch.</summary>
    public static IEnumerable<string> SplitKeywordList(string? raw)
        => (raw ?? string.Empty)
            .Split(['|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0);

    /// <summary>Đưa một câu về dạng để so sánh: bỏ hoa thường, BỎ DẤU tiếng Việt, gộp khoảng trắng, bỏ dấu
    /// câu ở hai đầu.
    ///
    /// Bỏ dấu là điểm quan trọng nhất: nửa số người gõ chat nội bộ không bỏ dấu ("chao ban", "cam on"), mà
    /// bắt quản trị viên liệt kê cả hai kiểu cho mọi từ khóa thì danh sách phình gấp đôi và vẫn sót.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var plain = OrgCodeHelper.RemoveDiacritics(text).ToLowerInvariant();

        // Bỏ mọi thứ không phải chữ/số ở hai đầu ("hi!!!" -> "hi", "...cảm ơn." -> "cam on") và gộp khoảng
        // trắng ở giữa. Giữ nguyên dấu câu NẰM GIỮA câu vì nó vẫn mang nghĩa của câu hỏi.
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        plain = plain.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '…', '-', ' ');

        return plain;
    }

    /// <summary>Câu hỏi có chứa từ khóa theo ĐÚNG RANH GIỚI TỪ không.
    ///
    /// Tự dò bằng chỉ số thay vì dùng \b của biểu thức chính quy: từ khóa do quản trị viên gõ nên có thể
    /// chứa ký tự đặc biệt của biểu thức chính quy (dấu ngoặc, dấu hỏi, dấu chấm), lúc đó \b vừa sai vừa có
    /// thể ném lỗi giữa lúc học viên đang chờ trả lời.</summary>
    private static bool ContainsWholeWord(string text, string keyword)
    {
        var index = text.IndexOf(keyword, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + keyword.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);

            if (beforeOk && afterOk) return true;

            index = text.IndexOf(keyword, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
