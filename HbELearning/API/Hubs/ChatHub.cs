// File: API/Hubs/ChatHub.cs
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HbELearning.API.Hubs;

/// <summary>Kênh thời gian thực của hộp chat nội bộ.
///
/// Hub này CỐ Ý không có phương thức gửi tin: việc gửi đi qua REST (ChatController.SendMessage) rồi server
/// tự đẩy xuống đây. Nếu để trình duyệt invoke thẳng lên hub, lúc websocket rớt giữa chừng thì tin nhắn
/// biến mất im lặng — người dùng thấy chữ mình gõ hiện lên rồi mất, mà không có mã lỗi nào để hiện toast.
///
/// Không cần quản lý Group thủ công: SignalR mặc định lấy ClaimTypes.NameIdentifier làm định danh người
/// dùng (TokenService đặt đúng claim đó), nên Clients.User("42") tới được MỌI thiết bị của tài khoản 42.</summary>
[Authorize]
public class ChatHub : Hub
{
    /// <summary>userId -> số kết nối đang mở. Đếm chứ không lưu cờ true/false: một người mở 3 tab rồi đóng
    /// 1 tab thì họ vẫn đang online.</summary>
    private static readonly ConcurrentDictionary<int, int> _onlineCount = new();

    private int CurrentUserId =>
        int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public override async Task OnConnectedAsync()
    {
        var count = _onlineCount.AddOrUpdate(CurrentUserId, 1, (_, old) => old + 1);
        if (count == 1) await Clients.Others.SendAsync("PresenceChanged", CurrentUserId, true);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var count = _onlineCount.AddOrUpdate(CurrentUserId, 0, (_, old) => Math.Max(0, old - 1));
        if (count == 0)
        {
            _onlineCount.TryRemove(CurrentUserId, out _);
            await Clients.Others.SendAsync("PresenceChanged", CurrentUserId, false);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Danh sách id đang online — trình duyệt hỏi một lần lúc mở hộp chat, sau đó nghe PresenceChanged.</summary>
    public Task<int[]> GetOnlineUserIdList() => Task.FromResult(_onlineCount.Keys.ToArray());

    /// <summary>Báo "đang gõ" tới những người còn lại. Chỉ để hiển thị, không ghi gì vào DB.</summary>
    public Task Typing(int conversationId, int[] receiverUserIdList) =>
        Clients.Users(receiverUserIdList.Select(x => x.ToString()))
               .SendAsync("Typing", conversationId, CurrentUserId);
}
