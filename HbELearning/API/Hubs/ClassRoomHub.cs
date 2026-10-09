// File: API/Hubs/ClassRoomHub.cs
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HbELearning.API.Hubs;

/// <summary>
/// Kênh báo hiệu (signaling) cho phòng học online: người chủ trì phát video/màn hình trực tiếp tới từng
/// người tham gia bằng WebRTC (mỗi người tham gia có 1 kết nối riêng với người chủ trì — không phải mesh
/// toàn bộ N-N — để tránh quá tải băng thông). Hub chỉ chuyển tiếp thông tin kết nối (SDP/ICE) và chat;
/// luồng video/audio thật đi trực tiếp giữa các trình duyệt (peer-to-peer), không qua máy chủ.
///
/// Hub chỉ lo phần truyền tải thời gian thực. Ai được vào phòng, khi nào buổi học bắt đầu/kết thúc và biên bản
/// diễn biến là nghiệp vụ, nằm ở <see cref="IClassRoomSessionService"/>.
/// </summary>
[Authorize]
public class ClassRoomHub : Hub
{
    private readonly IClassRoomSessionService _session;
    public ClassRoomHub(IClassRoomSessionService session) => _session = session;

    private record Participant(string ConnectionId, int UserId, string FullName, bool IsHost);

    /// <summary>roomId -> (connectionId -> thông tin người tham gia). Lưu tạm trong bộ nhớ, đủ dùng cho 1 tiến trình web.</summary>
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, Participant>> _roomList = new();

    private int CurrentUserId =>
        int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private string CurrentFullName => Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Người dùng";
    private bool CurrentIsAdmin => Context.User?.FindFirstValue(ClaimTypes.Role) == RoleName.Admin;

    private ClassRoomAttendee Attendee => new(CurrentUserId, CurrentFullName, CurrentIsAdmin);

    /// <summary>Vào phòng học: đăng ký kết nối và nhận lại danh sách người đã có mặt để bắt đầu thiết lập WebRTC.</summary>
    public async Task JoinRoom(int roomId, string? code)
    {
        var decision = await _session.JoinAsync(roomId, code, Attendee);
        if (decision.Error is not null)
        {
            await Clients.Caller.SendAsync("Error", decision.Error);
            return;
        }

        var participantList = _roomList.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, Participant>());
        var me = new Participant(Context.ConnectionId, CurrentUserId, CurrentFullName, decision.IsHost);
        participantList[Context.ConnectionId] = me;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(roomId));
        await _session.LogEventAsync(roomId, OnlineClassRoomEventType.Joined, Attendee);

        await Clients.Caller.SendAsync("Joined", new
        {
            self = me,
            otherList = participantList.Values.Where(p => p.ConnectionId != Context.ConnectionId)
        });
        await Clients.OthersInGroup(GroupName(roomId)).SendAsync("ParticipantJoined", me);
    }

    /// <summary>
    /// Người gọi có thật sự đang ở trong phòng này hay không.
    ///
    /// JoinRoom đã kiểm tra quyền vào phòng, nhưng các lệnh phía dưới thì KHÔNG: chúng chỉ nhận một con số
    /// roomId từ trình duyệt. Thiếu bước này thì bất kỳ ai đăng nhập được cũng gọi thẳng
    /// SendChatMessage(roomId bất kỳ) để chèn tin nhắn vào buổi học của phòng khác, hoặc gọi
    /// NotifyRecordingStatus(roomId, false) để tắt chỉ báo "đang ghi hình" trên màn hình mọi người trong khi
    /// buổi học vẫn đang được ghi.
    /// </summary>
    private bool IsInRoom(int roomId) =>
        _roomList.TryGetValue(roomId, out var participantList)
        && participantList.ContainsKey(Context.ConnectionId);

    /// <summary>Chuyển tiếp gói tin báo hiệu WebRTC (SDP offer/answer, ICE candidate) tới đúng người nhận.
    /// Chỉ chuyển trong phạm vi MỘT phòng: hai bên phải cùng có mặt trong cùng một phòng.</summary>
    public async Task Signal(string targetConnectionId, string type, string payload)
    {
        var sharesRoom = _roomList.Values.Any(participantList =>
            participantList.ContainsKey(Context.ConnectionId) && participantList.ContainsKey(targetConnectionId));
        if (!sharesRoom) return;

        await Clients.Client(targetConnectionId).SendAsync("Signal", Context.ConnectionId, type, payload);
    }

    public async Task SendChatMessage(int roomId, string message)
    {
        if (!IsInRoom(roomId)) return;
        if (string.IsNullOrWhiteSpace(message)) return;
        var trimmed = message.Trim();
        await Clients.Group(GroupName(roomId)).SendAsync("ChatMessage", CurrentFullName, trimmed, DateTime.Now);
        await _session.LogEventAsync(roomId, OnlineClassRoomEventType.ChatMessage, Attendee, trimmed);
    }

    /// <summary>Người chủ trì báo hiệu trình duyệt của mình đã bắt đầu/dừng ghi hình thành công — hiển thị chỉ báo
    /// thật (không phải chỉ báo tĩnh) cho mọi người trong phòng biết buổi học có đang thực sự được ghi lại hay không.</summary>
    public async Task NotifyRecordingStatus(int roomId, bool isRecording)
    {
        if (!IsInRoom(roomId)) return;
        await Clients.Group(GroupName(roomId)).SendAsync("RecordingStatus", isRecording);
    }

    /// <summary>Chỉ để hiển thị chỉ báo đang nói/tắt mic trên giao diện người khác — không ảnh hưởng luồng audio thật.</summary>
    public async Task ToggleMic(int roomId, bool isOn)
    {
        if (!IsInRoom(roomId)) return;
        await Clients.OthersInGroup(GroupName(roomId)).SendAsync("ParticipantMicToggled", Context.ConnectionId, isOn);
        await _session.LogEventAsync(roomId, isOn ? OnlineClassRoomEventType.MicOn : OnlineClassRoomEventType.MicOff, Attendee);
    }

    /// <summary>Chỉ để hiển thị chỉ báo đang bật/tắt camera trên giao diện người khác — không ảnh hưởng luồng video thật.</summary>
    public async Task ToggleCam(int roomId, bool isOn)
    {
        if (!IsInRoom(roomId)) return;
        await Clients.OthersInGroup(GroupName(roomId)).SendAsync("ParticipantCamToggled", Context.ConnectionId, isOn);
        await _session.LogEventAsync(roomId, isOn ? OnlineClassRoomEventType.CamOn : OnlineClassRoomEventType.CamOff, Attendee);
    }

    /// <summary>Người chủ trì kết thúc buổi học — mọi người bị ngắt kết nối, trạng thái chuyển Ended.</summary>
    public async Task EndRoom(int roomId)
    {
        if (!IsInRoom(roomId)) return;
        if (!await _session.EndByHostAsync(roomId, Attendee)) return;

        await Clients.Group(GroupName(roomId)).SendAsync("RoomEnded");
        _roomList.TryRemove(roomId, out _);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (roomId, participantList) in _roomList)
        {
            if (!participantList.TryRemove(Context.ConnectionId, out var me)) continue;

            if (me.IsHost)
            {
                await _session.EndAfterHostDisconnectedAsync(roomId, Attendee);
                await Clients.Group(GroupName(roomId)).SendAsync("RoomEnded");
                _roomList.TryRemove(roomId, out _);
            }
            else
            {
                await Clients.Group(GroupName(roomId)).SendAsync("ParticipantLeft", Context.ConnectionId);
                await _session.LogEventAsync(roomId, OnlineClassRoomEventType.Left, Attendee);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private static string GroupName(int roomId) => $"room-{roomId}";
}
