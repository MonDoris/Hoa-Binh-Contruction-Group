// File: API/Controllers/QuizController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Quiz. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IQuizService"/>.</summary>
public class QuizController : BaseApiController
{
    private readonly IQuizService _service;

    public QuizController(IQuizService service) => _service = service;

    [HttpGet("by-course/{courseId:int}")]
    public async Task<ActionResult<IEnumerable<QuizListDto>>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    [HttpGet("{id:int}/manage")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<QuizManageDto>> GetForManage(int id)
        => ToActionResult(await _service.GetForManage(id));

    [HttpPost]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(QuizRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, QuizRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));

    [HttpPost("{quizId:int}/question")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AddQuestion(int quizId, QuestionRequest request)
        => ToActionResult(await _service.AddQuestion(quizId, request));

    [HttpPut("question/{questionId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> UpdateQuestion(int questionId, QuestionRequest request)
        => ToActionResult(await _service.UpdateQuestion(questionId, request));

    [HttpDelete("question/{questionId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> DeleteQuestion(int questionId)
        => ToActionResult(await _service.DeleteQuestion(questionId));

    [HttpPost("{quizId:int}/start")]
    public async Task<ActionResult<QuizStartDto>> Start(int quizId, [FromQuery] int enrollmentId)
        => ToActionResult(await _service.Start(quizId, enrollmentId));

    [HttpPost("attempt/{attemptId:int}/submit")]
    public async Task<ActionResult<QuizResultDto>> Submit(int attemptId, SubmitQuizRequest request)
        => ToActionResult(await _service.Submit(attemptId, request));

    [HttpGet("attempt/{attemptId:int}/result")]
    public async Task<ActionResult<QuizResultDto>> GetResult(int attemptId)
        => ToActionResult(await _service.GetResult(attemptId));

    [HttpGet("{quizId:int}/my-attempt")]
    public async Task<ActionResult<IEnumerable<object>>> GetMyAttempt(int quizId, [FromQuery] int enrollmentId)
        => ToActionResult(await _service.GetMyAttempt(quizId, enrollmentId));
}
