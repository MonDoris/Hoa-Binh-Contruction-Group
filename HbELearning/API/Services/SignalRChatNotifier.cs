// File: API/Services/SignalRChatNotifier.cs
using Microsoft.AspNetCore.SignalR;

namespace HbELearning.API.Services;

/// <summary>
/// Hiện thực IChatNotifier bằng SignalR (ChatHub).
///
/// Tên sự kiện ("ConversationChanged", "ChatMessage", "MessageDeleted") và thứ tự đối số chép nguyên từ các lời
/// gọi _hub.Clients... từng nằm trong ChatController — giao diện đang lắng nghe đúng những tên này, đổi một chữ
/// là tin nhắn không còn hiện ra theo thời gian thực mà không có lỗi nào báo.
///
/// Clients.User("42") tới được MỌI thiết bị của tài khoản 42 vì ChatHub định danh người dùng bằng claim
/// NameIdentifier (xem ChatHub).
/// </summary>
public sealed class SignalRChatNotifier : IChatNotifier
{
    private readonly IHubContext<ChatHub> _hub;

    public SignalRChatNotifier(IHubContext<ChatHub> hub) => _hub = hub;

    public Task NotifyConversationChangedAsync(int userId, int conversationId)
        => _hub.Clients.User(userId.ToString()).SendAsync("ConversationChanged", conversationId);

    public Task NotifyConversationChangedAsync(IEnumerable<int> userIdList, int conversationId)
        => _hub.Clients.Users(userIdList.Select(x => x.ToString())).SendAsync("ConversationChanged", conversationId);

    public Task SendChatMessageAsync(IEnumerable<int> userIdList, object message)
        => _hub.Clients.Users(userIdList.Select(x => x.ToString())).SendAsync("ChatMessage", message);

    public Task NotifyMessageDeletedAsync(IEnumerable<int> userIdList, int conversationId, int messageId)
        => _hub.Clients.Users(userIdList.Select(x => x.ToString())).SendAsync("MessageDeleted", conversationId, messageId);
}
