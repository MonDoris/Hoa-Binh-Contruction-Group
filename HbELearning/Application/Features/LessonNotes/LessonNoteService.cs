// File: Application/Features/LessonNotes/LessonNoteService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.LessonNotes;

/// <summary>Ghi chú của học viên gắn với mốc thời gian trong video bài học.
///
/// Đây là sổ tay RIÊNG của từng người: không ai — kể cả quản trị viên hay người soạn bài — đọc được ghi chú
/// của người khác qua API này. Học viên ghi thật lòng ("đoạn này nghe không hiểu gì") chỉ khi chắc chắn
/// không có ai đọc; biết là sếp xem được thì họ sẽ viết cho sếp đọc, và tính năng mất sạch tác dụng.</summary>
public class LessonNoteService : ILessonNoteService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public LessonNoteService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList(int enrollmentId, int lessonId)
    {
        var denied = await CheckOwnerAsync(enrollmentId);
        if (denied is not null) return denied;

        return Result.Success(await _db.LessonNote.AsNoTracking()
            .Where(x => x.EnrollmentId == enrollmentId && x.LessonId == lessonId)
            .OrderBy(x => x.AtSecond).ThenBy(x => x.Id)
            .Select(x => new LessonNoteDto(x.Id, x.LessonId, x.AtSecond, x.Content, x.CreatedAt, x.UpdatedAt))
            .ToListAsync());
    }

    public async Task<Result> Create(LessonNoteCreateRequest request)
    {
        var denied = await CheckOwnerAsync(request.EnrollmentId);
        if (denied is not null) return denied;

        var content = request.Content.Trim();
        if (content.Length == 0)
            return Result.Invalid(new MessageResponse("Ghi chú đang để trống."));

        // Bài học phải thuộc đúng khóa của bản ghi phân công này. Thiếu kiểm tra thì một người học có thể
        // gắn ghi chú vào bài của khóa mình không được phân công, chỉ bằng cách đổi lessonId trong request.
        var belongs = await _db.Lesson.AsNoTracking().AnyAsync(x => x.Id == request.LessonId
            && _db.Enrollment.Any(e => e.Id == request.EnrollmentId && e.CourseId == x.CourseId));
        if (!belongs) return Result.Invalid(new MessageResponse("Bài học không thuộc khóa học này."));

        var note = new LessonNote
        {
            EnrollmentId = request.EnrollmentId,
            LessonId = request.LessonId,
            AtSecond = request.AtSecond,
            Content = content,
        };
        _db.LessonNote.Add(note);
        await _db.SaveChangesAsync();

        return Result.Success(new LessonNoteDto(note.Id, note.LessonId, note.AtSecond, note.Content, note.CreatedAt, note.UpdatedAt));
    }

    public async Task<Result> Update(int id, LessonNoteUpdateRequest request)
    {
        var note = await _db.LessonNote.FirstOrDefaultAsync(x => x.Id == id);
        if (note is null) return Result.NotFound(new MessageResponse("Không tìm thấy ghi chú."));

        var denied = await CheckOwnerAsync(note.EnrollmentId);
        if (denied is not null) return denied;

        var content = request.Content.Trim();
        if (content.Length == 0)
            return Result.Invalid(new MessageResponse("Ghi chú đang để trống."));

        note.Content = content;
        note.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Result.Success(new LessonNoteDto(note.Id, note.LessonId, note.AtSecond, note.Content, note.CreatedAt, note.UpdatedAt));
    }

    public async Task<Result> Delete(int id)
    {
        var note = await _db.LessonNote.FirstOrDefaultAsync(x => x.Id == id);
        if (note is null) return Result.NotFound(new MessageResponse("Không tìm thấy ghi chú."));

        var denied = await CheckOwnerAsync(note.EnrollmentId);
        if (denied is not null) return denied;

        _db.LessonNote.Remove(note);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa ghi chú."));
    }

    /// <summary>Chỉ CHỦ của bản ghi phân công được đụng vào ghi chú của nó.
    ///
    /// Cố ý KHÔNG mở cho quản trị viên hay người soạn bài như những API học tập khác: ghi chú là sổ tay
    /// riêng, và "quản lý xem được" là đủ để người học ngừng viết thật.</summary>
    private async Task<Result?> CheckOwnerAsync(int enrollmentId)
    {
        var enrollment = await _db.Enrollment.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));
        if (enrollment.UserId != _currentUser.UserId) return Result.Forbidden();
        return null;
    }
}
