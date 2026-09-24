// File: Application/Features/Chat/IChatService.cs

namespace HbELearning.Application.Features.Chat;

/// <summary>Các ca sử dụng của chức năng Chat. Tầng API gọi qua cổng này (xem API/Controllers/ChatService.cs).</summary>
public interface IChatService
{
    Task<Result> GetConversationList();

    /// <summary>Tổng số tin chưa đọc — cho huy hiệu trên nút chat nổi.</summary>
    Task<Result> GetUnreadCount();

    /// <summary>Lấy cuộc 1-1 với người này, chưa có thì tạo. Gọi lại nhiều lần vẫn trả về đúng một cuộc.</summary>
    Task<Result> OpenDirect(int userId);

    /// <summary>Lập một nhóm mới. Người lập mặc nhiên là thành viên, không phải tự tick tên mình.</summary>
    Task<Result> CreateGroup(CreateGroupRequest request);

    /// <summary>Đổi tên nhóm. Chỉ người lập nhóm (hoặc Admin) được đổi.</summary>
    Task<Result> RenameGroup(int id, RenameGroupRequest request);

    /// <summary>Đổi ảnh nhóm. Chỉ người lập nhóm (hoặc Admin).</summary>
    Task<Result> ChangeGroupAvatar(int id, FileUpload file);

    /// <summary>Gỡ ảnh nhóm, quay về biểu tượng nhóm mặc định.</summary>
    Task<Result> RemoveGroupAvatar(int id);

    /// <summary>Đổi nền khung chat của RIÊNG mình trong một hội thoại (cả 1-1 lẫn nhóm).
    ///
    /// Ai cũng đổi được, không cần quyền gì ngoài việc là thành viên: nền lưu ở dòng thành viên nên nó chỉ đổi
    /// màn hình của chính người bấm — xem ConversationMember.BackgroundKey.</summary>
    Task<Result> SetBackground(int id, SetBackgroundRequest request);

    /// <summary>Danh sách thành viên của một hội thoại — dùng cho bảng quản lý nhóm.</summary>
    Task<Result> GetMemberList(int id);

    /// <summary>Mời thêm người vào nhóm. Chỉ người lập nhóm (hoặc Admin).</summary>
    Task<Result> AddMember(int id, AddGroupMemberRequest request);

    /// <summary>Mời một người ra khỏi nhóm. Chỉ người lập nhóm (hoặc Admin) — tự rời thì dùng "leave".</summary>
    Task<Result> RemoveMember(int id, int userId);

    /// <summary>Tự rời nhóm.</summary>
    Task<Result> LeaveGroup(int id);

    /// <summary>Tải một ảnh của riêng mình lên làm nền hội thoại (cả 1-1 lẫn nhóm).
    ///
    /// Ai cũng làm được và chỉ đổi màn hình của chính người bấm, cùng lý do với SetBackground: nền lưu ở
    /// dòng thành viên chứ không ở hội thoại.</summary>
    Task<Result> SetBackgroundImage(int id, FileUpload file);

    /// <summary>Lịch sử hội thoại. Truyền beforeId để tải tiếp trang cũ hơn khi cuộn lên.</summary>
    Task<Result> GetMessageList(int id, int? beforeId);

    Task<Result> SendMessage(int id, SendMessageRequest request);

    /// <summary>Đánh dấu đã đọc tới tin cuối cùng của hội thoại.</summary>
    Task<Result> MarkRead(int id);

    /// <summary>Thu hồi tin nhắn của chính mình.</summary>
    Task<Result> DeleteMessage(int id);

    /// <summary>Tải một tệp đính kèm lên rồi trả về đường dẫn để gắn vào tin nhắn.
    ///
    /// CÓ endpoint riêng chứ không dùng chung /api/upload: chỗ đó chỉ mở cửa cho Admin/Trainer (trừ ảnh đại
    /// diện), mà nhắn tin thì ai cũng phải gửi được ảnh. Trần cỡ tệp ở đây cũng thấp hơn hẳn — xem
    /// <see cref="MaxAttachmentByte"/>. Đuôi tệp cho phép vẫn lấy theo Upload:AllowedExtension trong
    /// appsettings.json, do FileService kiểm.</summary>
    Task<Result> UploadAttachment(FileUpload file);

    Task<Result> GetContactList(string? keyword);
}
