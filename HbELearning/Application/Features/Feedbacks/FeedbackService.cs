// File: Application/Features/Feedbacks/FeedbackService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Feedbacks;

/// <summary>Feedback của học viên về khóa học: bình luận khi gặp vấn đề (người soạn khóa trả lời trong cùng luồng)
/// và đánh giá sao sau khi hoàn thành khóa.
///
/// Ai thấy gì: người soạn khóa, Admin và các cấp xem được nội dung khóa thấy tất cả. Người học (có phân công chưa
/// hủy) thấy mọi bình luận của khóa nhưng chỉ thấy đánh giá sao của CHÍNH MÌNH — điểm trung bình thì ai cũng thấy.
/// Cố ý không cho người học đọc nhận xét của nhau: một lời chê khóa học viết thẳng thắn sẽ không còn nếu đồng nghiệp
/// cùng phòng đọc được.</summary>
public class FeedbackService : IFeedbackService
{
    private const int MaxNotificationLength = 500;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationDispatcher _notificationService;

    public FeedbackService(IAppDbContext db, ICurrentUser currentUser, INotificationDispatcher notificationService)
    {
        _db = db;
        _currentUser = currentUser;
        _notificationService = notificationService;
    }

    public async Task<Result> GetByCourse(int courseId)
    {
        var course = await _db.Course.AsNoTracking()
            .Where(x => x.Id == courseId).Select(x => new { x.CreatedById }).FirstOrDefaultAsync();
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var userId = _currentUser.UserId;
        var isOwner = IsOwner(course.CreatedById);
        var isStaff = isOwner || _currentUser.IsContentViewer || _currentUser.IsInRole(RoleName.ManagingDirector);
        var enrollment = await FindEnrollmentAsync(courseId);

        if (!isStaff && enrollment is null)
            return Result.Forbidden();

        var query = _db.CourseFeedback.AsNoTracking().Where(x => x.CourseId == courseId);
        if (!isStaff)
            query = query.Where(x => x.Kind == FeedbackKind.Comment || x.UserId == userId);

        var rowList = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.ParentId, x.Kind, x.Content, x.Rating, x.UserId,
                AuthorName = x.User.FullName, x.CreatedAt, x.UpdatedAt
            })
            .ToListAsync();

        var ratingList = await _db.CourseFeedback.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.Kind == FeedbackKind.Review && x.Rating != null)
            .Select(x => x.Rating!.Value).ToListAsync();
        var starCountList = Enumerable.Range(1, 5).Select(star => ratingList.Count(r => r == star)).ToList();
        decimal? average = ratingList.Count == 0 ? null : Math.Round((decimal)ratingList.Average(), 1);

        var itemList = rowList.Select(x => new CourseFeedbackDto(
            x.Id, x.ParentId, x.Kind.ToString(), x.Content, x.Rating,
            x.UserId, x.AuthorName, x.UserId == userId, x.CreatedAt, x.UpdatedAt)).ToList();

        return Result.Success(new CourseFeedbackListDto(
            new FeedbackSummaryDto(average, ratingList.Count, starCountList),
            CanComment: enrollment is not null,
            CanReview: enrollment?.Status == EnrollmentStatus.Completed,
            IsOwner: isOwner, IsStaff: isStaff, ItemList: itemList));
    }

    public async Task<Result> AddComment(int courseId, FeedbackCommentRequest request)
    {
        var content = request.Content.Trim();
        if (content.Length == 0)
            return Result.Invalid(new MessageResponse("Nội dung đang để trống."));

        var course = await _db.Course.AsNoTracking()
            .Where(x => x.Id == courseId).Select(x => new { x.CreatedById, x.Title }).FirstOrDefaultAsync();
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var userId = _currentUser.UserId;
        var isOwner = IsOwner(course.CreatedById);
        var enrollment = await FindEnrollmentAsync(courseId);
        CourseFeedback? parent = null;

        if (request.ParentId.HasValue)
        {
            parent = await _db.CourseFeedback.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ParentId.Value && x.CourseId == courseId);
            if (parent is null) return Result.NotFound(new MessageResponse("Không tìm thấy bình luận cần trả lời."));
            if (parent.Kind != FeedbackKind.Comment || parent.ParentId.HasValue)
                return Result.Invalid(new MessageResponse("Chỉ trả lời được bình luận gốc."));

            // Người soạn khóa / Admin trả lời mọi luồng; người học chỉ nối tiếp luồng do chính mình mở.
            if (!isOwner && parent.UserId != userId) return Result.Forbidden();
        }
        else if (enrollment is null)
        {
            return Result.Invalid(new MessageResponse("Bạn cần được phân công khóa học này mới bình luận được."));
        }

        var feedback = new CourseFeedback
        {
            CourseId = courseId,
            UserId = userId,
            EnrollmentId = enrollment?.Id,
            ParentId = parent?.Id,
            Kind = FeedbackKind.Comment,
            Content = content,
        };
        _db.CourseFeedback.Add(feedback);
        await _db.SaveChangesAsync();

        // Người học viết → báo người soạn khóa. Người soạn trả lời → báo chủ luồng. Không tự báo cho chính mình.
        var recipientId = parent is not null && parent.UserId != userId ? parent.UserId : course.CreatedById;
        if (recipientId != userId)
        {
            var title = parent is null ? "Có bình luận mới về khóa học" : "Có trả lời mới về bình luận";
            var verb = parent is null ? "đã bình luận" : "đã trả lời";
            await NotifyAsync(courseId, recipientId, title,
                $"{_currentUser.FullName} {verb} ở khóa học \"{course.Title}\": {content}");
        }

        return Result.Success(await ToDtoAsync(feedback.Id));
    }

    public async Task<Result> SaveReview(int courseId, FeedbackReviewRequest request)
    {
        if (request.Rating is < 1 or > 5)
            return Result.Invalid(new MessageResponse("Số sao phải từ 1 đến 5."));

        var course = await _db.Course.AsNoTracking()
            .Where(x => x.Id == courseId).Select(x => new { x.CreatedById, x.Title }).FirstOrDefaultAsync();
        if (course is null) return Result.NotFound(new MessageResponse("Không tìm thấy khóa học."));

        var userId = _currentUser.UserId;
        var enrollment = await FindEnrollmentAsync(courseId);
        if (enrollment is null || enrollment.Status != EnrollmentStatus.Completed)
            return Result.Invalid(new MessageResponse("Chỉ đánh giá được sau khi hoàn thành khóa học."));

        var content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim();

        var review = await _db.CourseFeedback.FirstOrDefaultAsync(x =>
            x.CourseId == courseId && x.UserId == userId && x.Kind == FeedbackKind.Review);
        var isNew = review is null;
        if (review is null)
        {
            review = new CourseFeedback { CourseId = courseId, UserId = userId, Kind = FeedbackKind.Review };
            _db.CourseFeedback.Add(review);
        }
        else
        {
            review.UpdatedAt = DateTime.Now;
        }

        review.EnrollmentId = enrollment.Id;
        review.Rating = request.Rating;
        review.Content = content;
        await _db.SaveChangesAsync();

        if (isNew && course.CreatedById != userId)
            await NotifyAsync(courseId, course.CreatedById, "Có đánh giá mới về khóa học",
                $"{_currentUser.FullName} đánh giá {request.Rating}/5 sao cho khóa học \"{course.Title}\".");

        return Result.Success(await ToDtoAsync(review.Id));
    }

    public async Task<Result> UpdateComment(int id, FeedbackUpdateRequest request)
    {
        var feedback = await _db.CourseFeedback.FirstOrDefaultAsync(x => x.Id == id);
        if (feedback is null) return Result.NotFound(new MessageResponse("Không tìm thấy phản hồi."));
        if (feedback.Kind != FeedbackKind.Comment)
            return Result.Invalid(new MessageResponse("Đánh giá sao được sửa ở mục Đánh giá."));
        if (feedback.UserId != _currentUser.UserId) return Result.Forbidden();

        var content = request.Content.Trim();
        if (content.Length == 0)
            return Result.Invalid(new MessageResponse("Nội dung đang để trống."));

        feedback.Content = content;
        feedback.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Result.Success(await ToDtoAsync(feedback.Id));
    }

    public async Task<Result> Delete(int id)
    {
        var feedback = await _db.CourseFeedback.FirstOrDefaultAsync(x => x.Id == id);
        if (feedback is null) return Result.NotFound(new MessageResponse("Không tìm thấy phản hồi."));

        var courseOwnerId = await _db.Course.AsNoTracking()
            .Where(x => x.Id == feedback.CourseId).Select(x => x.CreatedById).FirstOrDefaultAsync();
        if (feedback.UserId != _currentUser.UserId && !IsOwner(courseOwnerId)) return Result.Forbidden();

        // Khóa ngoại ParentId là NoAction (SqlServer không cho tự lan truyền trên chính bảng) nên trả lời phải đi trước.
        if (feedback.Kind == FeedbackKind.Comment && !feedback.ParentId.HasValue)
            _db.CourseFeedback.RemoveRange(_db.CourseFeedback.Where(x => x.ParentId == id));

        _db.CourseFeedback.Remove(feedback);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa phản hồi."));
    }

    /// <summary>Người soạn khóa hoặc Admin: trả lời mọi luồng, xóa mọi phản hồi.</summary>
    private bool IsOwner(int courseCreatedById) => _currentUser.IsAdmin || courseCreatedById == _currentUser.UserId;

    /// <summary>Phân công chưa hủy của người đang đăng nhập với khóa này; ưu tiên bản đã Hoàn thành.</summary>
    private async Task<EnrollmentInfo?> FindEnrollmentAsync(int courseId)
    {
        var userId = _currentUser.UserId;
        var list = await _db.Enrollment.AsNoTracking()
            .Where(x => x.CourseId == courseId && x.UserId == userId && x.Status != EnrollmentStatus.Cancelled)
            .Select(x => new EnrollmentInfo(x.Id, x.Status))
            .ToListAsync();
        return list.FirstOrDefault(x => x.Status == EnrollmentStatus.Completed) ?? list.FirstOrDefault();
    }

    private async Task<CourseFeedbackDto> ToDtoAsync(int id)
    {
        var x = await _db.CourseFeedback.AsNoTracking().Where(f => f.Id == id)
            .Select(f => new
            {
                f.Id, f.ParentId, f.Kind, f.Content, f.Rating, f.UserId,
                AuthorName = f.User.FullName, f.CreatedAt, f.UpdatedAt
            })
            .FirstAsync();
        return new CourseFeedbackDto(x.Id, x.ParentId, x.Kind.ToString(), x.Content, x.Rating,
            x.UserId, x.AuthorName, x.UserId == _currentUser.UserId, x.CreatedAt, x.UpdatedAt);
    }

    private Task NotifyAsync(int courseId, int recipientId, string title, string message)
    {
        if (message.Length > MaxNotificationLength)
            message = message[..(MaxNotificationLength - 1)] + "…";
        return _notificationService.NotifyCourseWorkflowAsync(
            courseId, new List<int> { recipientId }, NotificationType.CourseFeedback, title, message);
    }

    private sealed record EnrollmentInfo(int Id, EnrollmentStatus Status);
}
