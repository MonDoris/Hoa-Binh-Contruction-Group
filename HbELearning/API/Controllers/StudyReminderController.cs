// File: API/Controllers/StudyReminderController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng StudyReminder. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IStudyReminderService"/>.</summary>
public class StudyReminderController : BaseApiController
{
    private readonly IStudyReminderService _service;

    public StudyReminderController(IStudyReminderService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudyReminderDto>>> GetList()
        => ToActionResult(await _service.GetList());

    [HttpPost]
    public async Task<ActionResult<StudyReminderDto>> Create([FromBody] StudyReminderRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StudyReminderDto>> Update(int id, [FromBody] StudyReminderRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<StudyReminderDto>> Toggle(int id)
        => ToActionResult(await _service.Toggle(id));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
