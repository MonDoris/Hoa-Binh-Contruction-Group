// File: Domain/Entities/OnlineClassRoom.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một buổi học trực tuyến (video call thật, báo hiệu qua SignalR + WebRTC).</summary>
public class OnlineClassRoom
{
    public int Id { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Mã tham gia — chỉ người có mã này (hoặc liên kết kèm mã) mới vào được phòng học thực sự
    /// (xem ClassRoomHub.JoinRoom); không phụ thuộc vào việc có nhìn thấy buổi học trong danh sách hay
    /// không. Sinh tự động khi tạo phòng, không null với phòng tạo sau khi có tính năng này.</summary>
    [MaxLength(12)]
    public string? JoinCode { get; set; }

    /// <summary>Người tạo/chủ trì — người phát video/màn hình chính trong buổi học.</summary>
    public int HostUserId { get; set; }
    public User HostUser { get; set; } = null!;

    public DateTime ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }

    public OnlineClassRoomStatus Status { get; set; } = OnlineClassRoomStatus.Scheduled;

    public DateTime? ActualStartAt { get; set; }
    public DateTime? ActualEndAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ==================== HÀNH VI ====================
    // Vòng đời của buổi học: Scheduled -> Live -> Ended, hoặc Scheduled -> Cancelled. Trước đây mỗi bước chuyển
    // được viết tay bằng cách gán Status + mốc thời gian ở từng nơi gọi (ClassRoomHub ba chỗ, Controller một chỗ),
    // nên quên gán mốc thời gian ở một chỗ là biên bản buổi học thiếu giờ bắt đầu/kết thúc mà không ai hay.

    /// <summary>Chỉ hủy được buổi học CHƯA diễn ra.</summary>
    public bool CanBeCancelled => Status == OnlineClassRoomStatus.Scheduled;

    /// <summary>Người chủ trì vào phòng lần đầu: buổi học bắt đầu.</summary>
    public void Start(DateTime now)
    {
        if (Status != OnlineClassRoomStatus.Scheduled)
            throw new InvalidOperationException($"Chỉ bắt đầu được buổi học đang chờ (hiện: {Status}).");
        Status = OnlineClassRoomStatus.Live;
        ActualStartAt = now;
    }

    /// <summary>Kết thúc buổi học.
    ///
    /// Cố ý KHÔNG chặn theo trạng thái hiện tại: người chủ trì bấm Kết thúc thì hệ thống luôn đóng phòng, kể cả
    /// khi trạng thái đang lệch (VD phòng vẫn ghi Scheduled vì lần vào trước bị rớt mạng giữa chừng) — đúng hành
    /// vi trước khi đưa logic này vào thực thể.</summary>
    public void End(DateTime now)
    {
        Status = OnlineClassRoomStatus.Ended;
        ActualEndAt = now;
    }

    /// <summary>Hủy buổi học chưa diễn ra.</summary>
    public void Cancel()
    {
        if (!CanBeCancelled)
            throw new InvalidOperationException($"Chỉ hủy được buổi học chưa diễn ra (hiện: {Status}).");
        Status = OnlineClassRoomStatus.Cancelled;
    }
}
