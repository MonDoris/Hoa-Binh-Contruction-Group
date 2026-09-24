// File: Application/Features/ApprovalDelegates/ApprovalDelegateService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.ApprovalDelegates;

/// <summary>Giám Đốc Nhân sự ủy quyền duyệt xuất bản nội dung (bước cuối chuỗi duyệt) cho ĐÚNG MỘT nhân viên
/// CÙNG PHÒNG BAN với mình — Giám Đốc Nhân sự ủy quyền cho một người trong phòng Nhân sự. Người được ủy quyền
/// có thể Duyệt/Từ chối khóa học đang chờ Giám Đốc Nhân sự thay cho Giám Đốc, dù không giữ vai trò Giám Đốc,
/// và tên họ hiện lên bước cuối của thanh lộ trình duyệt mà Giám Đốc Nhân sự nhìn thấy.</summary>
public class ApprovalDelegateService : IApprovalDelegateService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ApprovalDelegateService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> GetCandidateList()
    {
        if (!await IsHrDirectorAsync()) return Result.Forbidden();

        var candidateList = await CandidateQuery()
            .OrderBy(x => x.FullName)
            .Select(x => new { x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl, PositionName = x.Position!.Name })
            .ToListAsync();

        var candidateIdList = candidateList.Select(x => x.Id).ToList();
        var delegatedIdSet = (await _db.ApprovalDelegate.AsNoTracking()
            .Where(x => candidateIdList.Contains(x.UserId))
            .Select(x => x.UserId)
            .ToListAsync()).ToHashSet();

        return Result.Success(candidateList.Select(x => new ApprovalDelegateCandidateDto(
            x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl, x.PositionName, delegatedIdSet.Contains(x.Id))));
    }

    public async Task<Result> SetDelegate(SetApprovalDelegateRequest request)
    {
        if (!await IsHrDirectorAsync()) return Result.Forbidden();

        var candidateIdList = await CandidateQuery().Select(x => x.Id).ToListAsync();
        if (request.UserId.HasValue && !candidateIdList.Contains(request.UserId.Value))
            return Result.Invalid(new MessageResponse("Chỉ được ủy quyền cho nhân viên cùng phòng ban với bạn."));

        // Xóa cả ủy quyền do chính mình cấp lẫn ủy quyền đang có của người trong phòng ban: một phòng ban có
        // thể có nhiều Giám Đốc, và mỗi nhân viên chỉ được một dòng ủy quyền (chỉ mục duy nhất trên UserId).
        // Không gộp thế này thì bấm Lưu khi người đó đã được Giám Đốc khác ủy quyền sẽ vỡ chỉ mục duy nhất.
        var existingList = await _db.ApprovalDelegate
            .Where(x => x.GrantedById == _currentUser.UserId || candidateIdList.Contains(x.UserId))
            .ToListAsync();
        _db.ApprovalDelegate.RemoveRange(existingList);

        if (request.UserId.HasValue)
            _db.ApprovalDelegate.Add(new ApprovalDelegate
            {
                UserId = request.UserId.Value,
                GrantedById = _currentUser.UserId
            });

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse(request.UserId.HasValue
            ? "Đã cập nhật người được ủy quyền duyệt."
            : "Đã thu hồi ủy quyền duyệt."));
    }

    /// <summary>Nhân viên đủ điều kiện được ủy quyền: đang hoạt động, cùng phòng ban với Giám Đốc đang đăng nhập
    /// và không phải chính Giám Đốc (tự ủy quyền cho mình thì vô nghĩa).</summary>
    private IQueryable<User> CandidateQuery() => _db.User.AsNoTracking()
        .Where(x => x.IsActive && x.DepartmentId != null && x.DepartmentId == _currentUser.DepartmentId
                 && x.Id != _currentUser.UserId);

    private async Task<bool> IsHrDirectorAsync()
    {
        if (!_currentUser.IsDirector || !_currentUser.DepartmentId.HasValue) return false;
        var hrDepartmentId = (await _db.Department.AsNoTracking().FirstOrDefaultAsync(x => x.IsHrDepartment))?.Id;
        return hrDepartmentId.HasValue && _currentUser.DepartmentId == hrDepartmentId;
    }
}
