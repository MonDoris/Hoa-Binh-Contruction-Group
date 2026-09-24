// File: API/Controllers/ProgressController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Progress. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IProgressService"/>.</summary>
public class ProgressController : BaseApiController
{
    private readonly IProgressService _service;

    public ProgressController(IProgressService service) => _service = service;

    [HttpGet("enrollment/{enrollmentId:int}")]
    public async Task<ActionResult<IEnumerable<PathProgressDto>>> GetPath(int enrollmentId)
        => ToActionResult(await _service.GetPath(enrollmentId));

    [HttpPost("lesson/start")]
    public async Task<ActionResult<MessageResponse>> StartLesson([FromQuery] int enrollmentId, [FromQuery] int lessonId)
        => ToActionResult(await _service.StartLesson(enrollmentId, lessonId));

    [HttpPost("lesson/heartbeat")]
    public async Task<ActionResult<MessageResponse>> Heartbeat([FromQuery] int enrollmentId, [FromQuery] int lessonId, [FromQuery] int timeSpentSecond = 0, [FromQuery] int lastPositionSecond = 0, [FromQuery] int maxWatchedSecond = 0)
        => ToActionResult(await _service.Heartbeat(enrollmentId, lessonId, timeSpentSecond, lastPositionSecond, maxWatchedSecond));

    [HttpPost("lesson/complete")]
    public async Task<ActionResult<MessageResponse>> CompleteLesson([FromQuery] int enrollmentId, [FromQuery] int lessonId)
        => ToActionResult(await _service.CompleteLesson(enrollmentId, lessonId));
}
