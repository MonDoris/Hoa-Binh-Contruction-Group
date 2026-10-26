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

/// <summary>Bộ lọc của Lịch sử. Mọi trường đều tùy chọn và kết hợp được với nhau.</summary>
public class OnlineClassRoomHistoryQuery
{
    /// <summary>Từ khóa tìm trong tên Buổi học hoặc tên Chủ trì.</summary>
    public string? Keyword { get; set; }

    /// <summary>Từ ngày (tính cả ngày này) — theo lúc Buổi học thật sự bắt đầu.</summary>
    public DateTime? From { get; set; }

    /// <summary>Đến ngày (tính cả ngày này).</summary>
    public DateTime? To { get; set; }

    /// <summary>Chỉ những Buổi học mình chủ trì hoặc đã vào — thu hẹp danh sách của Admin.</summary>
    public bool OnlyMine { get; set; }
}

/// <summary>Một thẻ trong Lịch sử: một Buổi học đã kết thúc cùng các con số tóm tắt.</summary>
/// <param name="DurationMs">Thời lượng thật (lúc kết thúc trừ lúc bắt đầu); null nếu thiếu một trong hai mốc.</param>
/// <param name="ParticipantCount">Số NGƯỜI đã vào (mỗi người một lần, kể cả Chủ trì) — không đếm lượt vào lại.</param>
public record OnlineClassRoomHistoryDto(
    int Id, string Title, int HostUserId, string HostUserName,
    DateTime StartedAt, DateTime? EndedAt, double? DurationMs,
    int ParticipantCount, int RecordingCount, long RecordingSizeBytes, bool IsHost);

/// <summary>Mọi thứ trang xem lại cần cho MỘT Buổi học, trong một lần gọi: tóm tắt, các Bản ghi và Diễn biến.</summary>
/// <param name="ChatMessageCount">Số tin nhắn chat trong buổi.</param>
/// <param name="CanDownload">Chỉ Chủ trì và Admin được tải Bản ghi xuống.</param>
public record OnlineClassRoomReviewDto(
    int Id, string Title, int HostUserId, string HostUserName,
    DateTime StartedAt, DateTime? EndedAt, double? DurationMs,
    int ParticipantCount, int ChatMessageCount, bool IsHost, bool CanDownload,
    IReadOnlyList<OnlineClassRoomRecordingDto> RecordingList, IReadOnlyList<OnlineClassRoomEventDto> EventList);

public class OnlineClassRoomRequest
{
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [Required] public DateTime ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
}

/// <summary>1 mục cấu hình ICE server cho WebRTC (khớp trực tiếp với RTCIceServer phía trình duyệt).</summary>
public record IceServerDto(string[] Urls, string? Username, string? Credential);

/// <summary>1 dòng trong nhật ký diễn biến (biên bản) của buổi học — vào/ra phòng, chat, bật/tắt mic-cam, bắt đầu/kết thúc.</summary>
/// <param name="SeekRecordingId">Chỉ có ở trang xem lại: Bản ghi chứa khoảnh khắc này; null nếu không nằm trong video nào.</param>
/// <param name="SeekOffsetMs">Vị trí (mili giây) trong video của <paramref name="SeekRecordingId"/> ứng với sự kiện, đã trừ các đoạn tạm dừng.</param>
public record OnlineClassRoomEventDto(
    int Id, string EventType, int? UserId, string? UserName, string? Message, DateTime CreatedAt,
    int? RecordingId = null, int? SeekRecordingId = null, long? SeekOffsetMs = null);

/// <summary>1 bản ghi video của buổi học (quay ở phía trình duyệt người chủ trì, tải lên khi kết thúc).</summary>
public record OnlineClassRoomRecordingDto(
    int Id, string FileUrl, long FileSizeBytes, int RecordedByUserId, string RecordedByUserName, DateTime CreatedAt,
    long? DurationMs = null);

/// <summary>Thời lượng thật (tính theo đồng hồ trình duyệt) để vá lại header webm sau khi ghi hình kết thúc.</summary>
public record FinishRecordingRequest(double DurationMs);
