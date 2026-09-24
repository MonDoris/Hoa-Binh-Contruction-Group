// File: API/Controllers/AiQuickReplyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng AiQuickReply. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAiQuickReplyService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class AiQuickReplyController : BaseApiController
{
    private readonly IAiQuickReplyService _service;

    public AiQuickReplyController(IAiQuickReplyService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiQuickReplyDto>>> GetList(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetList(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AiQuickReplyDto>> Create(AiQuickReplyRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Create(request, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AiQuickReplyDto>> Update(int id, AiQuickReplyRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Update(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.Delete(id, cancellationToken));

    [HttpPost("test")]
    public async Task<ActionResult<AiQuickReplyTestResult>> Test(AiTutorAskRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Test(request, cancellationToken));
}
