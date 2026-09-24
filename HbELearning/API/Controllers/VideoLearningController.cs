// File: API/Controllers/VideoLearningController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng VideoLearning. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IVideoLearningService"/>.</summary>
public class VideoLearningController : BaseApiController
{
    private readonly IVideoLearningService _service;

    public VideoLearningController(IVideoLearningService service) => _service = service;

    [HttpGet("lesson/{lessonId:int}/manage")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<IEnumerable<VideoQuestionManageDto>>> GetForManage(int lessonId)
        => ToActionResult(await _service.GetForManage(lessonId));

    [HttpPost("lesson/{lessonId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(int lessonId, VideoQuestionRequest request)
        => ToActionResult(await _service.Create(lessonId, request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, VideoQuestionRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpGet("lesson/{lessonId:int}/chapter")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<IEnumerable<VideoChapterDto>>> GetChapterForManage(int lessonId)
        => ToActionResult(await _service.GetChapterForManage(lessonId));

    [HttpPost("lesson/{lessonId:int}/chapter")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> CreateChapter(int lessonId, VideoChapterRequest request)
        => ToActionResult(await _service.CreateChapter(lessonId, request));

    [HttpPut("chapter/{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> UpdateChapter(int id, VideoChapterRequest request)
        => ToActionResult(await _service.UpdateChapter(id, request));

    [HttpDelete("chapter/{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> DeleteChapter(int id)
        => ToActionResult(await _service.DeleteChapter(id));

    [HttpGet("lesson/{lessonId:int}")]
    public async Task<ActionResult<VideoLearningDto>> GetForLearner(int lessonId, [FromQuery] int enrollmentId = 0)
        => ToActionResult(await _service.GetForLearner(lessonId, enrollmentId));

    [HttpPost("answer")]
    public async Task<ActionResult<VideoAnswerResultDto>> Answer(VideoAnswerRequest request)
        => ToActionResult(await _service.Answer(request));
}
