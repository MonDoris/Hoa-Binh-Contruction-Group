// File: API/Controllers/LessonController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Lesson. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ILessonService"/>.</summary>
public class LessonController : BaseApiController
{
    private readonly ILessonService _service;

    public LessonController(ILessonService service) => _service = service;

    [HttpGet("by-course/{courseId:int}")]
    public async Task<ActionResult<IEnumerable<LessonListDto>>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LessonDetailDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpPost]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(LessonRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPost("with-quiz")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<LessonWithQuizResponse>> CreateWithQuiz(LessonWithQuizRequest request)
        => ToActionResult(await _service.CreateWithQuiz(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, LessonRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPut("reorder")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Reorder(List<ReorderItem> itemList)
        => ToActionResult(await _service.Reorder(itemList));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
