// File: Domain/Entities/AiChatAttachment.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một tệp người dùng gửi kèm câu hỏi cho HB Minerva, giữ lại để mở hội thoại cũ vẫn tải về được.
///
/// Nội dung tệp nằm trong kho tệp RIÊNG (ngoài wwwroot, xem IFileService.SavePrivateAsync) chứ không ở thư mục
/// uploads công khai: đây là tài liệu cá nhân người dùng đưa cho AI đọc, chỉ chính chủ được tải lại — qua API
/// đã kiểm chủ hội thoại.</summary>
public class AiChatAttachment
{
    public int Id { get; set; }

    /// <summary>Tin nhắn (câu hỏi) mà tệp này đi kèm.</summary>
    public int AiChatMessageId { get; set; }
    public AiChatMessage AiChatMessage { get; set; } = null!;

    /// <summary>Tên tệp gốc phía người dùng — dùng làm tên khi tải về, không bao giờ dùng làm đường dẫn lưu.</summary>
    [Required, MaxLength(260)]
    public string FileName { get; set; } = null!;

    [Required, MaxLength(100)]
    public string ContentType { get; set; } = null!;

    /// <summary>Đường dẫn TƯƠNG ĐỐI trong kho tệp riêng — xem IFileService.SavePrivateAsync.</summary>
    [Required, MaxLength(300)]
    public string FilePath { get; set; } = null!;

    public long SizeByte { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
