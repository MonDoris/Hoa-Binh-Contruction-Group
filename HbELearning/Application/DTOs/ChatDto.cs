// File: Application/DTOs/ChatDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Một dòng trong danh sách hộp thoại.
///
/// CreatedByUserId là người lập nhóm — chỉ người này được đổi tên nhóm, đổi ảnh nhóm và mời/mời ra thành
/// viên. Cuộc 1-1 vẫn có giá trị này nhưng nó vô nghĩa ở đó, giao diện không dùng tới.
///
/// AvatarUrl: cuộc 1-1 trả ảnh của người đối diện, nhóm trả ảnh của chính nhóm.
/// BackgroundKey / BackgroundUrl là nền khung chat của RIÊNG người đang hỏi, không phải của cả hội thoại:
/// một bên là tên mẫu nền dựng sẵn, một bên là ảnh họ tự tải lên — luôn chỉ một trong hai có giá trị.</summary>
public record ConversationDto(
    int Id, string Type, string Title, string? AvatarUrl,
    int? PartnerUserId, int MemberCount,
    string? LastMessage, bool LastMessageIsMine, DateTime? LastMessageAt,
    int UnreadCount, int CreatedByUserId, string? BackgroundKey, string? BackgroundUrl);

/// <summary>Một tin nhắn đã lưu.
///
/// MessageType trả TÊN của <see cref="Helpers.ChatMessageType"/> ("Text" / "Image" / "File" / "Sticker" /
/// "Audio") chứ không trả số: giao diện so chuỗi thì đọc được ngay, mà thêm kiểu mới về sau cũng không phải
/// đi sửa một bảng tra số học thuộc lòng nằm rải rác trong js.</summary>
public record ChatMessageDto(
    int Id, int ConversationId, int SenderUserId, string SenderName, string? SenderAvatarUrl,
    string Content, string? AttachmentUrl, bool IsDeleted, DateTime CreatedAt,
    string MessageType, string? AttachmentName, long AttachmentSize);

/// <summary>Gửi một tin. Ba trường đính kèm để trống với tin chữ thuần và tin nhãn dán.
/// MessageType nhận đúng tên của <see cref="Helpers.ChatMessageType"/>; để trống thì hiểu là "Text".</summary>
public record SendMessageRequest(
    string? Content, string? AttachmentUrl, string? MessageType, string? AttachmentName, long AttachmentSize);

/// <summary>Một người có thể bắt chuyện — dùng cho ô tìm kiếm "Nhắn tin mới".</summary>
public record ChatContactDto(int Id, string FullName, string? AvatarUrl, string? DepartmentName, string EmployeeCode);

// ==================== NHÓM CHAT ====================

public record CreateGroupRequest(string Title, List<int> MemberIdList);

public record RenameGroupRequest(string Title);

public record AddGroupMemberRequest(List<int> MemberIdList);

/// <summary>Đổi nền khung chat. Để trống / "default" là quay về nền mặc định.</summary>
public record SetBackgroundRequest(string? BackgroundKey);

/// <summary>Một thành viên trong bảng "Thành viên nhóm".</summary>
public record GroupMemberDto(
    int UserId, string FullName, string? AvatarUrl, string? DepartmentName, bool IsOwner, DateTime JoinedAt);
