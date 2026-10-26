// File: Domain/Entities/AiChatSession.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một cuộc hỏi đáp với HB Minerva, tương đương một "hội thoại" trong danh sách lịch sử bên trái
/// trang HB Minerva.
///
/// Cố ý KHÔNG dùng lại <see cref="Conversation"/>/<see cref="ChatMessage"/> của phần chat giữa người với
/// người: hai thứ chỉ giống nhau ở cái tên. Chat người-với-người có thành viên, có đã-đọc/chưa-đọc, có
/// SignalR đẩy tin theo thời gian thực và tin nhắn thuộc về NHIỀU người; còn ở đây mỗi hội thoại chỉ có
/// đúng một chủ sở hữu, mỗi câu trả lời phải mang kèm danh sách nguồn tài liệu, và không có ai để đẩy tin
/// tới. Nhét chung vào một bảng thì mọi truy vấn của cả hai bên đều phải mang thêm một điều kiện lọc
/// "loại hội thoại", và một lỗi quên lọc là tin nhắn riêng của người ta lọt vào khung chat AI.</summary>
public class AiChatSession
{
    public int Id { get; set; }

    /// <summary>Chủ hội thoại. Đây là ranh giới quyền của toàn bộ tính năng lịch sử: mọi truy vấn đọc/xóa
    /// đều phải lọc theo cột này, không bao giờ chỉ theo Id.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Tên hội thoại hiện trong danh sách lịch sử. Mặc định lấy từ câu hỏi đầu tiên (cắt ngắn),
    /// người dùng đổi lại được — một danh sách toàn dòng "Câu hỏi về..." thì không tìm lại được gì.</summary>
    [Required, MaxLength(150)]
    public string Title { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Mốc có lượt nói gần nhất — danh sách lịch sử sắp theo cột này chứ không theo CreatedAt:
    /// hội thoại mở từ tuần trước nhưng vừa hỏi thêm sáng nay vẫn phải nằm trên đầu.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<AiChatMessage> MessageList { get; set; } = new List<AiChatMessage>();
}
