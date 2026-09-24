// File: Domain/Entities/ConversationMember.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class ConversationMember
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    public int? LastReadMessageId { get; set; }

    /// <summary>Hình nền khung chat của RIÊNG người này trong hội thoại này — tên một mẫu nền dựng sẵn
    /// (xem ChatService.BackgroundKeySet), null = nền mặc định.
    ///
    /// Đặt ở bảng THÀNH VIÊN chứ không ở bảng hội thoại, nên nền là lựa chọn cá nhân: đổi nền không bắt cả
    /// nhóm nhìn theo gu của mình, và cuộc 1-1 cũng không phải trả lời câu hỏi "ai trong hai người được
    /// quyền quyết".</summary>
    [MaxLength(40)]
    public string? BackgroundKey { get; set; }

    /// <summary>Ảnh người này TỰ TẢI LÊN làm nền cho hội thoại này. Có giá trị thì nó thắng
    /// <see cref="BackgroundKey"/> — hai thứ không bao giờ cùng có mặt, đặt cái này là cái kia bị xóa và
    /// ngược lại (xem ChatService.SetBackground / SetBackgroundImage).
    ///
    /// Cột riêng chứ không nhét chung vào BackgroundKey: khóa mẫu nền dài vài ký tự và được đối chiếu với
    /// một danh sách đóng, còn đây là đường dẫn tệp dài tới 500 ký tự. Gộp lại thì mọi chỗ đọc ra đều phải
    /// tự đoán "chuỗi này là tên mẫu hay là đường dẫn".</summary>
    [MaxLength(500)]
    public string? BackgroundUrl { get; set; }

    public bool IsMuted { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.Now;
}
