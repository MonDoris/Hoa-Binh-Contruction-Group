// File: Application/Features/OnlineClassRooms/IClassRoomSessionService.cs

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>Người đang ở trong phòng học online, do SignalR Hub cung cấp.
///
/// Truyền tường minh thay vì đọc ICurrentUser: ICurrentUser dựa vào HttpContext của yêu cầu, còn Hub gọi vào cả
/// lúc một kết nối vừa rớt (OnDisconnectedAsync) — lúc đó không có yêu cầu HTTP nào đáng tin để hỏi.</summary>
public sealed record ClassRoomAttendee(int UserId, string FullName, bool IsAdmin);

/// <summary>Kết quả xin vào phòng: <see cref="Error"/> khác null nghĩa là bị từ chối, kèm câu báo cho người dùng.</summary>
public sealed record JoinRoomDecision(string? Error, bool IsHost);

/// <summary>
/// Nghiệp vụ của một buổi học online đang diễn ra: ai được vào, khi nào buổi học bắt đầu/kết thúc, và biên bản
/// diễn biến.
///
/// Trước khi tách lớp, toàn bộ phần này nằm trong ClassRoomHub cùng với việc chuyển tiếp tín hiệu WebRTC. Hub
/// giờ chỉ còn lo phần truyền tải thời gian thực (nhóm kết nối, danh sách người đang có mặt, chuyển tiếp gói
/// tin); mọi quyết định có đụng tới database đi qua đây.
/// </summary>
public interface IClassRoomSessionService
{
    /// <summary>Xét một người xin vào phòng. Nếu chính người chủ trì vào một phòng đang Chờ thì buổi học bắt đầu
    /// ngay (chuyển Live + ghi biên bản "Bắt đầu").
    ///
    /// Đây là điểm kiểm soát THẬT SỰ cho việc vào phòng — dù thấy buổi học trong danh sách hay không, người
    /// tham gia (không phải chủ trì/Admin) bắt buộc phải có đúng mã tham gia của phòng.</summary>
    Task<JoinRoomDecision> JoinAsync(int roomId, string? code, ClassRoomAttendee attendee);

    /// <summary>Người chủ trì bấm Kết thúc. Trả false (và không làm gì) nếu phòng không tồn tại hoặc người gọi
    /// không phải chủ trì.</summary>
    Task<bool> EndByHostAsync(int roomId, ClassRoomAttendee attendee);

    /// <summary>Người chủ trì rớt kết nối đột ngột (đóng tab/mất mạng) mà chưa bấm Kết thúc — tự kết thúc buổi
    /// học đang Live để không treo mãi ở trạng thái Đang diễn ra.</summary>
    Task EndAfterHostDisconnectedAsync(int roomId, ClassRoomAttendee attendee);

    /// <summary>Ghi một dòng vào nhật ký diễn biến (biên bản) của phòng học — xem lại được sau buổi học.</summary>
    Task LogEventAsync(int roomId, OnlineClassRoomEventType type, ClassRoomAttendee attendee, string? message = null);
}
