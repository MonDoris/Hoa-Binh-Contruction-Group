// File: Application/Features/AiQuickReplies/IAiQuickReplyService.cs
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiQuickReplies;

/// <summary>Các ca sử dụng của chức năng AiQuickReply. Tầng API gọi qua cổng này (xem API/Controllers/AiQuickReplyService.cs).</summary>
public interface IAiQuickReplyService
{
    Task<Result> GetList(CancellationToken cancellationToken);

    Task<Result> Create(AiQuickReplyRequest request, CancellationToken cancellationToken);

    Task<Result> Update(int id, AiQuickReplyRequest request, CancellationToken cancellationToken);

    Task<Result> Delete(int id, CancellationToken cancellationToken);

    /// <summary>Thử một câu hỏi mẫu xem nó rơi vào dòng nào — để quản trị viên biết trước, thay vì phải mở
    /// trang HB Minerva ở một tài khoản khác rồi gõ thử.
    ///
    /// Chạy qua đúng hàm so khớp mà lượt hỏi thật dùng (<see cref="QuickReplyMatcher"/>), nên kết quả ở đây
    /// là thứ người dùng thật sẽ nhận, không phải một bản mô phỏng gần đúng.</summary>
    Task<Result> Test(AiTutorAskRequest request, CancellationToken cancellationToken);
}
