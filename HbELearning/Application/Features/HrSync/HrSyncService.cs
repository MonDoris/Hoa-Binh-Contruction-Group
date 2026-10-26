// File: Application/Features/HrSync/HrSyncService.cs
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HbELearning.Application.Features.HrSync;

/// <summary>Giữ kết quả lượt đồng bộ thật gần nhất. Singleton để bấm "Đồng bộ" ở một yêu cầu thì yêu cầu khác (và
/// vòng lặp tự động) đều thấy; mất khi máy chủ khởi động lại — đây chỉ là thông tin tham khảo cho Admin.</summary>
public class HrSyncLastRunStore
{
    public HrSyncResultDto? LastRun { get; set; }
}

/// <summary>Đồng bộ nhân sự một chiều: phần mềm nhân sự → hệ thống học tập.
///
/// Quy tắc (cố ý thận trọng vì đây là dữ liệu tài khoản thật):
///   • Khớp theo MÃ — phòng ban theo Code, vị trí theo Code, nhân viên theo EmployeeCode (dự phòng: email).
///   • Thêm phòng ban / vị trí / nhân viên chưa có; cập nhật tên, phòng ban, vị trí, số điện thoại của người đã có.
///   • Chỉ VÔ HIỆU HÓA người mà phần mềm nhân sự báo đã nghỉ việc. KHÔNG bao giờ khóa vì "không thấy trong danh
///     sách", KHÔNG tự bật lại tài khoản đã tắt, và KHÔNG đụng tới tài khoản Admin.
///   • Không đổi vai trò và mật khẩu của người đã có. Người mới nhận vai trò mặc định (HrSync:DefaultRoleName, mặc
///     định "Học viên") và một mật khẩu ngẫu nhiên không ai biết — họ vào bằng "Quên mật khẩu" hoặc Admin "Reset MK".
///   • Mã nhân viên trùng nhưng KHÁC cả tên lẫn email = nhiều khả năng là hai người khác nhau (vd tài khoản NV001
///     tạo tay trùng mã bên nhân sự) → bỏ qua và báo, không ghi đè.</summary>
public class HrSyncService : IHrSyncService
{
    private const int MaxIssueKept = 200;

    // Một lượt đồng bộ tại một thời điểm: Admin bấm tay trùng lúc vòng lặp tự động chạy sẽ ghi chồng lên nhau.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IAppDbContext _db;
    private readonly IHrSystemClient _client;
    private readonly IPasswordHasher _passwordHasher;
    private readonly HrSyncLastRunStore _store;
    private readonly string _defaultRoleName;
    private readonly ILogger<HrSyncService> _logger;

    public HrSyncService(IAppDbContext db, IHrSystemClient client, IPasswordHasher passwordHasher,
        HrSyncLastRunStore store, IConfiguration config, ILogger<HrSyncService> logger)
    {
        _db = db;
        _client = client;
        _passwordHasher = passwordHasher;
        _store = store;
        _logger = logger;
        _defaultRoleName = config["HrSync:DefaultRoleName"] is { Length: > 0 } name ? name : RoleName.Learner;
    }

    public Result GetStatus()
        => Result.Success(new HrSyncStatusDto(_client.IsConfigured, _client.ProviderName, _defaultRoleName, _store.LastRun));

    public async Task<Result> Run(bool previewOnly, CancellationToken ct = default)
    {
        if (!_client.IsConfigured)
            return Result.ServiceUnavailable(new MessageResponse(
                "Chưa kết nối phần mềm nhân sự: cấu hình HrSystem trong appsettings.json chưa có địa chỉ hoặc API Key."));

        if (!await Gate.WaitAsync(0, ct))
            return Result.Conflict(new MessageResponse("Đang có một lượt đồng bộ khác chạy, vui lòng đợi nó xong."));

        try
        {
            HrSnapshot snapshot;
            try { snapshot = await _client.FetchAsync(ct); }
            catch (HrSystemException ex) { return Result.ExternalServiceFailed(new MessageResponse(ex.Message)); }

            var role = await _db.Role.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive && x.Name == _defaultRoleName, ct);
            if (role is null)
                return Result.Invalid(new MessageResponse(
                    $"Không tìm thấy vai trò mặc định \"{_defaultRoleName}\" cho người mới (cấu hình HrSync:DefaultRoleName)."));

            var result = await Apply(snapshot, role.Id, previewOnly, ct);
            if (!previewOnly)
            {
                _store.LastRun = result;
                _logger.LogInformation("Dong bo nhan su xong: +{Created} nguoi, ~{Updated} nguoi, khoa {Off} nguoi, {Issue} dong loi",
                    result.UserCreated, result.UserUpdated, result.UserDeactivated, result.IssueCount);
            }
            return Result.Success(result);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<HrSyncResultDto> Apply(HrSnapshot snapshot, int roleId, bool previewOnly, CancellationToken ct)
    {
        var issueList = new List<HrSyncIssueDto>();
        var issueCount = 0;
        void Issue(string subject, string message)
        {
            issueCount++;
            if (issueList.Count < MaxIssueKept) issueList.Add(new HrSyncIssueDto(subject, message));
        }

        int deptCreated = 0, deptUpdated = 0, posCreated = 0, posUpdated = 0;
        int userCreated = 0, userUpdated = 0, userDeactivated = 0, userUnchanged = 0;

        // ---------- Phòng ban ----------
        var deptByCode = new Dictionary<string, Department>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in await _db.Department.ToListAsync(ct)) deptByCode.TryAdd(d.Code, d);

        foreach (var rec in snapshot.DepartmentList)
        {
            var code = (rec.Code ?? "").Trim();
            var name = (rec.Name ?? "").Trim();
            if (code.Length is 0 or > 20 || name.Length is 0 or > 150)
            {
                Issue($"Phòng ban {code}", "Mã (tối đa 20 ký tự) hoặc tên (tối đa 150 ký tự) trống / quá dài.");
                continue;
            }

            if (deptByCode.TryGetValue(code, out var dept))
            {
                if (dept.Name != name) { dept.Name = name; deptUpdated++; }
            }
            else
            {
                dept = new Department { Code = code, Name = name, IsActive = true };
                _db.Department.Add(dept);
                deptByCode[code] = dept;
                deptCreated++;
            }
        }

        // ---------- Vị trí công việc ----------
        var posByCode = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await _db.Position.ToListAsync(ct)) posByCode.TryAdd(p.Code, p);

        foreach (var rec in snapshot.PositionList)
        {
            var code = (rec.Code ?? "").Trim();
            var name = (rec.Name ?? "").Trim();
            if (code.Length is 0 or > 20 || name.Length is 0 or > 150)
            {
                Issue($"Vị trí {code}", "Mã (tối đa 20 ký tự) hoặc tên (tối đa 150 ký tự) trống / quá dài.");
                continue;
            }

            Department? dept = null;
            if (!string.IsNullOrWhiteSpace(rec.DepartmentCode) && !deptByCode.TryGetValue(rec.DepartmentCode.Trim(), out dept))
                Issue($"Vị trí {code}", $"Không tìm thấy phòng ban \"{rec.DepartmentCode}\" — vị trí vẫn được tạo nhưng không thuộc phòng nào.");

            if (posByCode.TryGetValue(code, out var pos))
            {
                var changed = false;
                if (pos.Name != name) { pos.Name = name; changed = true; }
                // Chỉ gán phòng ban khi vị trí chưa thuộc phòng nào: đổi phòng của một vị trí đang dùng sẽ kéo theo
                // mọi người giữ vị trí đó, nên để Admin tự quyết trên màn hình Vị trí.
                if (pos.DepartmentId is null && pos.Department is null && dept is not null) { pos.Department = dept; changed = true; }
                if (changed) posUpdated++;
            }
            else
            {
                pos = new Position { Code = code, Name = name, Department = dept, IsActive = true };
                _db.Position.Add(pos);
                posByCode[code] = pos;
                posCreated++;
            }
        }

        // ---------- Nhân viên ----------
        var userList = await _db.User.Include(x => x.Role).ToListAsync(ct);
        var userByCode = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        var userByEmail = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in userList)
        {
            userByCode.TryAdd(u.EmployeeCode, u);
            userByEmail.TryAdd(u.Email, u);
        }

        // Một mật khẩu ngẫu nhiên không ai biết cho cả lượt: băm BCrypt từng người sẽ mất hàng phút với vài nghìn nhân viên.
        string? lockedPasswordHash = null;
        string LockedPasswordHash() => lockedPasswordHash ??= _passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));

        foreach (var rec in snapshot.EmployeeList)
        {
            var code = (rec.EmployeeCode ?? "").Trim();
            var fullName = (rec.FullName ?? "").Trim();
            var email = string.IsNullOrWhiteSpace(rec.Email) ? null : rec.Email.Trim().ToLowerInvariant();
            var phone = string.IsNullOrWhiteSpace(rec.PhoneNumber) ? null : rec.PhoneNumber.Trim();
            var subject = $"Nhân viên {code}";

            if (code.Length is 0 or > 30) { Issue(subject, "Mã nhân viên trống hoặc quá 30 ký tự."); continue; }
            if (fullName.Length is 0 or > 150) { Issue(subject, "Họ tên trống hoặc quá 150 ký tự."); continue; }
            if (email is not null && (email.Length > 150 || !email.Contains('@') || email.Contains(' ')))
            { Issue(subject, $"Email \"{email}\" không hợp lệ."); continue; }
            if (phone is not null && phone.Length > 20) phone = null;

            Department? dept = null;
            Position? pos = null;
            var orgUnknown = false;
            if (!string.IsNullOrWhiteSpace(rec.DepartmentCode) && !deptByCode.TryGetValue(rec.DepartmentCode.Trim(), out dept))
            { Issue(subject, $"Không tìm thấy phòng ban \"{rec.DepartmentCode}\" — giữ nguyên phòng ban hiện tại."); orgUnknown = true; }
            if (!string.IsNullOrWhiteSpace(rec.PositionCode) && !posByCode.TryGetValue(rec.PositionCode.Trim(), out pos))
            { Issue(subject, $"Không tìm thấy vị trí \"{rec.PositionCode}\" — giữ nguyên vị trí hiện tại."); orgUnknown = true; }

            // ----- Tìm người đã có: theo mã, dự phòng theo email -----
            User? user = null;
            if (userByCode.TryGetValue(code, out var byCode))
            {
                var sameName = string.Equals(byCode.FullName, fullName, StringComparison.OrdinalIgnoreCase);
                var sameEmail = email is not null && string.Equals(byCode.Email, email, StringComparison.OrdinalIgnoreCase);
                if (!sameName && !sameEmail && email is not null)
                {
                    Issue(subject, $"Mã này đã thuộc tài khoản \"{byCode.FullName}\" ({byCode.Email}) nhưng tên và email đều khác — bỏ qua, hãy đối chiếu thủ công.");
                    continue;
                }
                user = byCode;
            }
            else if (email is not null && userByEmail.TryGetValue(email, out var byEmail))
            {
                user = byEmail;
            }

            if (user is not null)
            {
                // Tài khoản Admin do hệ thống quản, không để dữ liệu nhân sự sửa hay khóa.
                if (user.Role?.Name == RoleName.Admin) { userUnchanged++; continue; }

                var changed = false;
                if (user.FullName != fullName) { user.FullName = fullName; changed = true; }
                if (phone is not null && user.PhoneNumber != phone) { user.PhoneNumber = phone; changed = true; }
                if (email is not null && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    if (userByEmail.TryGetValue(email, out var holder) && holder != user)
                        Issue(subject, $"Email \"{email}\" đang thuộc tài khoản khác — giữ nguyên email cũ.");
                    else { userByEmail.Remove(user.Email); user.Email = email; userByEmail[email] = user; changed = true; }
                }
                if (!orgUnknown)
                {
                    if (dept is not null && (dept.Id == 0 || user.DepartmentId != dept.Id)) { user.Department = dept; changed = true; }
                    if (pos is not null && (pos.Id == 0 || user.PositionId != pos.Id)) { user.Position = pos; changed = true; }
                }
                if (!rec.IsActive && user.IsActive) { user.IsActive = false; userDeactivated++; changed = true; }

                if (changed) userUpdated++; else userUnchanged++;
                continue;
            }

            // ----- Người mới -----
            if (!rec.IsActive) continue;   // đã nghỉ từ trước khi vào hệ thống: không tạo tài khoản cho người nghỉ việc
            if (email is null) { Issue(subject, "Thiếu email nên không tạo được tài khoản."); continue; }
            if (userByEmail.ContainsKey(email)) { Issue(subject, $"Email \"{email}\" đã thuộc tài khoản khác."); continue; }

            var created = new User
            {
                EmployeeCode = code,
                FullName = fullName,
                Email = email,
                PasswordHash = LockedPasswordHash(),
                PhoneNumber = phone,
                RoleId = roleId,
                Department = dept,
                Position = pos,
                IsActive = true
            };
            _db.User.Add(created);
            userByCode[code] = created;
            userByEmail[email] = created;
            userCreated++;
        }

        if (!previewOnly) await _db.SaveChangesAsync(ct);

        return new HrSyncResultDto(previewOnly, DateTime.Now,
            deptCreated, deptUpdated, posCreated, posUpdated,
            userCreated, userUpdated, userDeactivated, userUnchanged,
            issueCount, issueList);
    }
}
