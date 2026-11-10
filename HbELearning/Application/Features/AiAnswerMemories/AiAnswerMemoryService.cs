// File: Application/Features/AiAnswerMemories/AiAnswerMemoryService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.AiAnswerMemories;

/// <summary>Quản trị các câu trả lời HB Minerva đã GHI NHỚ (xem <see cref="AiAnswerMemory"/>). Chỉ Admin dùng.
///
/// Cần có màn hình này vì một câu ghi nhớ sai sẽ được trả lại cho TẤT CẢ những ai hỏi cùng nội dung, ngay lập tức
/// và trông y như một câu trả lời đúng. Admin phải nhìn thấy được AI đang "nhớ" những gì, sửa được câu sai, và bắt
/// AI quên khi quy định vừa thay.</summary>
public class AiAnswerMemoryService : IAiAnswerMemoryService
{
    private readonly IAppDbContext _db;

    public AiAnswerMemoryService(IAppDbContext db) => _db = db;

    public async Task<Result> GetList(string? keyword, CancellationToken cancellationToken)
    {
        var query = _db.AiAnswerMemory.AsNoTracking();

        var search = keyword?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Question.Contains(search) || x.Answer.Contains(search));

        var totalCount = await query.CountAsync(cancellationToken);
        var totalHitCount = totalCount == 0 ? 0 : await query.SumAsync(x => x.HitCount, cancellationToken);

        var itemList = await query
            .OrderByDescending(x => x.HitCount).ThenByDescending(x => x.LastUsedAt)
            .Take(MaxListCount)
            .Select(x => new AiAnswerMemoryDto(
                x.Id, x.Question, x.Answer, x.HitCount, x.UpdatedAt != null, x.CreatedAt, x.LastUsedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new AiAnswerMemoryListDto(totalCount, totalHitCount, itemList));
    }

    public async Task<Result> Update(int id, AiAnswerMemoryUpdateRequest request, CancellationToken cancellationToken)
    {
        var answer = request.Answer?.Trim() ?? string.Empty;
        // Câu trả lời rỗng thì người hỏi nhận về một bong bóng trống — muốn AI thôi trả lời câu này thì bấm Xóa.
        if (answer.Length == 0) return Result.Invalid(new MessageResponse("Câu trả lời không được để trống."));
        if (answer.Length > MaxAnswerLength)
            return Result.Invalid(new MessageResponse($"Câu trả lời dài quá {MaxAnswerLength} ký tự."));

        var memory = await _db.AiAnswerMemory.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (memory is null) return Result.NotFound(new MessageResponse("Không tìm thấy câu trả lời này."));

        memory.Answer = answer;
        memory.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new MessageResponse("Đã lưu câu trả lời. Người hỏi lại sẽ nhận bản này."));
    }

    public async Task<Result> Delete(int id, CancellationToken cancellationToken)
    {
        var removedCount = await _db.AiAnswerMemory.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);

        return removedCount == 0
            ? Result.NotFound(new MessageResponse("Không tìm thấy câu trả lời này."))
            : Result.Success(new MessageResponse("Đã xóa. Lần tới có người hỏi, AI sẽ trả lời lại từ đầu."));
    }

    public async Task<Result> DeleteAll(CancellationToken cancellationToken)
    {
        var removedCount = await _db.AiAnswerMemory.ExecuteDeleteAsync(cancellationToken);
        return Result.Success(new MessageResponse($"Đã xóa {removedCount} câu trả lời đã ghi nhớ."));
    }

    /// <summary>Số dòng trả về một lần. Bảng có thể lên tới vài nghìn câu; Admin cần xem những câu được dùng lại
    /// nhiều (sai thì ảnh hưởng nhiều người nhất), còn lại thì tìm theo từ khóa.</summary>
    private const int MaxListCount = 200;

    /// <summary>Trần độ dài câu trả lời sửa tay — dài gấp vài lần câu trả lời dài nhất model viết ra được.</summary>
    private const int MaxAnswerLength = 8000;
}
