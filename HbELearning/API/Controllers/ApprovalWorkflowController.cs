// File: API/Controllers/ApprovalWorkflowController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng ApprovalWorkflow. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IApprovalWorkflowService"/>.</summary>
public class ApprovalWorkflowController : BaseApiController
{
    private readonly IApprovalWorkflowService _service;

    public ApprovalWorkflowController(IApprovalWorkflowService service) => _service = service;

    [HttpGet]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<IEnumerable<ApprovalWorkflowDto>>> GetList([FromQuery] bool activeOnly = false, [FromQuery] int? departmentId = null, [FromQuery] bool sharedOnly = false)
        => ToActionResult(await _service.GetList(activeOnly, departmentId, sharedOnly));

    [HttpGet("company")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowCompanyViewer)]
    public async Task<ActionResult<IEnumerable<ApprovalWorkflowDto>>> GetCompanyList([FromQuery] int? departmentId = null, [FromQuery] bool sharedOnly = false)
        => ToActionResult(await _service.GetCompanyList(departmentId, sharedOnly));

    [HttpGet("role-option")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<IEnumerable<WorkflowRoleOptionDto>>> GetRoleOptionList()
        => ToActionResult(await _service.GetRoleOptionList());

    [HttpGet("approver-option")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<IEnumerable<WorkflowApproverOptionDto>>> GetApproverOptionList([FromQuery] int roleId, [FromQuery] int? departmentId = null)
        => ToActionResult(await _service.GetApproverOptionList(roleId, departmentId));

    [HttpGet("option")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.ContentAuthor)]
    public async Task<ActionResult<IEnumerable<ApprovalWorkflowOptionDto>>> GetOptionList()
        => ToActionResult(await _service.GetOptionList());

    [HttpPost]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(ApprovalWorkflowRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, ApprovalWorkflowRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPost("{id:int}/clone")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowCompanyViewer)]
    public async Task<ActionResult<MessageResponse>> Clone(int id, CloneApprovalWorkflowRequest request)
        => ToActionResult(await _service.Clone(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.Admin + "," + RoleName.WorkflowDepartmentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
