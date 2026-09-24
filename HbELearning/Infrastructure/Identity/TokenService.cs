// File: Infrastructure/Identity/TokenService.cs
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HbELearning.Infrastructure.Identity;

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config) => _config = config;

    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var jwt = _config.GetSection("Jwt");
        var key = jwt["Key"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key");
        var expireMinutes = int.TryParse(jwt["ExpireMinutes"], out var m) ? m : 480;
        var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);

        var claimList = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.Name),
            new("employeeCode", user.EmployeeCode),

            // Vai trò cài sẵn giữ nguyên cách chặn theo tên vai trò; vai trò do Admin tự tạo mới dùng tới
            // danh sách quyền bên dưới (xem PermissionAuthorizationHandler).
            new(AppClaimType.SystemRoleClaimType, user.Role.IsSystem ? "true" : "false")
        };

        // ---------- Kế thừa danh tính từ vai trò cha ----------
        // Vai trò con ("Trưởng phòng IT") được cấp THÊM một claim vai trò mang tên cha ("Trưởng phòng"), nhờ
        // vậy [Authorize(Roles = "Trưởng phòng")] và User.IsInRole đều nhận mà không phải sửa lại từng
        // attribute trên hơn 20 Controller.
        //
        // Claim tên thật của chính vai trò đó nằm TRƯỚC (ở trên) là có chủ đích: FindFirstValue(Role) lấy
        // claim đầu tiên, và đó là thứ được ghi vào lịch sử duyệt ("ai đã duyệt") — nhật ký phải ghi đúng
        // "Trưởng phòng IT" chứ không phải "Trưởng phòng".
        //
        // Việc này KHÔNG nới phạm vi duyệt nội dung: chuỗi duyệt vốn đã bắt buộc cùng phòng ban với người
        // tạo khóa học (xem CourseService.Approve), nên Trưởng phòng IT vẫn chỉ duyệt được bài phòng IT.
        var parentRoleName = user.Role.ParentRole?.Name;
        if (!string.IsNullOrWhiteSpace(parentRoleName) && parentRoleName != user.Role.Name)
            claimList.Add(new Claim(ClaimTypes.Role, parentRoleName));

        // Quyền thực tế = (quyền vai trò + quyền vai trò cha) GIAO quyền cấp riêng cho người này, gói trong
        // 1 claim CSV.
        var roleKeys = FunctionKey.Inherit(user.Role.FunctionKeys, user.Role.ParentRole?.FunctionKeys);
        var permissionList = FunctionKey.Effective(roleKeys, user.FunctionKeys);
        if (permissionList.Count > 0)
            claimList.Add(new Claim(AppClaimType.PermissionClaimType, string.Join(',', permissionList)));

        if (user.DepartmentId.HasValue)
            claimList.Add(new Claim("departmentId", user.DepartmentId.Value.ToString()));
        if (user.PositionId.HasValue)
            claimList.Add(new Claim("positionId", user.PositionId.Value.ToString()));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claimList),
            Expires = expiresAt,
            Issuer = jwt["Issuer"],
            Audience = jwt["Audience"],
            SigningCredentials = credentials
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expiresAt);
    }
}
