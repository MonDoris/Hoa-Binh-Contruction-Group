// File: API/Controllers/LessonNoteController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng LessonNote. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ILessonNoteService"/>.</summary>
public class LessonNoteController : BaseApiController
{
    private readonly ILessonNoteService _service;

    public LessonNoteController(ILessonNoteService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<LessonNoteDto>>> GetList([FromQuery] int enrollmentId, [FromQuery] int lessonId)
        => ToActionResult(await _service.GetList(enrollmentId, lessonId));

    [HttpPost]
    public async Task<ActionResult<LessonNoteDto>> Create(LessonNoteCreateRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<LessonNoteDto>> Update(int id, LessonNoteUpdateRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
