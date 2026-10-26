// File: API/Controllers/CourseController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Course. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ICourseService"/>.</summary>
public class CourseController : BaseApiController
{
    private readonly ICourseService _service;
    private readonly ICurrentUser _currentUser;

    public CourseController(ICourseService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CourseListDto>>> GetList([FromQuery] string? keyword, [FromQuery] CourseStatus? status, [FromQuery] int? departmentId, [FromQuery] int? positionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => ToActionResult(await _service.GetList(keyword, status, departmentId, positionId, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseDetailDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpGet("my-available")]
    public async Task<ActionResult<IEnumerable<CourseListDto>>> GetMyAvailable()
        => ToActionResult(await _service.GetMyAvailable());

    [HttpPost]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<CourseDetailDto>> Create(CourseCreateRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, CourseRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPatch("{id:int}/approval-workflow")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> ChangeApprovalWorkflow(int id, [FromQuery] int? workflowId)
        => ToActionResult(await _service.ChangeApprovalWorkflow(id, workflowId));

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> ChangeStatus(int id, [FromQuery] CourseStatus status)
        => ToActionResult(await _service.ChangeStatus(id, status));

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Submit(int id)
        => ToActionResult(await _service.Submit(id));

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<MessageResponse>> Approve(int id)
        => ToActionResult(await _service.Approve(id));

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<MessageResponse>> Reject(int id, CourseRejectRequest request)
        => ToActionResult(await _service.Reject(id, request));

    [HttpGet("{id:int}/approval-history")]
    public async Task<ActionResult<IEnumerable<CourseApprovalHistoryDto>>> GetApprovalHistory(int id)
        => ToActionResult(await _service.GetApprovalHistory(id));

    [HttpGet("{id:int}/step-approver-option")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<IEnumerable<CourseStepApproverOptionDto>>> GetStepApproverOptionList(int id)
        => ToActionResult(await _service.GetStepApproverOptionList(id));

    [HttpPost("{id:int}/reassign-approver")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> ReassignApprover(int id, CourseReassignApproverRequest request)
        => ToActionResult(await _service.ReassignApprover(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id, _currentUser.UserId, _currentUser.IsAdmin));

    [HttpPost("{id:int}/target")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AddTarget(int id, CourseTargetRequest request)
        => ToActionResult(await _service.AddTarget(id, request));

    [HttpDelete("target/{targetId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> RemoveTarget(int targetId)
        => ToActionResult(await _service.RemoveTarget(targetId));
}
