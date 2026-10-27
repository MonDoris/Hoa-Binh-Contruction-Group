// File: Application/Features/AiTutor/IAiTutorService.cs
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiTutor;

/// <summary>Các ca sử dụng của chức năng AiTutor. Tầng API gọi qua cổng này (xem API/Controllers/AiTutorService.cs).</summary>
public interface IAiTutorService
{
    /// <summary>HB Minerva có dùng được ngay không — để trang chat báo trước thay vì để học viên gõ xong câu hỏi
    /// rồi mới nhận lỗi.</summary>
    Task<Result> GetStatus(CancellationToken cancellationToken);

    /// <summary>Hỏi HB Minerva. Trả về dạng SSE (text/event-stream) giống màn hình quản trị: model chạy trên
    /// CPU chỉ sinh được vài chữ mỗi giây, chờ trọn câu trả lời rồi mới hiện thì học viên ngồi nhìn màn hình
    /// trắng cả phút và tưởng hệ thống treo.
    ///
    /// Mỗi lượt hỏi đều được GHI VÀO LỊCH SỬ (xem <see cref="AiChatSession"/>): câu hỏi ghi ngay
    /// trước khi model chạy, câu trả lời ghi khi model viết xong. Nhờ vậy người học đóng trình duyệt giữa
    /// chừng vẫn còn lại câu hỏi trong lịch sử, thay vì mất trắng cả lượt.</summary>
    Task Ask(AiTutorAskRequest request, IEventStream events, CancellationToken cancellationToken);

    /// <summary>Danh sách hội thoại cũ của chính người đang đăng nhập, mới nhất trước.</summary>
    Task<Result> GetSessionList(CancellationToken cancellationToken);

    /// <summary>Mở lại một hội thoại cũ: toàn bộ câu hỏi, câu trả lời và bảng nguồn của từng câu.</summary>
    Task<Result> GetSession(int id, CancellationToken cancellationToken);

    /// <summary>Tải lại một tệp đã gửi kèm câu hỏi — chỉ chủ hội thoại tải được.</summary>
    Task<Result> GetAttachment(int id, CancellationToken cancellationToken);

    /// <summary>Đổi tên một hội thoại. Tên mặc định lấy từ câu hỏi đầu tiên, mà câu hỏi đầu tiên thường là
    /// câu mò đường — vài ngày sau chính người hỏi cũng không nhận ra hội thoại nào là hội thoại nào.</summary>
    Task<Result> RenameSession(int id, AiChatSessionRenameRequest request, CancellationToken cancellationToken);

    /// <summary>Xóa một hội thoại khỏi lịch sử.</summary>
    Task<Result> DeleteSession(int id, CancellationToken cancellationToken);

    /// <summary>Xóa sạch lịch sử hỏi đáp của chính mình.</summary>
    Task<Result> DeleteAllSession(CancellationToken cancellationToken);
}
