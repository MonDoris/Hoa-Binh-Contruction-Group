// File: API/Controllers/ApprovalDelegateController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng ApprovalDelegate. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IApprovalDelegateService"/>.</summary>
[Authorize(Roles = RoleName.Director)]
public class ApprovalDelegateController : BaseApiController
{
    private readonly IApprovalDelegateService _service;

    public ApprovalDelegateController(IApprovalDelegateService service) => _service = service;

    [HttpGet("candidates")]
    public async Task<ActionResult<IEnumerable<ApprovalDelegateCandidateDto>>> GetCandidateList()
        => ToActionResult(await _service.GetCandidateList());

    [HttpPut]
    public async Task<ActionResult<MessageResponse>> SetDelegate(SetApprovalDelegateRequest request)
        => ToActionResult(await _service.SetDelegate(request));
}
