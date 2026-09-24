// File: Application/Common/Interfaces/IChatNotifier.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Đẩy sự kiện của hộp chat nội bộ tới máy của người nhận ngay lập tức.
///
/// Trước khi tách lớp, nghiệp vụ chat gọi thẳng IHubContext&lt;ChatHub&gt; — tức lớp nghiệp vụ biết cả SignalR lẫn
/// tên lớp Hub nằm ở tầng web. Giờ nó chỉ nói "báo cho những người này rằng...", còn đi bằng SignalR là
/// chuyện của tầng API (xem API/Services/SignalRChatNotifier.cs).
///
/// Tên sự kiện và thứ tự đối số ở phía giao diện (wwwroot/js) KHÔNG đổi — xem lớp hiện thực.
/// </summary>
public interface IChatNotifier
{
    /// <summary>Hội thoại vừa đổi (lập nhóm, đổi tên, thêm/bớt người, đổi nền...) — máy nhận tải lại danh sách.</summary>
    Task NotifyConversationChangedAsync(int userId, int conversationId);

    /// <inheritdoc cref="NotifyConversationChangedAsync(int, int)"/>
    Task NotifyConversationChangedAsync(IEnumerable<int> userIdList, int conversationId);

    /// <summary>Có tin nhắn mới.</summary>
    Task SendChatMessageAsync(IEnumerable<int> userIdList, object message);

    /// <summary>Một tin nhắn vừa bị thu hồi.</summary>
    Task NotifyMessageDeletedAsync(IEnumerable<int> userIdList, int conversationId, int messageId);
}
