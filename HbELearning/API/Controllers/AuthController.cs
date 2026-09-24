// File: API/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Auth. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAuthService"/>.</summary>
public class AuthController : BaseApiController
{
    private readonly IAuthService _service;

    public AuthController(IAuthService service) => _service = service;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
        => ToActionResult(await _service.Login(request));

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> Me()
        => ToActionResult(await _service.Me());

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMe(UpdateProfileRequest request)
        => ToActionResult(await _service.UpdateMe(request));

    [HttpPost("change-password")]
    public async Task<ActionResult<MessageResponse>> ChangePassword(ChangePasswordRequest request)
        => ToActionResult(await _service.ChangePassword(request));

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(ForgotPasswordRequest request)
        => ToActionResult(await _service.ForgotPassword(request));

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(VerifyOtpRequest request)
        => ToActionResult(await _service.VerifyOtp(request));

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest request)
        => ToActionResult(await _service.ResetPassword(request));

    [HttpGet("role")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetRole()
        => ToActionResult(await _service.GetRole());
}
