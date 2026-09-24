// File: Domain/Entities/AiChatMessage.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một lượt nói trong hội thoại với Pico B — câu hỏi của người dùng hoặc câu trả lời của AI.</summary>
public class AiChatMessage
{
    public int Id { get; set; }

    public int AiChatSessionId { get; set; }
    public AiChatSession AiChatSession { get; set; } = null!;

    public AiChatRole Role { get; set; }

    /// <summary>Nội dung thô (chữ thuần, kể cả các số trích dẫn [1] AI chèn vào) — giao diện tự dựng lại HTML
    /// đúng như lúc câu trả lời vừa sinh ra.</summary>
    [Required]
    public string Content { get; set; } = null!;

    /// <summary>Danh sách nguồn tài liệu của câu trả lời, lưu nguyên dạng JSON của
    /// <see cref="DTOs.AiAnswerSourceDto"/>. Null với câu hỏi của người dùng.
    ///
    /// Lưu ĐOẠN CHỮ đã dùng chứ không lưu mã đoạn tài liệu: nạp lại tài liệu là các đoạn cũ bị xóa và sinh
    /// mã mới, lúc đó lịch sử sẽ trỏ vào chỗ trống. Người xem lại cần thấy đúng thứ AI đã đọc ở thời điểm
    /// trả lời, kể cả khi tài liệu gốc từ đó tới nay đã sửa hoặc đã gỡ khỏi kho tri thức.</summary>
    public string? SourceJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
