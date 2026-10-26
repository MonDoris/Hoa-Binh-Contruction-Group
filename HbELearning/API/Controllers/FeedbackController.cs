// File: API/Controllers/FeedbackController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Feedback khóa học. Quyền chi tiết (ai thấy gì, ai trả lời) do
/// <see cref="IFeedbackService"/> quyết định theo phân công và người soạn khóa, không theo tên vai trò.</summary>
public class FeedbackController : BaseApiController
{
    private readonly IFeedbackService _service;

    public FeedbackController(IFeedbackService service) => _service = service;

    [HttpGet("course/{courseId:int}")]
    public async Task<ActionResult<CourseFeedbackListDto>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    [HttpPost("course/{courseId:int}/comment")]
    public async Task<ActionResult<CourseFeedbackDto>> AddComment(int courseId, FeedbackCommentRequest request)
        => ToActionResult(await _service.AddComment(courseId, request));

    [HttpPut("course/{courseId:int}/review")]
    public async Task<ActionResult<CourseFeedbackDto>> SaveReview(int courseId, FeedbackReviewRequest request)
        => ToActionResult(await _service.SaveReview(courseId, request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CourseFeedbackDto>> UpdateComment(int id, FeedbackUpdateRequest request)
        => ToActionResult(await _service.UpdateComment(id, request));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
