// File: Application/Features/Users/UserService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Users;

public class UserService : IUserService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Kiểu MIME của .xlsx — trình duyệt dựa vào đây để mở đúng bằng Excel.</summary>
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDataImportService _dataImportService;
    private readonly ICurrentUser _currentUser;

    public UserService(IAppDbContext db, IDataImportService dataImportService, ICurrentUser currentUser, IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
        _db = db;
        _dataImportService = dataImportService;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList(string? keyword, int? departmentId, int? positionId, int? roleId, bool? isActive, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.User.AsNoTracking()
            .Include(x => x.Role)
            .Include(x => x.Department)
            .Include(x => x.Position)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLower();
            query = query.Where(x => x.FullName.ToLower().Contains(k)
                                  || x.Email.ToLower().Contains(k)
                                  || (x.UserName != null && x.UserName.Contains(k))
                                  || x.EmployeeCode.ToLower().Contains(k)
                                  || (x.Department != null && x.Department.Name.ToLower().Contains(k))
                                  || (x.Position != null && x.Position.Name.ToLower().Contains(k)));
        }
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId);
        if (positionId.HasValue) query = query.Where(x => x.PositionId == positionId);
        if (roleId.HasValue)
        {
            // Lọc theo một vai trò CHA thì ra luôn cả các vai trò con của nó: chọn "Trưởng phòng" phải thấy
            // đủ trưởng phòng của mọi ban, chứ không phải một danh sách trống vì ai cũng đang giữ vai trò
            // riêng của ban mình ("Trưởng phòng IT", "Trưởng phòng Nhân sự"...).
            //
            // Chọn thẳng một vai trò con thì chỉ ra đúng người của vai trò đó — muốn xem riêng từng ban vẫn xem được.
            var roleIdList = await _db.Role.AsNoTracking()
                .Where(x => x.Id == roleId || x.ParentRoleId == roleId)
                .Select(x => x.Id)
                .ToListAsync();
            query = query.Where(x => roleIdList.Contains(x.RoleId));
        }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);

        var total = await query.CountAsync();

        var itemList = await query
            .OrderBy(x => x.EmployeeCode)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new UserListDto(x.Id, x.EmployeeCode, x.FullName, x.Email, x.Role.Name,
                x.Department != null ? x.Department.Name : null,
                x.Position != null ? x.Position.Name : null,
                x.IsActive, x.CreatedAt, x.LockedOutAt != null))
            .ToListAsync();

        return Result.Success(new PagedResult<UserListDto>(itemList, total, page, pageSize));
    }

    public async Task<Result> GetById(int id)
    {
        var user = await _db.User.AsNoTracking()
            .Include(x => x.Role).Include(x => x.Department).Include(x => x.Position)
            .FirstOrDefaultAsync(x => x.Id == id);

        return user is null ? Result.NotFound(new MessageResponse("Không tìm thấy người dùng.")) : Result.Success(AuthService.ToProfile(user));
    }

    public async Task<Result> Create(UserCreateRequest request)
    {
        if (await _db.User.AnyAsync(x => x.Email == request.Email))
            return Result.Conflict(new MessageResponse("Email đã tồn tại."));

        var (userName, userNameError) = await CheckUserNameAsync(request.UserName, null);
        if (userNameError is not null) return userNameError;

        if (!await _db.Role.AnyAsync(x => x.Id == request.RoleId && x.IsActive))
            return Result.Invalid(new MessageResponse("Vai trò không hợp lệ."));

        var user = new User
        {            
            EmployeeCode = await GenerateEmployeeCode(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLower(),
            UserName = userName,
            PasswordHash = _passwordHasher.Hash(request.Password),
            RoleId = request.RoleId,
            DepartmentId = request.DepartmentId,
            PositionId = request.PositionId,
            PhoneNumber = request.PhoneNumber,
            AvatarUrl = request.AvatarUrl,
            FunctionKeys = NormalizeFunctionKeys(request.FunctionKeys)
        };

        _db.User.Add(user);
        await _db.SaveChangesAsync();

        await _db.Entry(user).Reference(x => x.Role).LoadAsync();
        await _db.Entry(user).Reference(x => x.Department).LoadAsync();
        await _db.Entry(user).Reference(x => x.Position).LoadAsync();

        return Result.Created(nameof(GetById), new { id = user.Id }, AuthService.ToProfile(user));
    }

    public async Task<Result> Update(int id, UserUpdateRequest request)
    {
        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người dùng."));

        var (userName, userNameError) = await CheckUserNameAsync(request.UserName, id);
        if (userNameError is not null) return userNameError;

        user.FullName = request.FullName.Trim();
        user.UserName = userName;
        user.RoleId = request.RoleId;
        user.DepartmentId = request.DepartmentId;
        user.PositionId = request.PositionId;
        user.PhoneNumber = request.PhoneNumber;
        user.AvatarUrl = request.AvatarUrl;
        user.IsActive = request.IsActive;
        user.FunctionKeys = NormalizeFunctionKeys(request.FunctionKeys);

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    public async Task<Result> Deactivate(int id)
    {
        if (id == _currentUser.UserId)
            return Result.Invalid(new MessageResponse("Không thể vô hiệu hóa chính tài khoản đang đăng nhập."));

        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người dùng."));

        user.IsActive = false;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã vô hiệu hóa tài khoản."));
    }

    public async Task<Result> Delete(int id)
    {
        if (id == _currentUser.UserId)
            return Result.Invalid(new MessageResponse("Không thể xóa chính tài khoản đang đăng nhập."));

        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người dùng."));

        var hasActivity = await _db.Enrollment.AnyAsync(x => x.UserId == id || x.AssignedById == id)
            || await _db.Course.AnyAsync(x => x.CreatedById == id)
            || await _db.CourseApprovalHistory.AnyAsync(x => x.ActorUserId == id)
            || await _db.OnlineClassRoom.AnyAsync(x => x.HostUserId == id)
            // Lượt đổi quà ở cửa hàng là sổ chi tiêu điểm, và khóa ngoại của nó để Restrict (xem
            // AppDbContext) — không chặn ở đây thì cú xóa chết ở tầng CSDL với một thông báo tiếng Anh khó hiểu.
            || await _db.ShopRedemption.AnyAsync(x => x.UserId == id);

        if (hasActivity)
            return Result.Invalid(new MessageResponse(
                "Tài khoản đã có dữ liệu hoạt động (khóa học, phân công, duyệt nội dung...) nên không thể xóa vĩnh viễn. Hãy dùng \"Khóa\" để vô hiệu hóa thay vì xóa."));

        _db.User.Remove(user);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa tài khoản."));
    }

    public async Task<Result> ResetPassword(int id, AdminResetPasswordRequest? request)
    {
        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người dùng."));

        var newPassword = string.IsNullOrWhiteSpace(request?.NewPassword) ? "Hbe@12345" : request.NewPassword;
        user.PasswordHash = _passwordHasher.Hash(newPassword);

        // Đặt lại mật khẩu cũng là mở khóa: gần như mọi tài khoản bị khóa đều vì chủ nhân quên mật khẩu, mà
        // giao cho họ mật khẩu mới rồi vẫn để cửa đóng thì họ gọi lại phòng IT ngay lần đăng nhập kế tiếp.
        user.FailedLoginCount = 0;
        user.LockedOutAt = null;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã đặt lại mật khẩu thành: {newPassword}"));
    }

    public async Task<Result> Unlock(int id)
    {
        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return Result.NotFound(new MessageResponse("Không tìm thấy người dùng."));

        if (user.LockedOutAt is null)
            return Result.Invalid(new MessageResponse("Tài khoản này không bị khóa đăng nhập."));

        user.FailedLoginCount = 0;
        user.LockedOutAt = null;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse($"Đã mở khóa đăng nhập cho {user.FullName}."));
    }

    /// <summary>Chuẩn hóa và kiểm tra tên tài khoản gửi lên: đúng dạng thức và chưa ai dùng.
    ///
    /// Tính duy nhất được kiểm ở đây chứ không phó mặc cho chỉ mục duy nhất dưới database: một database đã
    /// chạy từ trước chỉ được vá THÊM CỘT lúc khởi động, nên có thể chưa có chỉ mục đó — mà hai người trùng
    /// tên tài khoản thì lúc đăng nhập hệ thống sẽ chọn đại một người.</summary>
    /// <param name="currentUserId">Id người đang được sửa, để không tự báo trùng với chính mình; null khi tạo mới.</param>
    /// <returns>Tên đã chuẩn hóa (null = bỏ trống) kèm phản hồi lỗi để controller trả thẳng cho client.</returns>
    private async Task<(string? Value, Result? Error)> CheckUserNameAsync(string? raw, int? currentUserId)
    {
        var userName = UserNameHelper.Normalize(raw);
        if (userName is null) return (null, null);

        if (!UserNameHelper.IsValid(userName))
            return (null, Result.Invalid(new MessageResponse(UserNameHelper.RuleMessage + ".")));

        var query = _db.User.Where(x => x.UserName == userName);
        if (currentUserId.HasValue) query = query.Where(x => x.Id != currentUserId.Value);
        if (await query.AnyAsync())
            return (null, Result.Conflict(new MessageResponse("Tên tài khoản đã tồn tại.")));

        return (userName, null);
    }

    /// <summary>Sinh mã nhân viên dạng NV + số thứ tự 3 chữ số, ví dụ NV001 (khớp định dạng dữ liệu mẫu).</summary>
    private async Task<string> GenerateEmployeeCode()
    {
        const string prefix = "NV";

        var numberList = await _db.User
            .Where(x => x.EmployeeCode.StartsWith(prefix))
            .Select(x => x.EmployeeCode.Substring(prefix.Length))
            .ToListAsync();

        var maxNumber = numberList
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        var code = $"{prefix}{(maxNumber + 1):D3}";
        while (await _db.User.AnyAsync(x => x.EmployeeCode == code))
        {
            maxNumber++;
            code = $"{prefix}{(maxNumber + 1):D3}";
        }
        return code;
    }

    /// <summary>Quyền cấp riêng cho một người — chuẩn hóa theo đúng danh mục quyền chung (Helpers/FunctionKey.cs).</summary>
    private static string? NormalizeFunctionKeys(string? raw) => FunctionKey.Normalize(raw);

    public Result DownloadImportTemplate()
        => Result.FileDownload(_dataImportService.BuildUserTemplate(), ExcelMime, "Mau_Import_NguoiDung.xlsx");

    public async Task<Result> Import(FileUpload file)
    {
        if (file is null || file.Length == 0)
            return Result.Invalid(new MessageResponse("Chưa chọn file."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xlsm"))
            return Result.Invalid(new MessageResponse("Chỉ nhận file Excel (.xlsx hoặc .xlsm)."));

        try
        {
            await using var stream = file.OpenReadStream();
            return Result.Success(await _dataImportService.ImportUserAsync(stream));
        }
        catch (Exception ex)
        {
            return Result.Invalid(new MessageResponse($"Không đọc được file Excel: {ex.Message}"));
        }
    }
}
