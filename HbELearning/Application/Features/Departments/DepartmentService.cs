// File: Application/Features/Departments/DepartmentService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Departments;

public class DepartmentService : IDepartmentService
{
    private readonly IAppDbContext _db;

    /// <summary>Kiểu MIME của .xlsx — trình duyệt dựa vào đây để mở đúng bằng Excel.</summary>
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDataImportService _dataImportService;

    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(IAppDbContext db, IDataImportService dataImportService,
        ILogger<DepartmentService> logger)
    {
        _logger = logger;
        _db = db;
        _dataImportService = dataImportService;
    }

    public async Task<Result> GetList(bool? isActive)
    {
        var query = _db.Department.AsNoTracking().AsQueryable();
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);

        var list = await query.OrderBy(x => x.Code)
            .Select(x => new DepartmentDto(x.Id, x.Code, x.Name, x.Description, x.IsActive, x.IsHrDepartment, x.UserList.Count))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetById(int id)
    {
        var item = await _db.Department.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new DepartmentDto(x.Id, x.Code, x.Name, x.Description, x.IsActive, x.IsHrDepartment, x.UserList.Count))
            .FirstOrDefaultAsync();

        return item is null ? Result.NotFound(new MessageResponse("Không tìm thấy phòng ban.")) : Result.Success(item);
    }

    public async Task<Result> Create(DepartmentRequest request)
    {
        var entity = new Department
        {
            Code = await GenerateDepartmentCodeAsync(request.Name),
            Name = request.Name.Trim(),
            Description = request.Description,
            IsActive = request.IsActive,
            IsHrDepartment = request.IsHrDepartment
        };

        if (request.IsHrDepartment) await ClearOtherHrDepartmentFlagAsync(null);

        _db.Department.Add(entity);
        await _db.SaveChangesAsync();

        return Result.Created(nameof(GetById), new { id = entity.Id },
            new DepartmentDto(entity.Id, entity.Code, entity.Name, entity.Description, entity.IsActive, entity.IsHrDepartment, 0));
    }

    public async Task<Result> Update(int id, DepartmentRequest request)
    {
        var entity = await _db.Department.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng ban."));

        // Mã phòng ban do hệ thống tự sinh khi tạo, không cho sửa lại sau đó.
        entity.Name = request.Name.Trim();
        entity.Description = request.Description;
        entity.IsActive = request.IsActive;

        if (request.IsHrDepartment && !entity.IsHrDepartment) await ClearOtherHrDepartmentFlagAsync(id);
        entity.IsHrDepartment = request.IsHrDepartment;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    /// <summary>Đảm bảo chỉ đúng 1 phòng ban được đánh dấu là phòng Nhân sự tại một thời điểm.</summary>
    private async Task ClearOtherHrDepartmentFlagAsync(int? exceptId)
    {
        var otherList = await _db.Department
            .Where(x => x.IsHrDepartment && x.Id != exceptId)
            .ToListAsync();
        foreach (var other in otherList) other.IsHrDepartment = false;
    }

    /// <summary>Sinh mã phòng ban từ chữ cái đầu các từ trong tên (bỏ các từ đệm như "Phòng", "Ban"),
    /// VD "Phòng Công nghệ thông tin" -> "CNTT". Thêm số thứ tự vào cuối nếu mã đã tồn tại.</summary>
    private async Task<string> GenerateDepartmentCodeAsync(string name)
    {
        var baseCode = InitialsFromName(name);
        if (string.IsNullOrEmpty(baseCode)) baseCode = "PB";

        var code = baseCode;
        var suffix = 1;
        while (await _db.Department.AnyAsync(x => x.Code == code))
        {
            suffix++;
            code = $"{baseCode}{suffix}";
        }
        return code;
    }

    /// <summary>Quy tắc ghép mã nằm ở Helpers/OrgCodeHelper.cs để dùng chung với phần nhập liệu từ Excel.</summary>
    private static string InitialsFromName(string name) => OrgCodeHelper.InitialsFromName(name);

    public async Task<Result> Delete(int id)
    {
        var entity = await _db.Department.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng ban."));

        if (await _db.User.AnyAsync(x => x.DepartmentId == id))
            return Result.Invalid(new MessageResponse("Phòng ban đang có nhân viên, không thể xóa. Hãy chuyển sang trạng thái ngừng hoạt động."));

        // Cùng lý do với vai trò ở RoleService.Delete: bước duyệt gắn cứng phòng ban này sẽ mất người duyệt.
        var workflowStepCount = await _db.ApprovalWorkflowStep.CountAsync(x => x.DepartmentId == id);
        if (workflowStepCount > 0)
            return Result.Invalid(new MessageResponse(
                $"Phòng ban này đang được dùng ở {workflowStepCount} bước trong các luồng cấp duyệt, không thể xóa. Hãy gỡ khỏi các luồng đó trước."));

        _db.Department.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa phòng ban."));
    }

    public Result DownloadImportTemplate()
        => Result.FileDownload(_dataImportService.BuildDepartmentTemplate(), ExcelMime, "Mau_Import_PhongBan.xlsx");

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
            return Result.Success(await _dataImportService.ImportDepartmentAsync(stream));
        }
        catch (Exception ex)
        {
            // Không ghép lời của ngoại lệ vào câu trả về: ClosedXML báo lỗi kèm đường dẫn tuyệt đối trên
            // máy chủ. Chi tiết vào nhật ký, người dùng nhận câu hướng dẫn xử lý.
            _logger.LogError(ex, "Khong doc duoc file Excel khi nhap du lieu");
            return Result.Invalid(new MessageResponse(
                "Không đọc được file Excel. Hãy kiểm tra file có đúng bộ cột như file mẫu không, "
                + "hoặc mở bằng Excel rồi lưu lại dạng .xlsx trước khi tải lên."));
        }
    }
}
