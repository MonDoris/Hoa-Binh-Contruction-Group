// File: Application/Features/OnlineClassRooms/ClassRoomSessionService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.OnlineClassRooms;

public class ClassRoomSessionService : IClassRoomSessionService
{
    private readonly IAppDbContext _db;
    private readonly ClassRoomRecordingRegistry _recordings;
    private readonly RecordingFinalizer _finalizer;
    private readonly TimeProvider _time;

    public ClassRoomSessionService(IAppDbContext db, ClassRoomRecordingRegistry recordings, RecordingFinalizer finalizer, TimeProvider time)
    {
        _db = db;
        _recordings = recordings;
        _finalizer = finalizer;
        _time = time;
    }

    private DateTime Now => _time.GetLocalNow().DateTime;

    public async Task<JoinRoomDecision> JoinAsync(int roomId, string? code, ClassRoomAttendee attendee)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == roomId);
        if (room is null) return new JoinRoomDecision("Không tìm thấy phòng học.", false);
        if (room.Status is OnlineClassRoomStatus.Ended or OnlineClassRoomStatus.Cancelled)
            return new JoinRoomDecision("Phòng học đã kết thúc hoặc bị hủy.", false);

        var isHost = attendee.UserId == room.HostUserId;
        // Phòng tạo trước khi có tính năng mã tham gia thì JoinCode rỗng — không thể đòi mã chưa từng tồn
        // tại, giữ hành vi cũ (không cần mã) cho những phòng này. Phòng tạo mới luôn có mã nên luôn bị kiểm.
        if (!isHost && !attendee.IsAdmin && !string.IsNullOrEmpty(room.JoinCode)
            && !string.Equals(room.JoinCode, code?.Trim(), StringComparison.OrdinalIgnoreCase))
            return new JoinRoomDecision("Mã phòng không đúng. Hãy dùng đúng mã hoặc liên kết mời do người chủ trì cung cấp.", isHost);

        if (!isHost && room.Status == OnlineClassRoomStatus.Scheduled)
            return new JoinRoomDecision("Người chủ trì chưa bắt đầu buổi học.", isHost);

        if (isHost && room.Status == OnlineClassRoomStatus.Scheduled)
        {
            room.Start(Now);
            await _db.SaveChangesAsync();
            await LogEventAsync(roomId, OnlineClassRoomEventType.Started, attendee);
        }

        var avatarUrl = await _db.User.Where(x => x.Id == attendee.UserId).Select(x => x.AvatarUrl).FirstOrDefaultAsync();
        return new JoinRoomDecision(null, isHost, avatarUrl);
    }

    public async Task<bool> EndByHostAsync(int roomId, ClassRoomAttendee attendee)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == roomId);
        if (room is null || room.HostUserId != attendee.UserId) return false;

        // Đang ghi hay đang tạm dừng thì chốt Bản ghi TRƯỚC, để Diễn biến ghi "dừng ghi" rồi mới tới "kết thúc".
        await StopRecordingIfRunningAsync(roomId, attendee);

        room.End(Now);
        await _db.SaveChangesAsync();
        await LogEventAsync(roomId, OnlineClassRoomEventType.Ended, attendee);
        _recordings.Forget(roomId);
        return true;
    }

    public async Task EndAfterHostDisconnectedAsync(int roomId, ClassRoomAttendee attendee)
    {
        // Mất kết nối giữa lúc ghi: trình duyệt không còn để báo "xong", máy chủ tự chốt bằng đồng hồ của mình.
        await StopRecordingIfRunningAsync(roomId, attendee);

        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == roomId);
        if (room is not null && room.Status == OnlineClassRoomStatus.Live)
        {
            room.End(Now);
            await _db.SaveChangesAsync();
            await LogEventAsync(roomId, OnlineClassRoomEventType.Ended, attendee);
        }
        _recordings.Forget(roomId);
    }

    public async Task LogEventAsync(int roomId, OnlineClassRoomEventType type, ClassRoomAttendee attendee,
        string? message = null, int? recordingId = null)
    {
        _db.OnlineClassRoomEvent.Add(new OnlineClassRoomEvent
        {
            OnlineClassRoomId = roomId,
            EventType = type,
            UserId = attendee.UserId == 0 ? null : attendee.UserId,
            UserName = attendee.FullName,
            Message = message,
            RecordingId = recordingId,
            CreatedAt = Now,
        });
        await _db.SaveChangesAsync();
    }

    // ==================== GHI HÌNH ====================

    public async Task<RecordingCommandResult> StartRecordingAsync(int roomId, int recordingId, ClassRoomAttendee attendee)
    {
        if (!await IsHostOfLiveRoomAsync(roomId, attendee)) return Snapshot(roomId, accepted: false);

        // Bản ghi phải là của CHÍNH buổi này và do chính Chủ trì tạo — không cho gắn Bản ghi của buổi khác vào đồng hồ.
        var owned = await _db.OnlineClassRoomRecording.AsNoTracking().AnyAsync(x =>
            x.Id == recordingId && x.OnlineClassRoomId == roomId && x.RecordedByUserId == attendee.UserId);
        if (!owned) return Snapshot(roomId, accepted: false);

        var room = _recordings.For(roomId);
        bool accepted;
        lock (room.Gate)
        {
            accepted = room.Clock.Start(Now);
            if (accepted) room.RecordingId = recordingId;
        }
        if (!accepted) return Snapshot(roomId, accepted: false);

        await LogEventAsync(roomId, OnlineClassRoomEventType.RecordingStarted, attendee, recordingId: recordingId);
        return Snapshot(roomId, accepted: true);
    }

    public Task<RecordingCommandResult> PauseRecordingAsync(int roomId, ClassRoomAttendee attendee)
        => TransitionAsync(roomId, attendee, OnlineClassRoomEventType.RecordingPaused, (clock, now) => clock.Pause(now));

    public Task<RecordingCommandResult> ResumeRecordingAsync(int roomId, ClassRoomAttendee attendee)
        => TransitionAsync(roomId, attendee, OnlineClassRoomEventType.RecordingResumed, (clock, now) => clock.Resume(now));

    public async Task<RecordingCommandResult> StopRecordingAsync(int roomId, ClassRoomAttendee attendee)
    {
        if (!await IsHostOfLiveRoomAsync(roomId, attendee)) return Snapshot(roomId, accepted: false);

        var stopped = await StopRecordingIfRunningAsync(roomId, attendee);
        return Snapshot(roomId, accepted: stopped);
    }

    public RecordingSnapshot GetRecordingSnapshot(int roomId) => Snapshot(roomId, accepted: true).Snapshot;

    /// <summary>Tạm dừng / tiếp tục: kiểm quyền, đổi trạng thái đồng hồ, rồi ghi Diễn biến gắn với Bản ghi đang chạy.</summary>
    private async Task<RecordingCommandResult> TransitionAsync(int roomId, ClassRoomAttendee attendee,
        OnlineClassRoomEventType eventType, Func<RecordingClock, DateTime, bool> transition)
    {
        if (!await IsHostOfLiveRoomAsync(roomId, attendee)) return Snapshot(roomId, accepted: false);

        var room = _recordings.Peek(roomId);
        if (room is null) return Snapshot(roomId, accepted: false);

        bool accepted;
        int? recordingId;
        lock (room.Gate)
        {
            accepted = transition(room.Clock, Now);
            recordingId = room.RecordingId;
        }
        if (!accepted) return Snapshot(roomId, accepted: false);

        await LogEventAsync(roomId, eventType, attendee, recordingId: recordingId);
        return Snapshot(roomId, accepted: true);
    }

    /// <summary>Chốt Bản ghi đang ghi/tạm dừng của phòng (nếu có). Trả true nếu thật sự có gì để chốt.</summary>
    private async Task<bool> StopRecordingIfRunningAsync(int roomId, ClassRoomAttendee attendee)
    {
        var room = _recordings.Peek(roomId);
        if (room is null) return false;

        TimeSpan? elapsed;
        int? recordingId;
        lock (room.Gate)
        {
            elapsed = room.Clock.Stop(Now);
            recordingId = room.RecordingId;
            if (elapsed is not null) room.RecordingId = null;
        }
        if (elapsed is null) return false;

        // Trình duyệt thường đã tự báo "xong" (chốt rồi) trước khi tới đây; chỉ chốt thay khi nó chưa kịp báo.
        if (recordingId is { } id)
        {
            var recording = await _db.OnlineClassRoomRecording.FirstOrDefaultAsync(x => x.Id == id);
            if (recording is { StoppedAt: null })
                await _finalizer.FinalizeAsync(recording, elapsed.Value.TotalMilliseconds);
        }

        await LogEventAsync(roomId, OnlineClassRoomEventType.RecordingStopped, attendee, recordingId: recordingId);
        return true;
    }

    /// <summary>Chỉ Chủ trì của một Buổi học ĐANG DIỄN RA mới điều khiển được ghi hình — kể cả Admin cũng không.</summary>
    private Task<bool> IsHostOfLiveRoomAsync(int roomId, ClassRoomAttendee attendee)
        => _db.OnlineClassRoom.AsNoTracking().AnyAsync(x =>
            x.Id == roomId && x.HostUserId == attendee.UserId && x.Status == OnlineClassRoomStatus.Live);

    private RecordingCommandResult Snapshot(int roomId, bool accepted)
    {
        var room = _recordings.Peek(roomId);
        if (room is null) return new RecordingCommandResult(accepted, RecordingSnapshot.Idle);

        lock (room.Gate)
            return new RecordingCommandResult(accepted,
                new RecordingSnapshot(room.Clock.State, (long)room.Clock.Elapsed(Now).TotalMilliseconds, room.RecordingId));
    }
}
