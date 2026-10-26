// File: Application/DTOs/FeedbackDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

/// <summary>Một dòng phản hồi. Bình luận gốc và đánh giá có ParentId = null; trả lời trỏ về bình luận gốc.</summary>
public record CourseFeedbackDto(int Id, int? ParentId, string Kind, string? Content, int? Rating,
    int UserId, string AuthorName, bool IsMine, DateTime CreatedAt, DateTime? UpdatedAt);

/// <param name="StarCountList">Năm phần tử: số lượt đánh giá 1, 2, 3, 4, 5 sao.</param>
public record FeedbackSummaryDto(decimal? Average, int ReviewCount, IReadOnlyList<int> StarCountList);

/// <summary>Toàn bộ phản hồi của một khóa kèm các cờ cho giao diện biết người xem được làm gì.</summary>
/// <param name="CanComment">Có phân công chưa hủy → được viết bình luận.</param>
/// <param name="CanReview">Phân công đã Hoàn thành → được chấm sao (đã chấm rồi thì là sửa).</param>
/// <param name="IsOwner">Là người soạn khóa hoặc Admin → trả lời mọi bình luận và xóa mọi phản hồi.</param>
/// <param name="IsStaff">Thấy đủ đánh giá của mọi người (người học chỉ thấy đánh giá của chính mình).</param>
public record CourseFeedbackListDto(FeedbackSummaryDto Summary, bool CanComment, bool CanReview,
    bool IsOwner, bool IsStaff, IReadOnlyList<CourseFeedbackDto> ItemList);

public class FeedbackCommentRequest
{
    [Required, MaxLength(2000)] public string Content { get; set; } = null!;

    /// <summary>Có giá trị = trả lời một bình luận gốc.</summary>
    public int? ParentId { get; set; }
}

public class FeedbackReviewRequest
{
    [Range(1, 5)] public int Rating { get; set; }
    [MaxLength(2000)] public string? Content { get; set; }
}

public class FeedbackUpdateRequest
{
    [Required, MaxLength(2000)] public string Content { get; set; } = null!;
}
