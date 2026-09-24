// File: Application/Features/ApprovalWorkflows/ApprovalWorkflowService.cs
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.ApprovalWorkflows;

/// <summary>
/// Các LUỒNG CẤP DUYỆT (chuỗi bước duyệt xếp theo thứ tự) kèm luôn người duyệt đã ghim của từng bước; người
/// soạn nội dung chỉ chọn một luồng có sẵn cho khóa học của mình, không chọn người duyệt.
///
/// Luồng thuộc về PHÒNG BAN, và được dựng ở hai cấp:
///
/// • CẤP PHÒNG BAN (<see cref="GetList"/>, <see cref="Create"/>, <see cref="Update"/>, <see cref="Delete"/>):
///   Trưởng Nhóm, Trưởng phòng, Giám Đốc dựng luồng cho CHÍNH phòng ban của mình — họ mới là người biết
///   trong phòng ai duyệt ai. Phạm vi phòng ban ép theo token của người đăng nhập, không lấy theo
///   ApprovalWorkflowRequest.DepartmentId gửi lên: cả ba vai trò này đều không được dựng luồng cho phòng khác.
///
/// • CẤP CÔNG TY (<see cref="GetCompanyList"/>, <see cref="Clone"/>): Trưởng phòng và Giám Đốc xem được luồng
///   của các phòng ban khác để tham khảo và nhân bản về phòng mình. Chỉ ĐỌC và NHÂN BẢN — sửa luồng của phòng
///   khác vẫn bị chặn như thường.
///
/// • ÁP DỤNG TOÀN CÔNG TY: khi bấm Lưu, người dựng luồng được hỏi có muốn áp dụng luồng cho toàn công ty
///   không. Chọn "Có" thì luồng thành DÙNG CHUNG (DepartmentId = null), mọi phòng ban đều chọn được. Người
///   không phải Admin chỉ gửi được một trong hai: null (toàn công ty) hoặc phòng ban của chính họ — mọi giá
///   trị khác bị ép về phòng ban của họ. Luồng dùng chung do họ tạo thì họ vẫn thấy, sửa, xóa được (xem
///   <see cref="CanManage"/>); luồng dùng chung của người khác thì chỉ đọc và nhân bản.
///
/// Admin đứng trên cả hai cấp: thấy toàn bộ luồng của mọi phòng ban trong một bảng có ô lọc theo phòng ban,
/// và sửa được mọi luồng.
///
/// Quyền đặt trên TỪNG action chứ không ở cấp lớp: nhiều thuộc tính [Authorize] được AND với nhau, nên một
/// [Authorize(Roles = Admin)] ở cấp lớp sẽ vô hiệu hóa phần mở thêm cho các vai trò khác ở từng action —
/// API trả 403 mà nhìn code thì tưởng đã cho phép.
/// </summary>
public class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly IAppDbContext _db;

    /// <summary>Người cấp phòng ban mà tài khoản chưa gắn phòng ban nào thì không có phạm vi nào để dựng
    /// luồng — nói thẳng phải nhờ ai sửa, thay vì để họ lưu xong rồi thấy luồng không hiện ở đâu cả.</summary>
    private const string NoDepartmentMessage =
        "Tài khoản của bạn chưa gắn phòng ban nào nên chưa dựng được luồng cấp duyệt. Hãy báo Admin gán phòng ban cho bạn.";
    private readonly ICurrentUser _currentUser;

    public ApprovalWorkflowService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Chặn ngay đầu action: người KHÔNG phải Admin bắt buộc phải có phòng ban, vì mọi thứ họ đọc và
    /// ghi đều bị rào trong đúng phòng ban đó. Trả null nghĩa là đi tiếp được.</summary>
    private Result? BlockIfNoDepartment() =>
        !_currentUser.IsAdmin && _currentUser.DepartmentId is null ? Result.Invalid(new MessageResponse(NoDepartmentMessage)) : null;

    /// <summary>403 kèm câu tiếng Việt thay cho Forbid() rỗng — người dùng cần biết vì sao nút vừa bấm không
    /// chạy, chứ không phải một hộp lỗi "403" không nói gì.</summary>
    private Result ForbidWith(string message) =>
        Result.Denied(new MessageResponse(message));

    /// <summary>Dựng ApprovalWorkflowDto từ một câu truy vấn — dùng chung cho bảng cấp phòng ban và bảng cấp
    /// công ty, để hai bảng không bao giờ lệch nhau về nội dung một dòng.</summary>
    private IQueryable<ApprovalWorkflowDto> SelectDto(IQueryable<ApprovalWorkflow> query) =>
        query.Select(x => new ApprovalWorkflowDto(
            x.Id, x.Name, x.Description, x.IsActive, x.IsDefault,
            _db.Course.Count(c => c.ApprovalWorkflowId == x.Id),
            x.CreatedById, x.CreatedBy.FullName, x.CreatedAt, x.UpdatedAt,
            x.StepList.OrderBy(s => s.OrderIndex)
                .Select(s => new ApprovalWorkflowStepDto(
                    s.Id, s.OrderIndex, s.Name, s.RoleId, s.Role.Name,
                    s.DepartmentId, s.Department != null ? s.Department.Name : null, s.AllowDelegate,
                    s.ApproverMode.ToString(), s.FixedUserId,
                    // FixedUserId co y khong kem khoa ngoai (xem ApprovalWorkflowStep) nen phai tu tra ten.
                    _db.User.Where(u => u.Id == s.FixedUserId).Select(u => u.FullName).FirstOrDefault()))
                .ToList(),
            x.DepartmentId, x.Department != null ? x.Department.Name : null));

    public async Task<Result> GetList(bool activeOnly = false, int? departmentId = null, bool sharedOnly = false)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;

        var query = _db.ApprovalWorkflow.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);

        if (_currentUser.IsAdmin)
        {
            if (sharedOnly) query = query.Where(x => x.DepartmentId == null);
            else if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        }
        else
        {
            // Ép theo token chứ không theo tham số: bộ lọc trên thanh công cụ là tiện ích của Admin, gửi kèm
            // departmentId của phòng khác không được biến thành một đường vòng để xem luồng phòng đó.
            var myDepartmentId = _currentUser.DepartmentId;
            var myUserId = _currentUser.UserId;
            query = query.Where(x => x.DepartmentId == myDepartmentId
                                     || (x.DepartmentId == null && x.CreatedById == myUserId));
        }

        var list = await SelectDto(query
                .OrderBy(x => x.Department != null ? x.Department.Name : string.Empty)
                .ThenByDescending(x => x.IsDefault).ThenBy(x => x.Name))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetCompanyList(int? departmentId = null, bool sharedOnly = false)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;

        var query = _db.ApprovalWorkflow.AsNoTracking();

        if (!_currentUser.IsAdmin)
        {
            // Loại luồng của phòng mình và luồng dùng chung do chính mình tạo — hai loại đó đã nằm ở bảng
            // cấp phòng ban (GetList), hiện lại ở đây là một luồng xuất hiện hai lần.
            var myDepartmentId = _currentUser.DepartmentId;
            var myUserId = _currentUser.UserId;
            query = query.Where(x => x.DepartmentId != myDepartmentId
                                     && !(x.DepartmentId == null && x.CreatedById == myUserId));
        }

        if (sharedOnly) query = query.Where(x => x.DepartmentId == null);
        else if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);

        var list = await SelectDto(query
                .OrderBy(x => x.Department != null ? x.Department.Name : string.Empty)
                .ThenByDescending(x => x.IsDefault).ThenBy(x => x.Name))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetRoleOptionList()
    {
        var list = await _db.Role.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsSystem).ThenBy(x => x.Id)
            .Select(x => new WorkflowRoleOptionDto(x.Id, x.Name))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetApproverOptionList(int roleId, int? departmentId = null)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;
        if (roleId <= 0) return Result.Success(Array.Empty<WorkflowApproverOptionDto>());

        // Bước giao cho một vai trò CHA phải ra luôn người giữ vai trò con của nó, y như UserService.GetList:
        // chọn "Trưởng phòng" mà danh sách trống vì ai cũng đang là "Trưởng phòng IT" thì không ghim được ai.
        var roleIdList = await _db.Role.AsNoTracking()
            .Where(x => x.Id == roleId || x.ParentRoleId == roleId)
            .Select(x => x.Id)
            .ToListAsync();

        var query = _db.User.AsNoTracking().Where(x => x.IsActive && roleIdList.Contains(x.RoleId));

        // Bước của phòng ban nào thì chỉ người phòng đó; riêng bước trên luồng dùng chung của Admin có thể
        // không gắn phòng nào — khi ấy liệt kê toàn bộ người giữ vai trò, giao diện đã nói rõ lý do.
        var scopeDepartmentId = departmentId ?? (_currentUser.IsAdmin ? null : _currentUser.DepartmentId);
        if (scopeDepartmentId.HasValue) query = query.Where(x => x.DepartmentId == scopeDepartmentId.Value);

        var list = await query
            .OrderBy(x => x.FullName)
            .Take(200)
            .Select(x => new WorkflowApproverOptionDto(x.Id, x.FullName,
                x.Department != null ? x.Department.Name : null,
                x.Position != null ? x.Position.Name : null))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetOptionList()
    {
        var query = _db.ApprovalWorkflow.AsNoTracking().Where(x => x.IsActive && x.StepList.Any());
        if (!_currentUser.IsAdmin)
        {
            var myDepartmentId = _currentUser.DepartmentId;
            query = query.Where(x => x.DepartmentId == null || x.DepartmentId == myDepartmentId);
        }

        var list = await query
            // Luồng ĐÚNG PHÒNG BAN đứng trước luồng dùng chung: người soạn có thể thấy cả hai, và cả hai đều
            // có thể đang mang dấu "mặc định" (mặc định tính theo từng phòng ban — xem ApprovalWorkflow.IsDefault).
            // Giao diện chọn cái ĐẦU TIÊN có IsDefault làm luồng điền sẵn, nên thứ tự này quyết định người
            // soạn được điền sẵn luồng của phòng mình hay một luồng dùng chung chẳng của ai.
            .OrderByDescending(x => x.DepartmentId != null)
            .ThenByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .Select(x => new ApprovalWorkflowOptionDto(
                x.Id, x.Name, x.Description, x.IsDefault,
                x.StepList.OrderBy(s => s.OrderIndex).Select(s => s.Name).ToList(),
                x.Department != null ? x.Department.Name : null))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> Create(ApprovalWorkflowRequest request)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;
        RestrictDepartmentScope(request);

        NormalizeStepModeList(request);
        var error = await ValidateAsync(request, null);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        var workflow = new ApprovalWorkflow
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DepartmentId = request.DepartmentId,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            CreatedById = _currentUser.UserId
        };
        FillStepList(workflow, request, await GetRoleNameMapAsync(request));

        _db.ApprovalWorkflow.Add(workflow);
        await _db.SaveChangesAsync();

        if (workflow.IsDefault) await ClearOtherDefaultAsync(workflow.Id, workflow.DepartmentId);

        return Result.Success(new MessageResponse($"Đã tạo luồng cấp duyệt \"{workflow.Name}\".", workflow.Id));
    }

    public async Task<Result> Update(int id, ApprovalWorkflowRequest request)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;

        var workflow = await _db.ApprovalWorkflow.Include(x => x.StepList).FirstOrDefaultAsync(x => x.Id == id);
        if (workflow is null) return Result.NotFound(new MessageResponse("Không tìm thấy luồng cấp duyệt."));

        if (!CanManage(workflow))
            return ForbidWith("Luồng này không thuộc phòng ban của bạn. Hãy nhân bản nó về phòng ban của bạn rồi sửa bản sao.");
        // Và không được đẩy luồng sang phòng khác qua đường sửa — chỉ phòng mình hoặc toàn công ty.
        RestrictDepartmentScope(request);

        NormalizeStepModeList(request);
        var error = await ValidateAsync(request, id);
        if (error is not null) return Result.Invalid(new MessageResponse(error));

        // Khóa học đang chạy dở trên luồng này trỏ vào bước theo SỐ THỨ TỰ (Course.CurrentStepOrder). Bớt
        // bước đi khiến chúng trỏ vào bước không còn tồn tại — chuỗi duyệt của chúng đứng im không ai duyệt
        // được nữa. Cho sửa tên/nội dung bước thoải mái, chỉ chặn việc rút ngắn luồng.
        var runningStepOrderMax = await _db.Course
            .Where(x => x.ApprovalWorkflowId == id && x.Status == CourseStatus.PendingApproval)
            .Select(x => (int?)x.CurrentStepOrder)
            .MaxAsync() ?? 0;
        if (request.StepList.Count < runningStepOrderMax)
            return Result.Invalid(new MessageResponse(
                $"Có khóa học đang chờ duyệt ở bước {runningStepOrderMax} của luồng này, không thể rút luồng xuống còn {request.StepList.Count} bước."));

        // Ảnh chụp định nghĩa cũ, để so xem bước nào thực sự đổi — lấy TRƯỚC khi dựng lại danh sách bước.
        var oldStepList = workflow.StepList
            .Select(s => new { s.OrderIndex, s.RoleId, s.DepartmentId, s.ApproverMode, s.FixedUserId })
            .ToList();

        workflow.Name = request.Name.Trim();
        workflow.Description = request.Description?.Trim();
        workflow.DepartmentId = request.DepartmentId;
        workflow.IsActive = request.IsActive;
        workflow.IsDefault = request.IsDefault;
        workflow.UpdatedAt = DateTime.Now;

        _db.ApprovalWorkflowStep.RemoveRange(workflow.StepList);
        workflow.StepList.Clear();
        FillStepList(workflow, request, await GetRoleNameMapAsync(request));

        await _db.SaveChangesAsync();

        await ClearOutdatedStepApproverAsync(id, oldStepList
            .Where(oldStep => !workflow.StepList.Any(newStep =>
                newStep.OrderIndex == oldStep.OrderIndex
                && newStep.RoleId == oldStep.RoleId
                && newStep.DepartmentId == oldStep.DepartmentId
                && newStep.ApproverMode == oldStep.ApproverMode
                && newStep.FixedUserId == oldStep.FixedUserId))
            .Select(oldStep => oldStep.OrderIndex)
            .ToList());

        if (workflow.IsDefault) await ClearOtherDefaultAsync(workflow.Id, workflow.DepartmentId);

        return Result.Success(new MessageResponse("Đã cập nhật luồng cấp duyệt."));
    }

    public async Task<Result> Clone(int id, CloneApprovalWorkflowRequest request)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;

        // Trưởng phòng / Giám Đốc chỉ lấy luồng VỀ phòng mình, không rải bản sao sang phòng khác — đích đến
        // lấy từ token, bỏ qua giá trị gửi lên. Chỉ Admin mới chọn được phòng ban đích bất kỳ.
        if (!_currentUser.IsAdmin) request.DepartmentId = _currentUser.DepartmentId;

        var source = await _db.ApprovalWorkflow.AsNoTracking()
            .Include(x => x.StepList)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (source is null) return Result.NotFound(new MessageResponse("Không tìm thấy luồng cấp duyệt."));

        string? targetDepartmentName = null;
        if (request.DepartmentId.HasValue)
        {
            targetDepartmentName = await _db.Department.AsNoTracking()
                .Where(x => x.Id == request.DepartmentId.Value).Select(x => x.Name).FirstOrDefaultAsync();
            if (targetDepartmentName is null)
                return Result.Invalid(new MessageResponse("Phòng ban đích không hợp lệ."));
        }

        if (request.DepartmentId == source.DepartmentId)
            return Result.Invalid(new MessageResponse(_currentUser.IsAdmin
                ? "Luồng này vốn đã thuộc phòng ban bạn chọn. Hãy chọn phòng ban khác để nhân bản sang."
                : "Luồng này vốn đã thuộc phòng ban của bạn, không cần nhân bản. Hãy mở nó ra sửa trực tiếp."));

        var name = await BuildCloneNameAsync(
            request.Name?.Trim(), source.Name, targetDepartmentName, request.DepartmentId);

        var clone = new ApprovalWorkflow
        {
            Name = name,
            Description = source.Description,
            DepartmentId = request.DepartmentId,
            // Bản sao chưa có người duyệt của phòng mình nên chưa cho người soạn chọn phải; và mặc định của
            // phòng đích do người dùng tự đặt sau, nhân bản không được tự giành lấy chỗ đó.
            IsActive = false,
            IsDefault = false,
            CreatedById = _currentUser.UserId
        };

        foreach (var step in source.StepList.OrderBy(x => x.OrderIndex))
            clone.StepList.Add(new ApprovalWorkflowStep
            {
                OrderIndex = step.OrderIndex,
                Name = step.Name,
                RoleId = step.RoleId,
                DepartmentId = step.DepartmentId == source.DepartmentId ? request.DepartmentId : step.DepartmentId,
                AllowDelegate = step.AllowDelegate,
                ApproverMode = StepApproverMode.AnyOfRole,
                FixedUserId = null
            });

        _db.ApprovalWorkflow.Add(clone);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse(
            $"Đã nhân bản thành luồng \"{name}\". Hãy mở ra chọn người duyệt cho từng bước rồi bật Hoạt động.",
            clone.Id));
    }

    /// <summary>Tên cho bản sao: tên người dùng đặt nếu có, không thì "{tên gốc} — {phòng ban đích}". Trùng
    /// tên trong cùng phòng ban thì thêm đuôi (2), (3)... — nhân bản hai lần vào một phòng là chuyện thường,
    /// báo lỗi trùng tên ở đây chỉ tổ bắt người dùng tự nghĩ tên.</summary>
    private async Task<string> BuildCloneNameAsync(string? requestedName, string sourceName,
        string? targetDepartmentName, int? targetDepartmentId)
    {
        var baseName = !string.IsNullOrWhiteSpace(requestedName)
            ? requestedName
            : $"{sourceName} — {targetDepartmentName ?? "Dùng chung"}";
        if (baseName.Length > 150) baseName = baseName[..150];

        var usedNameSet = (await _db.ApprovalWorkflow.AsNoTracking()
            .Where(x => x.DepartmentId == targetDepartmentId)
            .Select(x => x.Name)
            .ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!usedNameSet.Contains(baseName)) return baseName;

        for (var i = 2; ; i++)
        {
            var suffix = $" ({i})";
            var trimmed = baseName.Length + suffix.Length > 150
                ? baseName[..(150 - suffix.Length)]
                : baseName;
            var candidate = trimmed + suffix;
            if (!usedNameSet.Contains(candidate)) return candidate;
        }
    }

    public async Task<Result> Delete(int id)
    {
        if (BlockIfNoDepartment() is { } blocked) return blocked;

        var workflow = await _db.ApprovalWorkflow.Include(x => x.StepList).FirstOrDefaultAsync(x => x.Id == id);
        if (workflow is null) return Result.NotFound(new MessageResponse("Không tìm thấy luồng cấp duyệt."));

        if (!CanManage(workflow))
            return ForbidWith("Luồng này không thuộc phòng ban của bạn nên bạn không xóa được.");

        // Xóa luồng mà khóa học vẫn trỏ vào sẽ làm khóa học đó mất đường duyệt — kể cả khóa đã xuất bản, vì
        // lịch sử duyệt của nó vẫn cần đọc lại được. Ngừng hoạt động là cách gỡ luồng khỏi ô chọn mà không
        // đụng tới dữ liệu cũ.
        var courseCount = await _db.Course.CountAsync(x => x.ApprovalWorkflowId == id);
        if (courseCount > 0)
            return Result.Invalid(new MessageResponse(
                $"Có {courseCount} khóa học đang dùng luồng này, không thể xóa. Hãy chuyển luồng sang trạng thái Ngừng hoạt động."));

        _db.ApprovalWorkflowStep.RemoveRange(workflow.StepList);
        _db.ApprovalWorkflow.Remove(workflow);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đã xóa luồng cấp duyệt."));
    }

    // ---------- Hàm dùng chung ----------

    /// <summary>Người đang đăng nhập có được sửa/xóa luồng này không: Admin thì mọi luồng; người khác thì luồng
    /// của phòng mình, cộng luồng dùng chung toàn công ty do chính họ tạo.</summary>
    private bool CanManage(ApprovalWorkflow workflow)
        => _currentUser.IsAdmin
           || (workflow.DepartmentId is not null && workflow.DepartmentId == _currentUser.DepartmentId)
           || (workflow.DepartmentId is null && workflow.CreatedById == _currentUser.UserId);

    /// <summary>Phạm vi người không phải Admin được chọn: null = áp dụng toàn công ty (họ đã trả lời "Có" ở hộp
    /// thoại khi lưu), còn lại luôn là phòng ban của chính họ — phòng ban gửi lên bị bỏ qua.</summary>
    private void RestrictDepartmentScope(ApprovalWorkflowRequest request)
    {
        if (_currentUser.IsAdmin || request.DepartmentId is null) return;
        request.DepartmentId = _currentUser.DepartmentId;
    }

    /// <summary>Kiểm tra dữ liệu gửi lên. Trả về thông báo lỗi tiếng Việt, hoặc null nếu hợp lệ.</summary>
    private async Task<string?> ValidateAsync(ApprovalWorkflowRequest request, int? excludeId)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Vui lòng nhập tên luồng cấp duyệt.";

        // Trùng tên chỉ xét TRONG CÙNG phòng ban: nhân bản luồng của phòng IT về phòng Hành chính thì hai bản
        // mang cùng một cái tên là chuyện bình thường, chặn toàn hệ thống là chặn luôn việc lấy luồng về dùng.
        if (await _db.ApprovalWorkflow.AnyAsync(x => x.Name == name && x.DepartmentId == request.DepartmentId
                                                  && x.Id != excludeId))
            return request.DepartmentId.HasValue
                ? $"Phòng ban này đã có luồng cấp duyệt tên \"{name}\"."
                : $"Đã có luồng cấp duyệt dùng chung tên \"{name}\".";

        if (request.DepartmentId.HasValue
            && !await _db.Department.AnyAsync(x => x.Id == request.DepartmentId.Value))
            return "Phòng ban của luồng không hợp lệ.";

        if (request.StepList.Count == 0) return "Luồng cấp duyệt phải có ít nhất một bước.";

        var roleIdList = request.StepList.Select(x => x.RoleId).Distinct().ToList();
        var validRoleCount = await _db.Role.CountAsync(x => roleIdList.Contains(x.Id));
        if (validRoleCount != roleIdList.Count) return "Có bước đang chọn vai trò không hợp lệ.";

        var departmentIdList = request.StepList.Where(x => x.DepartmentId.HasValue)
            .Select(x => x.DepartmentId!.Value).Distinct().ToList();
        if (departmentIdList.Count > 0)
        {
            var validDepartmentCount = await _db.Department.CountAsync(x => departmentIdList.Contains(x.Id));
            if (validDepartmentCount != departmentIdList.Count) return "Có bước đang chọn phòng ban không hợp lệ.";
        }

        // Hai bước giống hệt nhau (cùng vai trò, cùng phạm vi phòng ban) nghĩa là cùng một nhóm người phải
        // bấm Duyệt hai lần liên tiếp cho cùng một khóa học — không thêm được cấp kiểm soát nào.
        //
        // Chỉ soi các bước AnyOfRole: bước có chỉ định người duyệt thì hai bước cùng vai trò/phòng ban là hai
        // NGƯỜI khác nhau ("Trưởng phòng A duyệt rồi Trưởng phòng B duyệt"), hoàn toàn hợp lệ.
        var duplicated = request.StepList
            .Where(x => x.ApproverMode == StepApproverMode.AnyOfRole)
            .GroupBy(x => new { x.RoleId, x.DepartmentId })
            .Any(g => g.Count() > 1);
        if (duplicated) return "Hai bước không được trùng cả vai trò lẫn phạm vi phòng ban.";

        return await ValidateFixedUserAsync(request);
    }

    /// <summary>Kiểm những bước ghim sẵn người duyệt (ApproverMode = FixedUser): phải chọn người, người đó
    /// phải còn hoạt động, phải đang giữ đúng vai trò của bước (vai trò kế thừa cũng tính — "Trưởng phòng IT"
    /// ghim được vào bước giao cho "Trưởng phòng") VÀ phải thuộc đúng phòng ban của bước.
    ///
    /// Phòng ban của bước lấy theo bước trước, không có thì lấy của cả luồng; cả hai đều trống thì không có
    /// phòng nào để mà kiểm. Giao diện đã lọc ô chọn theo đúng phạm vi này rồi, nhưng lọc ở giao diện chỉ là
    /// trang trí: ô chọn có thể đã cũ (người vừa chuyển phòng) và một yêu cầu tự tay dựng lên thì gửi Id nào
    /// cũng được.</summary>
    private async Task<string?> ValidateFixedUserAsync(ApprovalWorkflowRequest request)
    {
        var fixedStepList = request.StepList
            .Where(x => x.ApproverMode == StepApproverMode.FixedUser)
            .ToList();
        if (fixedStepList.Count == 0) return null;

        if (fixedStepList.Any(x => !x.FixedUserId.HasValue || x.FixedUserId <= 0))
            return "Bước đang đặt \"Cố định một người duyệt\" nhưng chưa chọn người. Hãy chọn người duyệt hoặc đổi sang chế độ khác.";

        var userIdList = fixedStepList.Select(x => x.FixedUserId!.Value).Distinct().ToList();
        var userList = await _db.User.AsNoTracking()
            .Where(x => userIdList.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.IsActive, x.RoleId, x.Role.ParentRoleId, x.DepartmentId })
            .ToListAsync();

        var departmentNameMap = await GetDepartmentNameMapAsync(request);

        foreach (var step in fixedStepList)
        {
            var user = userList.FirstOrDefault(x => x.Id == step.FixedUserId);
            if (user is null) return "Người duyệt cố định của một bước không còn tồn tại. Hãy chọn lại.";
            if (!user.IsActive) return $"{user.FullName} đã ngừng hoạt động, không đặt làm người duyệt cố định được.";
            if (user.RoleId != step.RoleId && user.ParentRoleId != step.RoleId)
                return $"{user.FullName} không giữ vai trò đã chọn cho bước này. Hãy chọn lại người duyệt hoặc đổi vai trò của bước.";

            var departmentId = step.DepartmentId ?? request.DepartmentId;
            if (departmentId.HasValue && user.DepartmentId != departmentId.Value)
                return $"{user.FullName} không thuộc phòng ban \"{departmentNameMap.GetValueOrDefault(departmentId.Value, "đã chọn")}\" của bước này. Hãy chọn người trong phòng ban đó.";
        }

        return null;
    }

    /// <summary>Tên các phòng ban mà luồng và các bước của nó đang trỏ tới — chỉ để câu thông báo lỗi gọi đúng
    /// tên phòng thay vì bắt người dùng tự đoán id.</summary>
    private async Task<Dictionary<int, string>> GetDepartmentNameMapAsync(ApprovalWorkflowRequest request)
    {
        var idList = request.StepList.Where(x => x.DepartmentId.HasValue).Select(x => x.DepartmentId!.Value)
            .Concat(request.DepartmentId.HasValue ? new[] { request.DepartmentId.Value } : Array.Empty<int>())
            .Distinct()
            .ToList();
        if (idList.Count == 0) return new Dictionary<int, string>();

        return await _db.Department.AsNoTracking()
            .Where(x => idList.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);
    }

    /// <summary>Quy các bước còn ở chế độ PickOne cũ ("người soạn chỉ định người duyệt") về AnyOfRole.
    ///
    /// Người soạn không còn được chọn người duyệt cho bài của mình — việc đó thuộc về người dựng luồng. Chế
    /// độ này đã bỏ khỏi giao diện, nhưng một yêu cầu tự tay dựng lên (hoặc một luồng cũ được mở ra lưu lại)
    /// vẫn có thể mang giá trị đó lên; đổi ngay tại đây để không có bước nào chờ một lựa chọn không còn ai
    /// nhập được nữa.</summary>
    private static void NormalizeStepModeList(ApprovalWorkflowRequest request)
    {
        foreach (var step in request.StepList.Where(x => x.ApproverMode == StepApproverMode.PickOne))
        {
            step.ApproverMode = StepApproverMode.AnyOfRole;
            step.FixedUserId = null;
        }
    }

    /// <summary>Tên vai trò của các bước — dùng làm nhãn mặc định cho bước không đặt tên.</summary>
    private async Task<Dictionary<int, string>> GetRoleNameMapAsync(ApprovalWorkflowRequest request)
    {
        var roleIdList = request.StepList.Select(x => x.RoleId).Distinct().ToList();
        return await _db.Role.AsNoTracking()
            .Where(x => roleIdList.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);
    }

    /// <summary>Dựng lại danh sách bước từ request, đánh số thứ tự theo đúng vị trí trong mảng (1, 2, 3...).</summary>
    private static void FillStepList(ApprovalWorkflow workflow, ApprovalWorkflowRequest request,
        IReadOnlyDictionary<int, string> roleNameMap)
    {
        var order = 0;
        foreach (var step in request.StepList)
        {
            order++;
            var name = step.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = roleNameMap.TryGetValue(step.RoleId, out var roleName) ? roleName : $"Bước {order}";

            workflow.StepList.Add(new ApprovalWorkflowStep
            {
                OrderIndex = order,
                Name = name.Length > 100 ? name[..100] : name,
                RoleId = step.RoleId,
                DepartmentId = step.DepartmentId,
                AllowDelegate = step.AllowDelegate,
                ApproverMode = step.ApproverMode,
                // Chỉ chế độ FixedUser mới giữ người đã ghim. Đổi bước từ FixedUser sang chế độ khác mà vẫn
                // giữ lại Id thì bước đó sẽ lặng lẽ chạy theo người cũ ngay khi ai đó bật FixedUser trở lại.
                FixedUserId = step.ApproverMode == StepApproverMode.FixedUser ? step.FixedUserId : null
            });
        }
    }

    /// <summary>Xóa những người duyệt ĐÃ ĐƯỢC CHỈ ĐỊNH ở các bước vừa bị Admin sửa đổi định nghĩa.
    ///
    /// Chỉ định được chọn theo vai trò + phạm vi phòng ban của bước tại thời điểm gửi duyệt. Admin đổi bước 2
    /// từ "Trưởng phòng" sang "Giám Đốc" mà vẫn giữ lại người đã chỉ định thì bước đó sẽ do một Trưởng phòng
    /// duyệt — đúng người cũ, sai hẳn cấp duyệt Admin vừa đặt. Xóa đi thì khóa học quay về luật vai trò của
    /// bước mới (xem CourseService.CanActOnStep), tức là vẫn duyệt tiếp được chứ không kẹt.</summary>
    private async Task ClearOutdatedStepApproverAsync(int workflowId, IReadOnlyCollection<int> changedOrderList)
    {
        if (changedOrderList.Count == 0) return;

        var outdatedList = await _db.CourseStepApprover
            .Where(x => changedOrderList.Contains(x.StepOrder)
                     && _db.Course.Any(c => c.Id == x.CourseId && c.ApprovalWorkflowId == workflowId))
            .ToListAsync();
        if (outdatedList.Count == 0) return;

        _db.CourseStepApprover.RemoveRange(outdatedList);
        await _db.SaveChangesAsync();
    }

    /// <summary>Mỗi phòng ban chỉ một luồng mặc định — bỏ dấu ở các luồng còn lại CỦA CHÍNH phòng ban đó
    /// (và riêng nhóm luồng dùng chung với nhau). Gỡ dấu trên toàn hệ thống như trước thì đặt mặc định cho
    /// phòng này sẽ xóa mất mặc định của 38 phòng còn lại.</summary>
    private async Task ClearOtherDefaultAsync(int keepId, int? departmentId)
    {
        var otherList = await _db.ApprovalWorkflow
            .Where(x => x.IsDefault && x.Id != keepId && x.DepartmentId == departmentId)
            .ToListAsync();
        if (otherList.Count == 0) return;

        foreach (var item in otherList) item.IsDefault = false;
        await _db.SaveChangesAsync();
    }
}
