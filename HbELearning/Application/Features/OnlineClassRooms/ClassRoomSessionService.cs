// File: Application/Features/OnlineClassRooms/ClassRoomSessionService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.OnlineClassRooms;

public class ClassRoomSessionService : IClassRoomSessionService
{
    private readonly IAppDbContext _db;

    public ClassRoomSessionService(IAppDbContext db) => _db = db;

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
            room.Start(DateTime.Now);
            await _db.SaveChangesAsync();
            await LogEventAsync(roomId, OnlineClassRoomEventType.Started, attendee);
        }

        return new JoinRoomDecision(null, isHost);
    }

    public async Task<bool> EndByHostAsync(int roomId, ClassRoomAttendee attendee)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == roomId);
        if (room is null || room.HostUserId != attendee.UserId) return false;

        room.End(DateTime.Now);
        await _db.SaveChangesAsync();
        await LogEventAsync(roomId, OnlineClassRoomEventType.Ended, attendee);
        return true;
    }

    public async Task EndAfterHostDisconnectedAsync(int roomId, ClassRoomAttendee attendee)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == roomId);
        if (room is null || room.Status != OnlineClassRoomStatus.Live) return;

        room.End(DateTime.Now);
        await _db.SaveChangesAsync();
        await LogEventAsync(roomId, OnlineClassRoomEventType.Ended, attendee);
    }

    public async Task LogEventAsync(int roomId, OnlineClassRoomEventType type, ClassRoomAttendee attendee, string? message = null)
    {
        _db.OnlineClassRoomEvent.Add(new OnlineClassRoomEvent
        {
            OnlineClassRoomId = roomId,
            EventType = type,
            UserId = attendee.UserId == 0 ? null : attendee.UserId,
            UserName = attendee.FullName,
            Message = message
        });
        await _db.SaveChangesAsync();
    }
}
