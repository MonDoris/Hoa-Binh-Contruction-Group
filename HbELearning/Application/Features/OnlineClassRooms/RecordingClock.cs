// File: Application/Features/OnlineClassRooms/RecordingClock.cs

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>Trạng thái ghi hình của một Buổi học đang diễn ra.</summary>
public enum RecordingState { Idle = 0, Recording = 1, Paused = 2 }

/// <summary>
/// Đồng hồ ghi hình của MỘT Buổi học: máy trạng thái Idle → Recording ⇄ Paused → Idle, kèm số giây đã ghi THẬT
/// (không tính các đoạn tạm dừng).
///
/// Thuần tuý, không I/O, không tự đọc giờ: mỗi lệnh nhận "bây giờ" từ bên ngoài. Nhờ vậy cùng một lớp chạy được
/// ở server (nguồn chuẩn cho mọi người trong phòng, kể cả người vào muộn) và kiểm thử được bằng đồng hồ giả.
/// Không tự khóa — nơi giữ nó (xem <see cref="RoomRecording"/>) chịu trách nhiệm tuần tự hóa các lệnh.
///
/// Lệnh không hợp lệ ở trạng thái hiện tại (tiếp tục khi đang ghi, tạm dừng khi chưa ghi...) trả về
/// false/null và KHÔNG đổi gì — Hub nhận lệnh từ trình duyệt nên phải chịu được lệnh lặp hoặc đến sai thứ tự.
/// </summary>
public sealed class RecordingClock
{
    private TimeSpan _recorded;
    private DateTime? _runningSince;

    public RecordingState State { get; private set; } = RecordingState.Idle;

    /// <summary>Bắt đầu một Bản ghi mới: chỉ khi chưa ghi gì; đồng hồ về 0.</summary>
    public bool Start(DateTime now)
    {
        if (State != RecordingState.Idle) return false;
        _recorded = TimeSpan.Zero;
        _runningSince = now;
        State = RecordingState.Recording;
        return true;
    }

    public bool Pause(DateTime now)
    {
        if (State != RecordingState.Recording) return false;
        _recorded += Since(now);
        _runningSince = null;
        State = RecordingState.Paused;
        return true;
    }

    public bool Resume(DateTime now)
    {
        if (State != RecordingState.Paused) return false;
        _runningSince = now;
        State = RecordingState.Recording;
        return true;
    }

    /// <summary>Chốt Bản ghi. Trả thời gian đã ghi thật; null nếu chưa ghi gì (không có gì để chốt).</summary>
    public TimeSpan? Stop(DateTime now)
    {
        if (State == RecordingState.Idle) return null;
        var total = Elapsed(now);
        _recorded = TimeSpan.Zero;
        _runningSince = null;
        State = RecordingState.Idle;
        return total;
    }

    /// <summary>Thời gian đã ghi thật tính đến <paramref name="now"/>; 0 khi chưa ghi.</summary>
    public TimeSpan Elapsed(DateTime now) => State switch
    {
        RecordingState.Recording => _recorded + Since(now),
        RecordingState.Paused => _recorded,
        _ => TimeSpan.Zero,
    };

    // Đồng hồ máy chủ có thể bị chỉnh lùi giữa hai lệnh; thời gian ghi không bao giờ âm.
    private TimeSpan Since(DateTime now)
        => _runningSince is { } start && now > start ? now - start : TimeSpan.Zero;
}
