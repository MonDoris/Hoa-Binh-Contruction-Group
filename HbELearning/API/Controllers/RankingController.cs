// File: API/Controllers/RankingController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Ranking. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IRankingService"/>.</summary>
public class RankingController : BaseApiController
{
    private readonly IRankingService _service;

    public RankingController(IRankingService service) => _service = service;

    [HttpGet("me")]
    public async Task<ActionResult<RankStatusDto>> GetMyStatus()
        => ToActionResult(await _service.GetMyStatus());

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetLeaderboard([FromQuery] int top = 50, [FromQuery] int? month = null, [FromQuery] int? year = null, [FromQuery] int? departmentId = null)
        => ToActionResult(await _service.GetLeaderboard(top, month, year, departmentId));

    [HttpGet("streak-leaderboard")]
    public async Task<ActionResult<List<StreakLeaderboardEntryDto>>> GetStreakLeaderboard([FromQuery] int top = 20, [FromQuery] string? mode = null)
        => ToActionResult(await _service.GetStreakLeaderboard(top, mode));

    [HttpGet("tier")]
    public async Task<ActionResult<List<RankTierDto>>> GetTierList()
        => ToActionResult(await _service.GetTierList());

    [HttpPost("tier")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> CreateTier(RankTierRequest request)
        => ToActionResult(await _service.CreateTier(request));

    [HttpPut("tier/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> UpdateTier(int id, RankTierRequest request)
        => ToActionResult(await _service.UpdateTier(id, request));

    [HttpDelete("tier/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> DeleteTier(int id)
        => ToActionResult(await _service.DeleteTier(id));

    [HttpPut("department-reward/{rankTierId:int}")]
    [Authorize(Roles = RoleName.Director)]
    public async Task<ActionResult<MessageResponse>> SetDepartmentReward(int rankTierId, DepartmentRankRewardRequest request)
        => ToActionResult(await _service.SetDepartmentReward(rankTierId, request));

    [HttpDelete("department-reward/{rankTierId:int}")]
    [Authorize(Roles = RoleName.Director)]
    public async Task<ActionResult<MessageResponse>> DeleteDepartmentReward(int rankTierId)
        => ToActionResult(await _service.DeleteDepartmentReward(rankTierId));

    [HttpGet("tier/{id:int}/code")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<RankVoucherCodeDto>>> GetVoucherCodeList(int id)
        => ToActionResult(await _service.GetVoucherCodeList(id));

    [HttpPost("tier/{id:int}/code")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> AddVoucherCode(int id, AddVoucherCodeRequest request)
        => ToActionResult(await _service.AddVoucherCode(id, request));

    [HttpDelete("code/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> DeleteVoucherCode(int id)
        => ToActionResult(await _service.DeleteVoucherCode(id));
}
