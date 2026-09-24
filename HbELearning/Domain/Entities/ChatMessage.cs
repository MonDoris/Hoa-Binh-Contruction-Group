// File: Domain/Entities/ChatMessage.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class ChatMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public int SenderUserId { get; set; }
    public User SenderUser { get; set; } = null!;

    /// <summary>Phần chữ của tin. Tin chỉ có tệp đính kèm thì để chuỗi rỗng (KHÔNG null: cột NOT NULL, và
    /// mọi chỗ đọc ra đều đang dùng trực tiếp mà không kiểm tra null). Tin nhãn dán để đúng một emoji ở đây.</summary>
    [Required, MaxLength(4000)]
    public string Content { get; set; } = null!;

    /// <summary>Cách hiển thị tin — xem <see cref="ChatMessageType"/>. Mặc định Text nên tin cũ không đổi gì.</summary>
    public ChatMessageType MessageType { get; set; } = ChatMessageType.Text;

    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    /// <summary>Tên tệp GỐC lúc người ta chọn từ máy mình. Bắt buộc phải lưu: tệp trên đĩa được đặt lại tên
    /// thành một GUID (xem FileService.SaveAsync) nên suy ngược từ đường dẫn chỉ ra một chuỗi hex vô nghĩa.
    /// Với tệp gửi từ một THƯ MỤC thì đây là đường dẫn tương đối trong thư mục đó (VD "Hồ sơ/CV.pdf"),
    /// để bên nhận thấy được nó nằm ở đâu trong cây thư mục gốc.</summary>
    [MaxLength(300)]
    public string? AttachmentName { get; set; }

    /// <summary>Cỡ tệp theo byte — để vẽ "1,2 MB" trên thẻ tệp mà không phải đụng tới đĩa.</summary>
    public long AttachmentSize { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
