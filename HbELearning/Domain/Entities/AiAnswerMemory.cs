// File: Domain/Entities/AiAnswerMemory.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một câu trả lời HB Minerva đã viết xong và GHI NHỚ lại: người sau hỏi cùng nội dung thì nhận lại ngay
/// câu trả lời này, không phải chờ model viết lại từ đầu (trên máy chạy CPU mỗi câu tốn từ nửa phút tới vài phút).
///
/// Khác <see cref="AiQuickReply"/>: câu cài sẵn do Admin soạn tay cho những câu không nằm trong tài liệu nào; còn
/// bảng này tự đầy lên từ chính những câu học viên hỏi, và mỗi dòng gắn chặt với các đoạn tài liệu đã làm căn cứ
/// (<see cref="ChunkIdList"/>) — tài liệu bị nạp lại hay người hỏi không có quyền đọc đoạn đó thì dòng này không
/// được dùng.
///
/// Dùng chung cho mọi người hỏi nên KHÔNG mang mã người hỏi: câu trả lời dựa trên tài liệu, không dựa trên ai hỏi.</summary>
public class AiAnswerMemory
{
    public int Id { get; set; }

    /// <summary>"Khóa nghĩa" của câu hỏi: các âm tiết mang nghĩa, bỏ hư từ ("mấy", "gì", "như thế nào", "ạ"...), bỏ
    /// hoa thường, dấu câu và thứ tự. Hai câu cùng khóa được coi là cùng một câu hỏi — xem
    /// <c>Application/Services/AnswerMemoryKey.cs</c> để biết vì sao KHÔNG so bằng vector.</summary>
    [Required, MaxLength(400)]
    public string QuestionKey { get; set; } = null!;

    /// <summary>Câu hỏi nguyên văn của người hỏi đầu tiên — để Admin đọc, không dùng để so khớp.</summary>
    [Required, MaxLength(500)]
    public string Question { get; set; } = null!;

    public string Answer { get; set; } = null!;

    /// <summary>Mã các đoạn tài liệu đã đưa cho model khi viết câu trả lời, xếp tăng dần, ngăn nhau bằng dấu phẩy.</summary>
    [Required, MaxLength(400)]
    public string ChunkIdList { get; set; } = null!;

    /// <summary>Model đã viết câu trả lời kèm phiên bản câu lệnh, dạng "qwen2.5:3b|2026-10-28". Đổi model hay sửa câu
    /// lệnh thì các câu cũ không dùng nữa — bản mới có thể trả lời khác, và thường là sửa chính vì câu cũ sai.</summary>
    [Required, MaxLength(100)]
    public string AnswerModel { get; set; } = null!;

    /// <summary>Số lần câu trả lời này được dùng lại thay cho model.</summary>
    public int HitCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Lần gần nhất được viết ra hoặc được dùng lại — dòng lâu không ai hỏi tới thì tự bị dọn.</summary>
    public DateTime LastUsedAt { get; set; } = DateTime.Now;

    /// <summary>Lần Admin sửa tay câu trả lời gần nhất (null = nguyên văn của model).</summary>
    public DateTime? UpdatedAt { get; set; }
}
