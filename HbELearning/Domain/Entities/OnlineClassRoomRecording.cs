// File: Domain/Entities/OnlineClassRoomRecording.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>File video ghi lại buổi học online (quay ở phía trình duyệt người chủ trì bằng MediaRecorder,
/// tải lên sau khi kết thúc buổi học). 1 phòng học có thể có nhiều bản ghi nếu buổi học bị ngắt giữa chừng.</summary>
public class OnlineClassRoomRecording
{
    public int Id { get; set; }

    public int OnlineClassRoomId { get; set; }
    public OnlineClassRoom OnlineClassRoom { get; set; } = null!;

    /// <summary>Đường dẫn tương đối (VD: /uploads/classroom-recording/202608/xxx.webm), lưu qua IFileService.</summary>
    [Required, MaxLength(500)]
    public string FileUrl { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    public int RecordedByUserId { get; set; }
    public User RecordedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
