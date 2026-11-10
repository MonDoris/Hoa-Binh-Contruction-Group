// File: API/Controllers/AiDocuments/AiAnswerMemoryController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers.AiDocuments;

/// <summary>Điểm cuối HTTP của chức năng AiAnswerMemory. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAiAnswerMemoryService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class AiAnswerMemoryController : BaseApiController
{
    private readonly IAiAnswerMemoryService _service;

    public AiAnswerMemoryController(IAiAnswerMemoryService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<AiAnswerMemoryListDto>> GetList([FromQuery] string? keyword, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetList(keyword, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Update(int id, AiAnswerMemoryUpdateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Update(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.Delete(id, cancellationToken));

    [HttpDelete]
    public async Task<ActionResult<MessageResponse>> DeleteAll(CancellationToken cancellationToken)
        => ToActionResult(await _service.DeleteAll(cancellationToken));
}
