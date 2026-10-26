// File: Domain/Entities/OnlineClassRoomEvent.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>1 dòng trong nhật ký diễn biến (biên bản) của một buổi học online — vào/ra phòng, chat,
/// bật/tắt mic-cam, bắt đầu/kết thúc. Ghi lại theo thời gian thực từ ClassRoomHub để xem lại sau buổi học.</summary>
public class OnlineClassRoomEvent
{
    public int Id { get; set; }

    public int OnlineClassRoomId { get; set; }
    public OnlineClassRoom OnlineClassRoom { get; set; } = null!;

    public OnlineClassRoomEventType EventType { get; set; }

    /// <summary>Người thực hiện hành động. Null cho các sự kiện hệ thống không gắn với 1 người cụ thể.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Snapshot họ tên tại thời điểm ghi — giữ nguyên giá trị hiển thị dù người dùng đổi tên sau này.</summary>
    [MaxLength(150)]
    public string? UserName { get; set; }

    /// <summary>Nội dung tin nhắn chat, chỉ dùng cho EventType = ChatMessage.</summary>
    [MaxLength(500)]
    public string? Message { get; set; }

    /// <summary>Bản ghi mà sự kiện ghi hình này nói tới (RecordingStarted/Paused/Resumed/Stopped); null với mọi sự
    /// kiện khác. Cố ý KHÔNG khai báo khóa ngoại: cột này được thêm vào bảng đã có dữ liệu, và Diễn biến vốn là nhật ký
    /// chỉ ghi thêm — trang xem lại dùng nó để đặt sự kiện đúng vào Bản ghi của nó.</summary>
    public int? RecordingId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
