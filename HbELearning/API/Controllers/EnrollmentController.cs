// File: API/Controllers/EnrollmentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Enrollment. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IEnrollmentService"/>.</summary>
public class EnrollmentController : BaseApiController
{
    private readonly IEnrollmentService _service;

    public EnrollmentController(IEnrollmentService service) => _service = service;

    [HttpPost("assign")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Assign(AssignRequest request)
        => ToActionResult(await _service.Assign(request));

    [HttpPost("assign-by-target")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AssignByTarget(AssignByTargetRequest request)
        => ToActionResult(await _service.AssignByTarget(request));

    [HttpPost("self-enroll/{courseId:int}")]
    public async Task<ActionResult<MessageResponse>> SelfEnroll(int courseId)
        => ToActionResult(await _service.SelfEnroll(courseId));

    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<EnrollmentListDto>>> GetMy([FromQuery] EnrollmentStatus? status)
        => ToActionResult(await _service.GetMy(status));

    [HttpGet("my-schedule")]
    public async Task<ActionResult<IEnumerable<ScheduleItemDto>>> GetMySchedule([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => ToActionResult(await _service.GetMySchedule(from, to));

    [HttpGet("by-course/{courseId:int}")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<IEnumerable<EnrollmentListDto>>> GetByCourse(int courseId, [FromQuery] EnrollmentStatus? status, [FromQuery] int? departmentId)
        => ToActionResult(await _service.GetByCourse(courseId, status, departmentId));

    [HttpGet("by-user/{userId:int}")]
    [Authorize(Roles = RoleName.ReportViewers + "," + RoleName.Director)]
    public async Task<ActionResult<IEnumerable<EnrollmentListDto>>> GetByUser(int userId)
        => ToActionResult(await _service.GetByUser(userId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EnrollmentDetailDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Cancel(int id)
        => ToActionResult(await _service.Cancel(id));
}
