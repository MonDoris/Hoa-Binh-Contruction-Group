// File: Application/Features/Feedbacks/IFeedbackService.cs

namespace HbELearning.Application.Features.Feedbacks;

/// <summary>Các ca sử dụng của chức năng Feedback khóa học (bình luận + đánh giá sao).</summary>
public interface IFeedbackService
{
    Task<Result> GetByCourse(int courseId);

    /// <summary>Viết bình luận gốc, hoặc trả lời (request.ParentId có giá trị).</summary>
    Task<Result> AddComment(int courseId, FeedbackCommentRequest request);

    /// <summary>Chấm sao + nhận xét của người đang đăng nhập; đã chấm rồi thì cập nhật.</summary>
    Task<Result> SaveReview(int courseId, FeedbackReviewRequest request);

    /// <summary>Sửa chữ của bình luận / trả lời do chính mình viết.</summary>
    Task<Result> UpdateComment(int id, FeedbackUpdateRequest request);

    Task<Result> Delete(int id);
}
