// File: Application/Features/OnlineClassRooms/ClassRoomRecordingRegistry.cs
using System.Collections.Concurrent;

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>Chụp nhanh trạng thái ghi hình của một Buổi học — thứ mà mọi người trong phòng cần thấy.</summary>
public sealed record RecordingSnapshot(RecordingState State, long ElapsedMs, int? RecordingId)
{
    public static readonly RecordingSnapshot Idle = new(RecordingState.Idle, 0, null);
}

/// <summary>Kết quả một lệnh điều khiển ghi hình: có được nhận không, và trạng thái SAU lệnh.</summary>
public sealed record RecordingCommandResult(bool Accepted, RecordingSnapshot Snapshot);

/// <summary>Đồng hồ ghi hình và Bản ghi đang chạy của MỘT Buổi học. <see cref="Gate"/> tuần tự hóa các lệnh.</summary>
public sealed class RoomRecording
{
    public object Gate { get; } = new();
    public RecordingClock Clock { get; } = new();

    /// <summary>Bản ghi đang được ghi hoặc đang tạm dừng; null khi không ghi.</summary>
    public int? RecordingId { get; set; }
}

/// <summary>
/// Sổ trạng thái ghi hình của mọi Buổi học đang diễn ra. Một thể hiện duy nhất (singleton) vì dịch vụ phiên học
/// được tạo mới cho mỗi lệnh của Hub — trạng thái phải sống lâu hơn một lệnh. Giống danh sách người trong phòng ở
/// ClassRoomHub: lưu trong bộ nhớ, đủ dùng cho một tiến trình web, mất khi tiến trình khởi động lại (lúc đó cả
/// phòng cũng mất kết nối).
/// </summary>
public sealed class ClassRoomRecordingRegistry
{
    private readonly ConcurrentDictionary<int, RoomRecording> _rooms = new();

    public RoomRecording For(int roomId) => _rooms.GetOrAdd(roomId, _ => new RoomRecording());

    /// <summary>Tra mà không tạo — để hỏi trạng thái của phòng chưa từng ghi hình khỏi sinh ra mục rỗng.</summary>
    public RoomRecording? Peek(int roomId) => _rooms.TryGetValue(roomId, out var room) ? room : null;

    public void Forget(int roomId) => _rooms.TryRemove(roomId, out _);
}
