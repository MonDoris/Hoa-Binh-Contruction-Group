// File: API/Controllers/DashboardController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Dashboard. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IDashboardService"/>.</summary>
public class DashboardController : BaseApiController
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service) => _service = service;

    [HttpGet("learner")]
    public async Task<ActionResult<LearnerDashboardDto>> GetLearnerDashboard(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetLearnerDashboard(cancellationToken));

    [HttpGet("mission")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<WeeklyMissionDetailDto>>> GetMissionList(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetMissionList(cancellationToken));

    [HttpGet("mission/activity")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<MissionActivityDto>>> GetMissionActivityList()
        => ToActionResult(await _service.GetMissionActivityList());

    [HttpGet("point-rule")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<List<PointRuleDto>>> GetPointRuleList()
        => ToActionResult(await _service.GetPointRuleList());

    [HttpPut("point-rule")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> SavePointRuleList(PointRuleSaveRequest request)
        => ToActionResult(await _service.SavePointRuleList(request));

    [HttpPost("mission")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> CreateMission(WeeklyMissionRequest request)
        => ToActionResult(await _service.CreateMission(request));

    [HttpPut("mission/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> UpdateMission(int id, WeeklyMissionRequest request)
        => ToActionResult(await _service.UpdateMission(id, request));

    [HttpDelete("mission/{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> DeleteMission(int id)
        => ToActionResult(await _service.DeleteMission(id));
}
