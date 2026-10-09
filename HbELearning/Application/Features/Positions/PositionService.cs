// File: Application/Features/Positions/PositionService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Positions;

public class PositionService : IPositionService
{
    private readonly IAppDbContext _db;

    /// <summary>Kiểu MIME của .xlsx — trình duyệt dựa vào đây để mở đúng bằng Excel.</summary>
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDataImportService _dataImportService;

    private readonly ILogger<PositionService> _logger;

    public PositionService(IAppDbContext db, IDataImportService dataImportService,
        ILogger<PositionService> logger)
    {
        _logger = logger;
        _db = db;
        _dataImportService = dataImportService;
    }

    public async Task<Result> GetList(int? departmentId, bool? isActive)
    {
        var query = _db.Position.AsNoTracking().AsQueryable();
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);

        var list = await query.OrderBy(x => x.Code)
            .Select(x => new PositionDto(x.Id, x.Code, x.Name, x.Description,
                x.DepartmentId, x.Department != null ? x.Department.Name : null,
                x.IsActive, x.UserList.Count))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetById(int id)
    {
        var item = await _db.Position.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new PositionDto(x.Id, x.Code, x.Name, x.Description,
                x.DepartmentId, x.Department != null ? x.Department.Name : null,
                x.IsActive, x.UserList.Count))
            .FirstOrDefaultAsync();

        return item is null ? Result.NotFound(new MessageResponse("Không tìm thấy vị trí công việc.")) : Result.Success(item);
    }

    public async Task<Result> Create(PositionRequest request)
    {
        var entity = new Position
        {
            Code = await GeneratePositionCodeAsync(request.DepartmentId),
            Name = request.Name.Trim(),
            Description = request.Description,
            DepartmentId = request.DepartmentId,
            IsActive = request.IsActive
        };

        _db.Position.Add(entity);
        await _db.SaveChangesAsync();

        return Result.Created(nameof(GetById), new { id = entity.Id },
            new PositionDto(entity.Id, entity.Code, entity.Name, entity.Description,
                entity.DepartmentId, null, entity.IsActive, 0));
    }

    public async Task<Result> Update(int id, PositionRequest request)
    {
        var entity = await _db.Position.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy vị trí công việc."));

        // Mã vị trí do hệ thống tự sinh khi tạo, không cho sửa lại sau đó.
        entity.Name = request.Name.Trim();
        entity.Description = request.Description;
        entity.DepartmentId = request.DepartmentId;
        entity.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    /// <summary>Sinh mã vị trí dạng {MãPhòngBan}-{STT 2 chữ số}, VD "IT-01". Không chọn phòng ban thì dùng tiền tố "VT".</summary>
    private async Task<string> GeneratePositionCodeAsync(int? departmentId)
    {
        var prefix = "VT";
        if (departmentId.HasValue)
        {
            var department = await _db.Department.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId);
            if (department is not null) prefix = department.Code;
        }

        var seq = await _db.Position.CountAsync(x => x.Code.StartsWith(prefix + "-")) + 1;
        var code = $"{prefix}-{seq:D2}";
        while (await _db.Position.AnyAsync(x => x.Code == code))
        {
            seq++;
            code = $"{prefix}-{seq:D2}";
        }
        return code;
    }

    public async Task<Result> Delete(int id)
    {
        var entity = await _db.Position.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return Result.NotFound(new MessageResponse("Không tìm thấy vị trí công việc."));

        if (await _db.User.AnyAsync(x => x.PositionId == id))
            return Result.Invalid(new MessageResponse("Vị trí đang có nhân viên, không thể xóa."));

        // Xóa các dòng "đối tượng đào tạo" trỏ tới vị trí này. Trước đây CSDL tự xóa theo, nhưng khóa ngoại
        // đó đã phải chuyển sang NoAction để SQL Server chấp nhận schema (xem AppDbContext).
        var targetList = await _db.CourseTarget.Where(x => x.PositionId == id).ToListAsync();
        _db.CourseTarget.RemoveRange(targetList);

        _db.Position.Remove(entity);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa vị trí công việc."));
    }

    public Result DownloadImportTemplate()
        => Result.FileDownload(_dataImportService.BuildPositionTemplate(), ExcelMime, "Mau_Import_ViTri.xlsx");

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
            return Result.Success(await _dataImportService.ImportPositionAsync(stream));
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
