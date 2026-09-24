// File: Domain/Entities/Conversation.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Conversation
{
    public int Id { get; set; }

    public ConversationType Type { get; set; }

    /// <summary>Tên nhóm. Cuộc 1-1 để null: tên hiển thị chính là tên người đối diện, mà "người đối diện"
    /// là ai thì tùy người đang xem — không thể lưu cứng một chuỗi ở đây.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>Khóa nhận diện cuộc 1-1, dạng "idNhỏ-idLớn" (VD "7-42"). Có chỉ mục duy nhất, nên hai người
    /// cùng bấm "nhắn tin" cho nhau trong cùng một giây cũng không thể sinh ra hai cuộc trò chuyện song song
    /// — chuyện chắc chắn xảy ra nếu chỉ dựa vào một câu SELECT "đã có chưa" trước khi INSERT. Nhóm để null.</summary>
    [MaxLength(50)]
    public string? DirectKey { get; set; }

    /// <summary>Ảnh đại diện của NHÓM. Cuộc 1-1 để null: ảnh hiển thị ở đó là ảnh của người đối diện, mà
    /// "người đối diện" là ai thì tùy người đang xem — cùng lý do với <see cref="Title"/>.</summary>
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Thời điểm tin nhắn cuối — để sắp xếp danh sách hội thoại mà không phải quét bảng tin nhắn.</summary>
    public DateTime? LastMessageAt { get; set; }

    public ICollection<ConversationMember> MemberList { get; set; } = new List<ConversationMember>();
    public ICollection<ChatMessage> MessageList { get; set; } = new List<ChatMessage>();
}
