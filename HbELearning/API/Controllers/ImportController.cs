// File: API/Controllers/ImportController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Import. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IImportService"/>.</summary>
[Authorize(Roles = RoleName.ContentAuthor)]
public class ImportController : BaseApiController
{
    private readonly IImportService _service;

    public ImportController(IImportService service) => _service = service;

    [HttpGet("template/question")]
    public IActionResult DownloadQuestionTemplate([FromQuery] string? format)
        => ToActionResult(_service.DownloadQuestionTemplate(format));

    [HttpGet("template/question-sheet")]
    public IActionResult DownloadQuestionSheetTemplate([FromQuery] string? format)
        => ToActionResult(_service.DownloadQuestionSheetTemplate(format));

    [HttpGet("template/lesson")]
    public IActionResult DownloadLessonTemplate([FromQuery] string? format)
        => ToActionResult(_service.DownloadLessonTemplate(format));

    [HttpPost("question/{quizId:int}")]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<ImportResultDto>> ImportQuestion(int quizId, IFormFile file)
        => ToActionResult(await _service.ImportQuestion(quizId, file.ToFileUpload()));

    /// <summary>Nhập cả ngân hàng câu hỏi của khóa học từ một file Word gộp nhiều bài, tự tách theo bài.</summary>
    [HttpPost("question-document/{courseId:int}")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<QuestionDocumentResultDto>> ImportQuestionDocument(int courseId, IFormFile file, [FromQuery] string? section)
        => ToActionResult(await _service.ImportQuestionDocument(courseId, file.ToFileUpload(), ParseIndexList(section)));

    /// <summary>Nhập file Word đề thi tự do vào đúng một bài kiểm tra.</summary>
    [HttpPost("question-document/quiz/{quizId:int}")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<QuestionDocumentResultDto>> ImportQuestionDocumentToQuiz(int quizId, IFormFile file)
        => ToActionResult(await _service.ImportQuestionDocumentToQuiz(quizId, file.ToFileUpload()));

    /// <summary>Xem trước các nhóm câu hỏi tách được và bài kiểm tra sẽ nhận chúng. Không ghi gì vào hệ thống.</summary>
    [HttpPost("question-document/{courseId:int}/outline")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<QuestionOutlineDto>> GetQuestionOutline(int courseId, IFormFile file)
        => ToActionResult(await _service.GetQuestionOutline(courseId, file.ToFileUpload()));

    /// <summary>Nhập bảng câu hỏi (file mẫu cả khóa học) — gom theo cột "Bài kiểm tra" rồi đổ vào từng bài.</summary>
    [HttpPost("question-sheet/{courseId:int}")]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<QuestionDocumentResultDto>> ImportQuestionSheet(int courseId, IFormFile file, [FromQuery] string? section)
        => ToActionResult(await _service.ImportQuestionSheet(courseId, file.ToFileUpload(), ParseIndexList(section)));

    /// <summary>Xem trước các nhóm trong bảng câu hỏi và bài kiểm tra sẽ nhận chúng. Không ghi gì vào hệ thống.</summary>
    [HttpPost("question-sheet/{courseId:int}/outline")]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<QuestionOutlineDto>> GetQuestionSheetOutline(int courseId, IFormFile file)
        => ToActionResult(await _service.GetQuestionSheetOutline(courseId, file.ToFileUpload()));

    [HttpPost("lesson/{courseId:int}")]
    [RequestSizeLimit(20_971_520)]
    public async Task<ActionResult<ImportResultDto>> ImportLesson(int courseId, IFormFile file)
        => ToActionResult(await _service.ImportLesson(courseId, file.ToFileUpload()));

    // 100 MB: bài giảng PowerPoint nhiều ảnh chụp thường nặng hơn hẳn tài liệu Word / PDF cùng nội dung.
    /// <param name="section">Số thứ tự các phần cần tạo, ngăn nhau bằng dấu phẩy ("0,2,3") — do hộp thoại xem
    /// trước gửi lên khi người soạn bỏ tick vài phần. Bỏ trống = tạo hết.</param>
    [HttpPost("document/{courseId:int}")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<DocumentImportResultDto>> ImportDocument(int courseId, IFormFile file, [FromQuery] string? section)
        => ToActionResult(await _service.ImportDocument(courseId, file.ToFileUpload(), ParseIndexList(section)));

    /// <summary>Xem trước các bài học sẽ tách ra từ một file Word. Không tạo gì cả, nên không cần courseId.</summary>
    [HttpPost("document/outline")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<DocumentOutlineDto>> GetDocumentOutline(IFormFile file)
        => ToActionResult(await _service.GetDocumentOutline(file.ToFileUpload()));

    private static List<int>? ParseIndexList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var parsed) ? parsed : -1)
                .Where(x => x >= 0)
                .ToList();
}
