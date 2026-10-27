// File: Application/Features/Auth/AuthService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.Auth;

public class AuthService : IAuthService
{
    private const int OtpExpireMinutes = 5;

    /// <summary>Số lần được gõ sai mật khẩu trước khi tài khoản bị khóa. Lần sai thứ ba là lần cuối cùng.</summary>
    private const int MaxLoginAttempt = 3;

    /// <summary>Câu báo cho tài khoản đã bị khóa. Nói thẳng phải gặp ai, vì hệ thống cố ý không tự mở khóa
    /// lại sau một khoảng thời gian — người dùng có ngồi đợi cũng không vào được.</summary>
    private const string LockedOutMessage = "Bạn đã bị khóa tài khoản, xin liên hệ phòng IT.";

    /// <summary>Câu báo sai thông tin đăng nhập. Cố ý KHÔNG nói sai ở đâu (định danh hay mật khẩu) để không
    /// biến trang đăng nhập thành công cụ dò xem email nào đang có tài khoản trong hệ thống.</summary>
    private const string InvalidCredentialMessage = "Email/tên tài khoản hoặc mật khẩu không đúng.";

    private const int OtpMaxAttempts = 5;

    private const int ResetTokenExpireMinutes = 10;

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IZaloZnsService _zaloZns;
    private readonly IEsmsService _esms;
    private readonly IEmailService _email;
    private readonly IRankingEngine _rankingService;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuthService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileService _fileService;

    public AuthService(IAppDbContext db, ITokenService tokenService, IZaloZnsService zaloZns, IEsmsService esms, IEmailService email, IRankingEngine rankingService, ICurrentUser currentUser, IPasswordHasher passwordHasher, ILogger<AuthService> logger, IServiceScopeFactory scopeFactory, IFileService fileService)
    {
        _fileService = fileService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _passwordHasher = passwordHasher;
        _db = db;
        _tokenService = tokenService;
        _zaloZns = zaloZns;
        _esms = esms;
        _email = email;
        _rankingService = rankingService;
        _currentUser = currentUser;
    }

    public async Task<Result> Login(LoginRequest request)
    {
        // Một ô nhập, hai cách định danh: email hoặc tên tài khoản. Cả hai đều được lưu ở dạng chữ thường
        // (xem UserService.Create / UserNameHelper) nên chỉ cần hạ chữ chuỗi người dùng gõ là khớp được,
        // không phụ thuộc collation của từng loại database. Tên tài khoản không chứa "@" nên một chuỗi
        // không thể vừa là email của người này vừa là tên tài khoản của người khác.
        var loginId = request.Email.Trim().ToLower();

        var user = await _db.User
            .Include(x => x.Role).ThenInclude(r => r.ParentRole)
            .Include(x => x.Department)
            .Include(x => x.Position)
            .FirstOrDefaultAsync(x => x.Email.ToLower() == loginId
                                   || (x.UserName != null && x.UserName == loginId));

        // Định danh không tồn tại: không có bản ghi nào để đếm số lần sai, nên trả về không kèm con số —
        // giao diện giữ nguyên số đang hiện. Đếm cả những chuỗi gõ bừa thì một người gõ nhầm tên tài khoản
        // vài lần sẽ thấy đồng hồ tụt về 0 trong khi tài khoản thật của họ chưa hề bị làm sao.
        if (user is null)
            return Result.Unauthorized(new LoginFailedResponse(InvalidCredentialMessage, null, false));

        // Đang bị khóa thì dừng ngay TẠI ĐÂY, trước cả khi so mật khẩu: gõ đúng mật khẩu cũng không vào được,
        // và số lần sai cũng không cần cộng thêm nữa.
        if (user.LockedOutAt is not null)
            return Result.Unauthorized(new LoginFailedResponse(LockedOutMessage, 0, true));

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            var remainingAttempt = Math.Max(0, MaxLoginAttempt - user.FailedLoginCount);
            if (remainingAttempt == 0) user.LockedOutAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Result.Unauthorized(new LoginFailedResponse(
                remainingAttempt == 0
                    ? LockedOutMessage
                    : $"{InvalidCredentialMessage} Bạn còn {remainingAttempt} lần thử.",
                remainingAttempt, remainingAttempt == 0));
        }

        if (!user.IsActive)
            return Result.Unauthorized(new LoginFailedResponse("Tài khoản đã bị vô hiệu hóa.", null, false));

        // Vào được rồi thì xóa sạch dấu vết những lần gõ sai trước đó: bộ đếm chỉ tính các lần sai LIÊN TIẾP.
        user.FailedLoginCount = 0;
        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();

        // Điểm "mở hệ thống trong ngày". Đặt SAU khi đã chắc chắn đăng nhập thành công và tài khoản còn hiệu
        // lực — cộng trước thì mọi cú gõ sai mật khẩu cũng thành một lần ghi nhận.
        //
        // Chạy NỀN trong phạm vi (scope) riêng: lần đăng nhập đầu tiên mỗi ngày kéo theo cả chuỗi kiểm tra nhiệm
        // vụ / bậc hạng / chuỗi ngày học với nhiều lượt đọc-ghi database, mà người dùng đang ngồi chờ màn hình
        // đăng nhập. Phần thưởng phụ không đáng để họ chờ. Nuốt lỗi có chủ đích: hỏng nó cũng không được phép
        // ảnh hưởng đăng nhập.
        var userId = user.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ranking = scope.ServiceProvider.GetRequiredService<IRankingEngine>();
                await ranking.AwardDailyPointsAsync(userId, PointReason.DailyLogin, DateTime.Today);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Khong cong duoc diem dang nhap trong ngay cho nguoi dung {UserId}.", userId); }
        });

        var (token, expiresAt) = _tokenService.CreateToken(user);
        var isApprovalDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == user.Id);
        return Result.Success(new LoginResponse(token, expiresAt,
            ToProfile(user, isApprovalDelegate, await IsWorkflowApproverAsync(user))));
    }

    public async Task<Result> Me()
    {
        var user = await _db.User
            .Include(x => x.Role).ThenInclude(r => r.ParentRole)
            .Include(x => x.Department)
            .Include(x => x.Position)
            .FirstOrDefaultAsync(x => x.Id == _currentUser.UserId);
        if (user is null) return Result.NotFound();

        var isApprovalDelegate = await _db.ApprovalDelegate.AsNoTracking().AnyAsync(x => x.UserId == user.Id);
        return Result.Success(ToProfile(user, isApprovalDelegate, await IsWorkflowApproverAsync(user)));
    }

    public async Task<Result> UpdateMe(UpdateProfileRequest request)
    {
        var user = await _db.User
            .Include(x => x.Role).ThenInclude(r => r.ParentRole)
            .Include(x => x.Department).Include(x => x.Position)
            .FirstOrDefaultAsync(x => x.Id == _currentUser.UserId);
        if (user is null) return Result.NotFound();

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.AvatarUrl = request.AvatarUrl;

        await _db.SaveChangesAsync();
        return Result.Success(ToProfile(user));
    }

    public async Task<Result> UpdateMyCover(UpdateCoverRequest request)
    {
        var coverUrl = string.IsNullOrWhiteSpace(request.CoverUrl) ? null : request.CoverUrl.Trim();

        // Chỉ nhận tệp vừa tải lên thư mục ảnh bìa. Đường dẫn này được vẽ thẳng vào CSS background-image của
        // trang: để tự do thì một chuỗi lạ (URL ngoài, dấu nháy) chèn được vào trang của chính người đó.
        if (coverUrl is not null
            && (!coverUrl.StartsWith(CoverUrlPrefix, StringComparison.Ordinal)
                || coverUrl.Contains("..") || coverUrl.IndexOfAny(['\'', '"', '(', ')', '\\', ' ']) >= 0))
            return Result.Invalid(new MessageResponse("Ảnh bìa không hợp lệ."));

        var user = await _db.User
            .Include(x => x.Role).ThenInclude(r => r.ParentRole)
            .Include(x => x.Department).Include(x => x.Position)
            .FirstOrDefaultAsync(x => x.Id == _currentUser.UserId);
        if (user is null) return Result.NotFound();

        var oldCoverUrl = user.CoverUrl;
        user.CoverUrl = coverUrl;
        await _db.SaveChangesAsync();

        // Ảnh cũ không ai dùng nữa (mỗi người một ảnh bìa riêng) — xóa sau khi đã lưu, để lỗi lúc lưu không làm
        // mất ảnh đang hiện.
        if (oldCoverUrl is not null && oldCoverUrl != coverUrl && oldCoverUrl.StartsWith(CoverUrlPrefix, StringComparison.Ordinal))
            _fileService.Delete(oldCoverUrl);

        return Result.Success(ToProfile(user));
    }

    /// <summary>Thư mục tải lên của ảnh bìa — khớp "cover" trong UploadService.</summary>
    private const string CoverUrlPrefix = "/uploads/cover/";

    public async Task<Result> ChangePassword(ChangePasswordRequest request)
    {
        var user = await _db.User.FirstOrDefaultAsync(x => x.Id == _currentUser.UserId);
        if (user is null) return Result.NotFound();

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Invalid(new MessageResponse("Mật khẩu hiện tại không đúng."));

        if (PasswordPolicy.Validate(request.NewPassword, user.Email, user.UserName, user.EmployeeCode) is string weak)
            return Result.Invalid(new MessageResponse(weak));

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            return Result.Invalid(new MessageResponse("Mật khẩu mới phải khác mật khẩu hiện tại."));

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đổi mật khẩu thành công."));
    }

    public async Task<Result> ForgotPassword(ForgotPasswordRequest request)
    {
        var channel = (request.Channel ?? "zalo").Trim().ToLowerInvariant();
        if (channel is not ("zalo" or "sms" or "email"))
            return Result.Invalid(new MessageResponse("Kênh gửi OTP không hợp lệ."));

        var channelConfigured = channel switch
        {
            "zalo" => _zaloZns.IsConfigured,
            "sms" => _esms.IsConfigured,
            _ => _email.IsConfigured,
        };
        var channelLabel = channel switch { "zalo" => "Zalo", "sms" => "SMS", _ => "Email" };
        if (!channelConfigured)
            return Result.Invalid(new MessageResponse($"Chưa cấu hình gửi OTP qua {channelLabel}. Vui lòng liên hệ quản trị viên hệ thống."));

        // Thông báo chung chung dù email có tồn tại hay không, tránh lộ thông tin tài khoản nào đang được sử dụng.
        var genericMessage = channel == "email"
            ? "Nếu email tồn tại, mã OTP đã được gửi tới hộp thư."
            : $"Nếu email tồn tại và tài khoản đã đăng ký số điện thoại, mã OTP đã được gửi qua {channelLabel}.";

        var user = await _db.User.FirstOrDefaultAsync(x => x.Email == request.Email);
        var requirePhone = channel != "email";
        if (user is null || (requirePhone && string.IsNullOrWhiteSpace(user.PhoneNumber)) || !user.IsActive)
            return Result.Success(new MessageResponse(genericMessage));

        // Tài khoản đang bị khóa thì chặn ngay tại đây. Không chặn thì cả cơ chế khóa coi như không có: người
        // gõ sai ba lần chỉ cần bấm "Quên mật khẩu", tự đặt mật khẩu mới rồi vào lại như chưa có chuyện gì.
        // Nói thẳng lý do chứ không trả câu chung chung, để họ biết đường gọi phòng IT thay vì ngồi đợi OTP
        // không bao giờ tới.
        if (user.LockedOutAt is not null)
            return Result.Invalid(new MessageResponse(LockedOutMessage));

        // Hủy các lượt yêu cầu trước đó chưa dùng, chỉ giữ mã OTP mới nhất còn hiệu lực.
        var previousList = await _db.PasswordResetRequest
            .Where(x => x.UserId == user.Id && !x.IsUsed)
            .ToListAsync();
        foreach (var previous in previousList) previous.IsUsed = true;

        var otpCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _db.PasswordResetRequest.Add(new PasswordResetRequest
        {
            UserId = user.Id,
            OtpCodeHash = _passwordHasher.Hash(otpCode),
            OtpExpiresAt = DateTime.Now.AddMinutes(OtpExpireMinutes),
        });
        await _db.SaveChangesAsync();

        try
        {
            if (channel == "zalo") await _zaloZns.SendOtpAsync(user.PhoneNumber!, otpCode);
            else if (channel == "sms") await _esms.SendOtpAsync(user.PhoneNumber!, otpCode);
            else await _email.SendOtpAsync(user.Email, otpCode);
        }
        catch (Exception ex)
        {
            // Điểm cuối này KHÔNG cần đăng nhập. Lời của thư viện SMTP / Zalo / eSMS mang theo địa chỉ máy
            // chủ thư, tài khoản dùng để gửi và mã lỗi của nhà cung cấp — không được để nó đi ra ngoài.
            _logger.LogError(ex, "Gui OTP that bai qua kenh {Channel}", channel);
            return Result.ExternalServiceFailed(new MessageResponse(
                $"Chưa gửi được mã OTP qua {channelLabel} lúc này. Vui lòng thử lại, hoặc chọn kênh khác."));
        }

        return Result.Success(new MessageResponse(genericMessage));
    }

    public async Task<Result> VerifyOtp(VerifyOtpRequest request)
    {
        const string invalidMessage = "Mã OTP không đúng hoặc đã hết hạn.";

        var user = await _db.User.FirstOrDefaultAsync(x => x.Email == request.Email);
        if (user is null) return Result.Invalid(new MessageResponse(invalidMessage));

        var otpRequest = await _db.PasswordResetRequest
            .Where(x => x.UserId == user.Id && !x.IsUsed && !x.IsOtpVerified)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (otpRequest is null || otpRequest.OtpExpiresAt < DateTime.Now)
            return Result.Invalid(new MessageResponse(invalidMessage));

        if (otpRequest.OtpAttempts >= OtpMaxAttempts)
            return Result.Invalid(new MessageResponse("Bạn đã nhập sai quá nhiều lần. Vui lòng yêu cầu mã OTP mới."));

        if (!_passwordHasher.Verify(request.Code, otpRequest.OtpCodeHash))
        {
            otpRequest.OtpAttempts++;
            await _db.SaveChangesAsync();
            return Result.Invalid(new MessageResponse(invalidMessage));
        }

        var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        otpRequest.IsOtpVerified = true;
        otpRequest.ResetTokenHash = HashToken(resetToken);
        otpRequest.ResetTokenExpiresAt = DateTime.Now.AddMinutes(ResetTokenExpireMinutes);
        await _db.SaveChangesAsync();

        return Result.Success(new VerifyOtpResponse(resetToken));
    }

    public async Task<Result> ResetPassword(ResetPasswordRequest request)
    {
        const string invalidMessage = "Phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn. Vui lòng thực hiện lại từ đầu.";

        var tokenHash = HashToken(request.ResetToken);
        var otpRequest = await _db.PasswordResetRequest
            .Include(x => x.User)
            .Where(x => x.ResetTokenHash == tokenHash && x.IsOtpVerified && !x.IsUsed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (otpRequest is null || otpRequest.ResetTokenExpiresAt < DateTime.Now)
            return Result.Invalid(new MessageResponse(invalidMessage));

        // Chặn lần nữa ở bước cuối: mã OTP có hiệu lực 5 phút, tài khoản có thể vừa bị khóa trong khoảng đó.
        if (otpRequest.User.LockedOutAt is not null)
            return Result.Invalid(new MessageResponse(LockedOutMessage));

        // Kiểm quy tắc Ở ĐÂY chứ không chỉ ở màn hình đổi mật khẩu: đây cũng là một lối đặt mật khẩu mới,
        // và là lối mà người dùng đang vội nhất (vừa quên mật khẩu, vừa chờ OTP) nên dễ gõ đại nhất.
        if (PasswordPolicy.Validate(request.NewPassword, otpRequest.User.Email, otpRequest.User.UserName,
                otpRequest.User.EmployeeCode) is string weak)
            return Result.Invalid(new MessageResponse(weak));

        otpRequest.User.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        otpRequest.IsUsed = true;
        await _db.SaveChangesAsync();

        return Result.Success(new MessageResponse("Đặt lại mật khẩu thành công. Vui lòng đăng nhập với mật khẩu mới."));
    }

    private static string HashToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<Result> CreateHubTicket()
    {
        // Nạp đủ vai trò như lúc đăng nhập: vé mang đúng bộ claim của thẻ thường nên hub vẫn xét quyền được.
        var user = await _db.User
            .Include(x => x.Role).ThenInclude(r => r.ParentRole)
            .FirstOrDefaultAsync(x => x.Id == _currentUser.UserId);
        if (user is null) return Result.NotFound();
        if (!user.IsActive || user.LockedOutAt is not null) return Result.Forbidden();

        var (ticket, expiresAt) = _tokenService.CreateHubTicket(user);
        return Result.Success(new HubTicketResponse(ticket, expiresAt));
    }

    public async Task<Result> GetRole()
    {
        var raw = await _db.Role.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsSystem).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.FunctionKeys, x.ParentRoleId,
                ParentRoleName = x.ParentRole != null ? x.ParentRole.Name : null,
                ParentFunctionKeys = x.ParentRole != null ? x.ParentRole.FunctionKeys : null
            })
            .ToListAsync();

        // Trả quyền ĐÃ gộp quyền cha: đây là danh sách mà màn hình Người dùng dùng làm trần khi cấp quyền
        // riêng cho một người. Trả quyền riêng của vai trò thì Admin không tick nổi những quyền mà vai trò
        // đó thực sự có qua kế thừa.
        var list = raw
            .Select(x => new RoleDto(x.Id, x.Name, x.Description,
                FunctionKey.Inherit(x.FunctionKeys, x.ParentFunctionKeys),
                x.ParentRoleId, x.ParentRoleName))
            .ToList();

        // Xếp con ngay dưới cha để ô chọn vai trò hiện đúng thứ tự cây.
        var childListByParent = list.Where(x => x.ParentRoleId.HasValue)
            .GroupBy(x => x.ParentRoleId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var sortedList = new List<RoleDto>();
        foreach (var role in list.Where(x => !x.ParentRoleId.HasValue))
        {
            sortedList.Add(role);
            if (childListByParent.TryGetValue(role.Id, out var childList)) sortedList.AddRange(childList);
        }
        sortedList.AddRange(list.Where(x => !sortedList.Contains(x)));

        return Result.Success(sortedList);
    }

    /// <summary>Vai trò của người này (hoặc vai trò cha mà nó kế thừa) có đang được giao bước nào trong một luồng
    /// cấp duyệt còn hoạt động hay không — xem <see cref="UserProfileDto.IsWorkflowApprover"/>.</summary>
    private async Task<bool> IsWorkflowApproverAsync(User user)
    {
        var roleIdList = new List<int> { user.RoleId };
        if (user.Role.ParentRoleId.HasValue) roleIdList.Add(user.Role.ParentRoleId.Value);

        return await _db.ApprovalWorkflowStep.AsNoTracking()
            .AnyAsync(x => roleIdList.Contains(x.RoleId) && x.ApprovalWorkflow.IsActive);
    }

    internal static UserProfileDto ToProfile(User user, bool isApprovalDelegate = false,
        bool isWorkflowApprover = false) => new(
        user.Id, user.EmployeeCode, user.FullName, user.Email, user.Role.Name,
        user.DepartmentId, user.Department?.Name,
        user.PositionId, user.Position?.Name, user.AvatarUrl,
        user.PhoneNumber, user.CreatedAt, user.LastLoginAt, isApprovalDelegate,
        user.FunctionKeys,
        // Quyền của vai trò đã cộng sẵn quyền vai trò cha — giao diện chỉ so khớp, không tự đi gộp lại.
        FunctionKey.Inherit(user.Role.FunctionKeys, user.Role.ParentRole?.FunctionKeys),
        user.Role.ParentRole?.Name,
        isWorkflowApprover,
        user.UserName,
        user.CoverUrl
        );
}
