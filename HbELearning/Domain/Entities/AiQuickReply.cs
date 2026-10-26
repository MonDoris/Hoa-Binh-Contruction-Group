// File: Domain/Entities/AiQuickReply.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một câu trả lời CÀI SẴN của HB Minerva: gặp đúng câu hỏi này thì trả lời ngay bằng câu chữ đã soạn,
/// không hỏi model và không tra kho tài liệu.
///
/// Vì sao cần: những câu xã giao ("hi", "cảm ơn", "bạn là ai") không có trong tài liệu nội bộ nào cả. Đưa
/// chúng qua đường tìm kiếm ngữ nghĩa là kiểu gì cũng vớ được một đoạn tài liệu bất kỳ — chữ "hi" ngắn tới
/// mức vector của nó gần như nhau với mọi thứ — rồi model viết ra một bài giảng lạc đề mất cả chục giây.
/// Trả lời cài sẵn cho đúng nhóm câu đó vừa đúng, vừa ra ngay lập tức, vừa không tốn một giây CPU nào của
/// máy chủ AI.
///
/// Đây KHÔNG phải chỗ để nhồi kiến thức công ty: mọi câu hỏi có câu trả lời nằm trong tài liệu hay bài học
/// thì nạp tài liệu / để AI học khóa học vẫn tốt hơn nhiều, vì một dòng cài sẵn chỉ khớp đúng những chữ đã
/// liệt kê, còn tìm kiếm ngữ nghĩa hiểu được cả những cách hỏi chưa ai nghĩ tới.</summary>
public class AiQuickReply
{
    public int Id { get; set; }

    /// <summary>Các câu/từ khóa kích hoạt, ngăn nhau bằng dấu gạch đứng "|".
    /// VD: <c>hi|hello|chào|xin chào|chào bạn</c>
    ///
    /// So khớp bỏ qua hoa thường VÀ bỏ qua dấu tiếng Việt, nên viết "chào" là bắt được cả người gõ "chao".</summary>
    [Required, MaxLength(500)]
    public string Keyword { get; set; } = null!;

    /// <summary>Câu trả lời. Dùng được vài chỗ thay chữ động — xem <see cref="Services.QuickReplyMatcher"/>:
    /// <c>{ten}</c> họ tên đầy đủ người hỏi, <c>{ten_ngan}</c> tên gọi, <c>{vai_tro}</c> vai trò.</summary>
    [Required, MaxLength(2000)]
    public string Answer { get; set; } = null!;

    public QuickReplyMatchType MatchType { get; set; } = QuickReplyMatchType.Exact;

    /// <summary>Tắt tạm một dòng mà không phải xóa — xóa rồi thì câu chữ đã soạn công phu cũng đi theo.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Thứ tự xét. Dòng nhỏ hơn được xét trước, nên câu khớp hẹp (cả câu) đặt trên câu khớp rộng
    /// (chứa từ khóa) thì mới có cơ hội thắng.</summary>
    public int OrderIndex { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
