// File: API/Controllers/ReportController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Report. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IReportService"/>.</summary>
public class ReportController : BaseApiController
{
    private readonly IReportService _service;

    public ReportController(IReportService service) => _service = service;

    [HttpGet("public-stat")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicStatDto>> GetPublicStat()
        => ToActionResult(await _service.GetPublicStat());

    [HttpGet("overview")]
    [Authorize(Roles = RoleName.ReportViewers)]
    public async Task<ActionResult<OverviewReportDto>> GetOverview()
        => ToActionResult(await _service.GetOverview());

    [HttpGet("completion-trend")]
    [Authorize(Roles = RoleName.ReportViewers)]
    public async Task<ActionResult<CompletionTrendDto>> GetCompletionTrend([FromQuery] int? year)
        => ToActionResult(await _service.GetCompletionTrend(year));

    [HttpGet("course")]
    [Authorize(Roles = RoleName.ReportViewers)]
    public async Task<ActionResult<IEnumerable<CourseReportDto>>> GetCourseReport()
        => ToActionResult(await _service.GetCourseReport());

    [HttpGet("department")]
    [Authorize(Roles = RoleName.ReportViewers + "," + RoleName.Director)]
    public async Task<ActionResult<IEnumerable<DepartmentReportDto>>> GetDepartmentReport()
        => ToActionResult(await _service.GetDepartmentReport());

    [HttpGet("learner")]
    [Authorize(Roles = RoleName.ReportViewers + "," + RoleName.HeadOfDepartment + "," + RoleName.Director)]
    public async Task<ActionResult<IEnumerable<LearnerReportDto>>> GetLearnerReport([FromQuery] int? departmentId)
        => ToActionResult(await _service.GetLearnerReport(departmentId));
}
