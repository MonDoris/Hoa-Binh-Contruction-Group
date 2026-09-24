// File: Application/Features/AiQuickReplies/AiQuickReplyService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HbELearning.Application.Features.AiQuickReplies;

/// <summary>Quản trị các câu trả lời CÀI SẴN của Pico B — chào hỏi, cảm ơn, "bạn là ai"... Chỉ Admin dùng:
/// đây là lời của hệ thống nói với toàn công ty, không phải nội dung đào tạo của một khóa học.
///
/// Xem <see cref="AiQuickReply"/> để biết vì sao những câu này không nên đi qua model.</summary>
public class AiQuickReplyService : IAiQuickReplyService
{
    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;

    /// <summary>Khóa đệm của danh sách câu cài sẵn. Đặt ở đây và dùng chung với AiTutorService: nơi SỬA
    /// và nơi ĐỌC phải nói về đúng một khóa, nếu không thì sửa xong phải chờ hết hạn đệm mới thấy hiệu lực.</summary>
    public const string CacheKey = "ai-quick-reply-list";
    private readonly ICurrentUser _currentUser;

    public AiQuickReplyService(IAppDbContext db, IMemoryCache cache, ICurrentUser currentUser)
    {
        _db = db;
        _cache = cache;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList(CancellationToken cancellationToken)
        => Result.Success(await _db.AiQuickReply.AsNoTracking()
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
            .Select(x => new AiQuickReplyDto(
                x.Id, x.Keyword, x.Answer, x.MatchType.ToString(), x.IsActive, x.OrderIndex, x.CreatedAt))
            .ToListAsync(cancellationToken));

    public async Task<Result> Create(AiQuickReplyRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error) return Result.Invalid(new MessageResponse(error));

        var reply = new AiQuickReply
        {
            Keyword = request.Keyword.Trim(),
            Answer = request.Answer.Trim(),
            MatchType = ParseMatchType(request.MatchType),
            IsActive = request.IsActive,
            OrderIndex = request.OrderIndex
        };

        _db.AiQuickReply.Add(reply);
        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(CacheKey);

        return Result.Success(ToDto(reply));
    }

    public async Task<Result> Update(int id, AiQuickReplyRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error) return Result.Invalid(new MessageResponse(error));

        var reply = await _db.AiQuickReply.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reply is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu trả lời cài sẵn này."));

        reply.Keyword = request.Keyword.Trim();
        reply.Answer = request.Answer.Trim();
        reply.MatchType = ParseMatchType(request.MatchType);
        reply.IsActive = request.IsActive;
        reply.OrderIndex = request.OrderIndex;
        reply.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(CacheKey);

        return Result.Success(ToDto(reply));
    }

    public async Task<Result> Delete(int id, CancellationToken cancellationToken)
    {
        var removedCount = await _db.AiQuickReply.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
        _cache.Remove(CacheKey);

        return removedCount == 0
            ? Result.NotFound(new MessageResponse("Không tìm thấy câu trả lời cài sẵn này."))
            : Result.Success(new MessageResponse("Đã xóa câu trả lời cài sẵn."));
    }

    public async Task<Result> Test(AiTutorAskRequest request, CancellationToken cancellationToken)
    {
        var replyList = await _db.AiQuickReply.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.OrderIndex).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var matched = QuickReplyMatcher.Match(request.Question ?? string.Empty, replyList);
        if (matched is null) return Result.Success(new AiQuickReplyTestResult(false, null, null, null));

        var profile = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId)
            .Select(x => new { x.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new AiQuickReplyTestResult(
            true, matched.Id, matched.Keyword,
            QuickReplyMatcher.ApplyPlaceholder(matched.Answer, profile?.FullName, _currentUser.Role)));
    }

    /// <summary>Từ khóa rỗng thì dòng đó không bao giờ khớp gì; câu trả lời rỗng thì người hỏi nhận về một
    /// bong bóng trống. Cả hai đều là dòng rác nằm lại trong bảng mà không ai nhận ra là hỏng.</summary>
    private static string? Validate(AiQuickReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword)) return "Chưa nhập từ khóa kích hoạt.";
        if (string.IsNullOrWhiteSpace(request.Answer)) return "Chưa nhập câu trả lời.";
        if (request.Keyword.Length > 500) return "Danh sách từ khóa dài quá 500 ký tự.";
        if (request.Answer.Length > 2000) return "Câu trả lời dài quá 2000 ký tự.";

        // Từ khóa chỉ toàn dấu câu/khoảng trắng thì sau khi chuẩn hóa còn lại chuỗi rỗng — dòng vẫn lưu được
        // nhưng vĩnh viễn không khớp gì. Chặn ngay để người soạn sửa lại thay vì ngồi đoán vì sao không chạy.
        if (!QuickReplyMatcher.SplitKeywordList(request.Keyword).Any())
            return "Từ khóa không hợp lệ — hãy nhập chữ, các từ khóa ngăn nhau bằng dấu |";

        return null;
    }

    private static QuickReplyMatchType ParseMatchType(string? raw)
        => Enum.TryParse<QuickReplyMatchType>(raw, ignoreCase: true, out var value)
            ? value
            : QuickReplyMatchType.Exact;

    private static AiQuickReplyDto ToDto(AiQuickReply x)
        => new(x.Id, x.Keyword, x.Answer, x.MatchType.ToString(), x.IsActive, x.OrderIndex, x.CreatedAt);
}
