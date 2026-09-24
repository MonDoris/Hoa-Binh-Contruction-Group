// File: Application/DTOs/OnlineClassRoomDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>JoinCode chỉ khác null khi người xem là chủ trì hoặc Admin — người khác dù thấy buổi học trong
/// danh sách cũng không được lộ mã, phải được chủ trì chia sẻ mã/liên kết riêng thì mới vào được
/// (xem ClassRoomHub.JoinRoom).</summary>
public record OnlineClassRoomListDto(
    int Id, string Title,
    int HostUserId, string HostUserName,
    DateTime ScheduledStart, DateTime? ScheduledEnd,
    string Status, DateTime? ActualStartAt, DateTime? ActualEndAt,
    string? JoinCode);

public class OnlineClassRoomRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [Required] public DateTime ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
}

/// <summary>1 mục cấu hình ICE server cho WebRTC (khớp trực tiếp với RTCIceServer phía trình duyệt).</summary>
public record IceServerDto(string[] Urls, string? Username, string? Credential);

/// <summary>1 dòng trong nhật ký diễn biến (biên bản) của buổi học — vào/ra phòng, chat, bật/tắt mic-cam, bắt đầu/kết thúc.</summary>
public record OnlineClassRoomEventDto(
    int Id, string EventType, int? UserId, string? UserName, string? Message, DateTime CreatedAt);

/// <summary>1 bản ghi video của buổi học (quay ở phía trình duyệt người chủ trì, tải lên khi kết thúc).</summary>
public record OnlineClassRoomRecordingDto(
    int Id, string FileUrl, long FileSizeBytes, int RecordedByUserId, string RecordedByUserName, DateTime CreatedAt);

/// <summary>Thời lượng thật (tính theo đồng hồ trình duyệt) để vá lại header webm sau khi ghi hình kết thúc.</summary>
public record FinishRecordingRequest(double DurationMs);
