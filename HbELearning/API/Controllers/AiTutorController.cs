// File: API/Controllers/AiTutorController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng AiTutor. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IAiTutorService"/>.</summary>
public class AiTutorController : BaseApiController
{
    private readonly IAiTutorService _service;

    public AiTutorController(IAiTutorService service) => _service = service;

    [HttpGet("status")]
    public async Task<ActionResult<AiTutorStatusDto>> GetStatus(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetStatus(cancellationToken));

    /// <summary>Nhận multipart/form-data: question, sessionId (tùy chọn) và tối đa vài tệp trong trường "files".
    /// Tệp đọc hết vào bộ nhớ ở đây vì tầng Application không biết gì về IFormFile; dung lượng từng tệp được
    /// <see cref="AiTutorService"/> kiểm tra lại, còn RequestSizeLimit chặn từ cổng vào.</summary>
    [HttpPost("ask")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task Ask([FromForm] string? question, [FromForm] int? sessionId, [FromForm] List<IFormFile>? files, CancellationToken cancellationToken)
    {
        var attachmentList = new List<AiTutorAttachment>();
        foreach (var file in files ?? [])
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            attachmentList.Add(new AiTutorAttachment(file.FileName, file.ContentType, buffer.ToArray()));
        }

        await _service.Ask(new AiTutorAskRequest(question ?? string.Empty, sessionId, attachmentList),
            new ServerSentEventStream(Response), cancellationToken);
    }

    [HttpGet("session")]
    public async Task<ActionResult<IEnumerable<AiChatSessionDto>>> GetSessionList(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetSessionList(cancellationToken));

    [HttpGet("session/{id:int}")]
    public async Task<ActionResult<AiChatSessionDetailDto>> GetSession(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetSession(id, cancellationToken));

    [HttpPut("session/{id:int}")]
    public async Task<ActionResult<MessageResponse>> RenameSession(int id, AiChatSessionRenameRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.RenameSession(id, request, cancellationToken));

    [HttpDelete("session/{id:int}")]
    public async Task<ActionResult<MessageResponse>> DeleteSession(int id, CancellationToken cancellationToken)
        => ToActionResult(await _service.DeleteSession(id, cancellationToken));

    [HttpDelete("session")]
    public async Task<ActionResult<MessageResponse>> DeleteAllSession(CancellationToken cancellationToken)
        => ToActionResult(await _service.DeleteAllSession(cancellationToken));
}
