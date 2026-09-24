// File: Application/Features/Certificates/CertificateService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Certificates;

public class CertificateService : ICertificateService
{
    /// <summary>Trần số nhân viên trả về một lượt. Admin nhìn toàn công ty nên không chặn thì một công ty
    /// vài nghìn người đổ hết xuống trình duyệt trong một request — ô tìm kiếm ở khối đó là cách lọc, không
    /// phải cuộn.</summary>
    private const int MaxSubordinate = 100;

    /// <summary>Giá trị thay cho "chưa gắn phòng ban" trong tham số lọc departmentId. Id phòng ban thật luôn
    /// từ 1 trở lên nên số 0 không bao giờ đụng ai. Cần một giá trị như vậy vì bỏ qua nhóm không phòng ban
    /// thì những tài khoản đó biến mất lặng lẽ khỏi màn hình lọc, mà Admin lại thường là một trong số đó.</summary>
    private const int UnassignedDepartmentId = 0;

    private readonly IAppDbContext _db;
    private readonly ICertificateIssuer _certificateService;
    private readonly ICurrentUser _currentUser;

    public CertificateService(IAppDbContext db, ICertificateIssuer certificateService, ICurrentUser currentUser)
    {
        _db = db;
        _certificateService = certificateService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetMy()
    {
        var userId = _currentUser.UserId;
        return Result.Success(await QueryDto(x => x.Enrollment.UserId == userId));
    }

    public async Task<Result> GetList(int? courseId, int? departmentId)
    {
        var list = await QueryDto(x =>
            (!courseId.HasValue || x.Enrollment.CourseId == courseId) &&
            (!departmentId.HasValue || x.Enrollment.User.DepartmentId == departmentId));

        return Result.Success(list);
    }

    public async Task<Result> GetById(int id)
    {
        var list = await QueryDto(x => x.Id == id);
        var item = list.FirstOrDefault();

        if (item is null) return Result.NotFound(new MessageResponse("Không tìm thấy chứng nhận."));
        if (!_currentUser.IsManager && item.UserId != _currentUser.UserId) return Result.Forbidden();

        return Result.Success(item);
    }

    public async Task<Result> GetSubordinateList(string? keyword, int? departmentId)
    {
        var query = await SubordinateQueryAsync();
        if (query is null) return Result.Success(Array.Empty<CertificateSubordinateDto>());

        if (departmentId.HasValue)
            query = departmentId.Value == UnassignedDepartmentId
                ? query.Where(x => x.DepartmentId == null)
                : query.Where(x => x.DepartmentId == departmentId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(x => x.FullName.Contains(k) || x.EmployeeCode.Contains(k));
        }

        var userList = await query
            .OrderBy(x => x.FullName)
            .Take(MaxSubordinate)
            .Select(x => new
            {
                x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl,
                DepartmentName = x.Department != null ? x.Department.Name : null,
                PositionName = x.Position != null ? x.Position.Name : null,
                RoleName = x.Role.Name
            })
            .ToListAsync();

        // Đếm chứng nhận của CẢ danh sách trong một câu truy vấn. Gọi riêng cho từng người thì một phòng
        // ban 40 nhân viên là 40 lượt đi vòng xuống database chỉ để lấy 40 con số.
        var idList = userList.Select(x => x.Id).ToList();
        var countMap = await _db.Certificate.AsNoTracking()
            .Where(x => idList.Contains(x.Enrollment.UserId))
            .GroupBy(x => x.Enrollment.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return Result.Success(userList.Select(x => new CertificateSubordinateDto(
            x.Id, x.FullName, x.EmployeeCode, x.AvatarUrl,
            x.DepartmentName, x.PositionName, x.RoleName,
            countMap.GetValueOrDefault(x.Id))).ToList());
    }

    public async Task<Result> GetSubordinateDepartmentList()
    {
        var query = await SubordinateQueryAsync();
        if (query is null) return Result.Success(Array.Empty<CertificateDepartmentDto>());

        var groupList = await query
            .GroupBy(x => x.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Lấy TÊN bằng một câu riêng thay vì gộp vào phép gom nhóm: gom nhóm theo một khóa có chứa thuộc
        // tính của bảng liên kết là chỗ EF hay dịch ra câu SQL vòng vo, mà ở đây chỉ cần tra vài cái tên.
        var idList = groupList.Where(x => x.DepartmentId.HasValue)
            .Select(x => x.DepartmentId!.Value).ToList();
        var nameMap = await _db.Department.AsNoTracking()
            .Where(x => idList.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var list = groupList
            .Select(x => new CertificateDepartmentDto(
                x.DepartmentId ?? UnassignedDepartmentId,
                x.DepartmentId is null
                    ? "Chưa gắn phòng ban"
                    : nameMap.GetValueOrDefault(x.DepartmentId.Value, "Phòng ban đã xóa"),
                x.Count))
            .OrderBy(x => x.Name)
            .ToList();

        return Result.Success(list);
    }

    public async Task<Result> GetByUser(int userId)
    {
        if (userId != _currentUser.UserId && !await IsSubordinateAsync(userId))
            return Result.Forbidden();

        return Result.Success(await QueryDto(x => x.Enrollment.UserId == userId));
    }

    public async Task<Result> Verify(string code)
    {
        // Nhận CẢ số hiệu in trên chứng nhận (HBE-2026-000001 — dễ nhớ, người ta gõ tay) LẪN mã ngẫu nhiên
        // trong QR (liên kết cũ đã in/gửi đi vẫn phải chạy). Số hiệu chỉ gồm chữ HOA nên đưa về chữ hoa
        // trước khi so, gõ "hbe-2026-000001" vẫn ra.
        code = (code ?? string.Empty).Trim();
        var certificateNo = code.ToUpperInvariant();

        var certificate = await _db.Certificate.AsNoTracking()
            .Include(x => x.Enrollment).ThenInclude(e => e.User)
            .Include(x => x.Enrollment).ThenInclude(e => e.Course)
            .FirstOrDefaultAsync(x => x.CertificateNo == certificateNo || x.VerificationCode == code);

        if (certificate is null)
            return Result.Success(new CertificateVerifyDto(false, "Không tìm thấy chứng nhận với mã này.",
                null, null, null, null, null, null, null));

        if (certificate.IsRevoked)
            return Result.Success(new CertificateVerifyDto(false, "Chứng nhận đã bị thu hồi.",
                certificate.CertificateNo, null, null, null, null, null, null));

        if (certificate.ExpiredAt.HasValue && certificate.ExpiredAt < DateTime.Now)
            return Result.Success(new CertificateVerifyDto(false, "Chứng nhận đã hết hiệu lực.",
                certificate.CertificateNo, certificate.Enrollment.User.FullName,
                certificate.Enrollment.User.EmployeeCode, certificate.Enrollment.Course.Title,
                certificate.IssuedAt, certificate.ExpiredAt, certificate.FinalScore));

        return Result.Success(new CertificateVerifyDto(true, "Chứng nhận hợp lệ.",
            certificate.CertificateNo,
            certificate.Enrollment.User.FullName,
            certificate.Enrollment.User.EmployeeCode,
            certificate.Enrollment.Course.Title,
            certificate.IssuedAt, certificate.ExpiredAt, certificate.FinalScore));
    }

    public async Task<Result> Issue(int enrollmentId)
    {
        var enrollment = await _db.Enrollment.FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi phân công."));

        if (enrollment.Status != EnrollmentStatus.Completed)
            return Result.Invalid(new MessageResponse("Người học chưa hoàn thành khóa học."));

        var certificate = await _certificateService.IssueAsync(enrollmentId, enrollment.FinalScore ?? 100m);
        return Result.Success(new MessageResponse($"Đã cấp chứng nhận số {certificate.CertificateNo}."));
    }

    public async Task<Result> Revoke(int id)
    {
        var certificate = await _db.Certificate.FirstOrDefaultAsync(x => x.Id == id);
        if (certificate is null) return Result.NotFound(new MessageResponse("Không tìm thấy chứng nhận."));

        certificate.IsRevoked = true;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã thu hồi chứng nhận."));
    }

    /// <summary>Truy vấn những người bậc THẤP HƠN người đang đăng nhập, trong phạm vi họ quản. Trả về null
    /// nếu vai trò này không quản ai (Học viên, hoặc một vai trò tự tạo chưa gán vai trò cha).</summary>
    private async Task<IQueryable<User>?> SubordinateQueryAsync()
    {
        var me = await _db.User.AsNoTracking()
            .Where(x => x.Id == _currentUser.UserId)
            .Select(x => new
            {
                x.DepartmentId,
                // Vai trò tự tạo ("Trưởng phòng IT") không có tên trong thang bậc — quy về vai trò CHA
                // trước khi hỏi bậc, đúng cách TokenService cấp thêm claim vai trò cho nó.
                EffectiveRoleName = x.Role.ParentRole != null ? x.Role.ParentRole.Name : x.Role.Name
            })
            .FirstOrDefaultAsync();

        if (me is null) return null;

        var myRank = RoleName.RankOf(me.EffectiveRoleName);
        if (myRank <= RoleName.RankOf(RoleName.Learner)) return null;   // Học viên không quản ai

        var companyWide = RoleName.IsCompanyWide(me.EffectiveRoleName);

        // Không nhìn xuyên phòng ban mà bản thân chưa được gắn phòng ban nào thì phạm vi quản lý là rỗng.
        // Bỏ qua nhánh này là Trưởng phòng "lơ lửng" nhìn thấy sạch nhân viên toàn công ty.
        if (!companyWide && me.DepartmentId is null) return null;

        // Bảng vai trò chỉ vài chục dòng nên kéo hết về rồi lọc bằng C#: bậc là một phép so tên, không có
        // cách nào dịch nó thành SQL mà không dựng thêm một cột xếp hạng trong database.
        var roleList = await _db.Role.AsNoTracking()
            .Select(x => new { x.Id, EffectiveName = x.ParentRole != null ? x.ParentRole.Name : x.Name })
            .ToListAsync();

        var lowerRoleIdList = roleList
            .Where(x => RoleName.RankOf(x.EffectiveName) < myRank)
            .Select(x => x.Id)
            .ToList();

        if (lowerRoleIdList.Count == 0) return null;

        var userId = _currentUser.UserId;
        var query = _db.User.AsNoTracking()
            .Where(x => x.IsActive && x.Id != userId && lowerRoleIdList.Contains(x.RoleId));

        if (!companyWide)
        {
            var departmentId = me.DepartmentId;
            query = query.Where(x => x.DepartmentId == departmentId);
        }

        return query;
    }

    private async Task<bool> IsSubordinateAsync(int userId)
    {
        var query = await SubordinateQueryAsync();
        return query is not null && await query.AnyAsync(x => x.Id == userId);
    }

    private async Task<List<CertificateDto>> QueryDto(
        System.Linq.Expressions.Expression<Func<Certificate, bool>> predicate)
    {
        return await _db.Certificate.AsNoTracking()
            .Where(predicate)
            .OrderByDescending(x => x.IssuedAt)
            .Select(x => new CertificateDto(
                x.Id, x.EnrollmentId, x.CertificateNo, x.VerificationCode,
                x.IssuedAt, x.ExpiredAt, x.FinalScore, x.QrCodeUrl, x.IsRevoked,
                x.Enrollment.CourseId, x.Enrollment.Course.Code, x.Enrollment.Course.Title,
                x.Enrollment.UserId, x.Enrollment.User.FullName, x.Enrollment.User.EmployeeCode,
                x.Enrollment.User.Department != null ? x.Enrollment.User.Department.Name : null,
                x.Enrollment.User.Position != null ? x.Enrollment.User.Position.Name : null))
            .ToListAsync();
    }
}
