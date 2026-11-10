// File: API/Controllers/Translations/TranslationController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers.Translations;

/// <summary>Điểm cuối HTTP của chức năng Translation. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ITranslationService"/>.</summary>
[Authorize(Roles = RoleName.Admin)]
public class TranslationController : BaseApiController
{
    private readonly ITranslationService _service;

    public TranslationController(ITranslationService service) => _service = service;

    [AllowAnonymous]
    [HttpGet("dictionary/{lang}")]
    public async Task<ActionResult<Dictionary<string, string>>> GetDictionary(string lang, CancellationToken cancellationToken)
        => ToActionResult(await _service.GetDictionary(lang, cancellationToken));

    [AllowAnonymous]
    [HttpGet("language")]
    public async Task<ActionResult<IEnumerable<TranslationLanguageDto>>> GetLanguageList(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetLanguageList(cancellationToken));

    [HttpPost("language")]
    public async Task<ActionResult<MessageResponse>> CreateLanguage(LanguageCreateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.CreateLanguage(request, cancellationToken));

    [HttpPut("language/{code}")]
    public async Task<ActionResult<MessageResponse>> UpdateLanguage(string code, LanguageUpdateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.UpdateLanguage(code, request, cancellationToken));

    [HttpDelete("language/{code}")]
    public async Task<ActionResult<MessageResponse>> DeleteLanguage(string code, CancellationToken cancellationToken)
        => ToActionResult(await _service.DeleteLanguage(code, cancellationToken));

    [HttpGet("group")]
    public async Task<ActionResult<IEnumerable<TranslationGroupDto>>> GetGroupList(CancellationToken cancellationToken)
        => ToActionResult(await _service.GetGroupList(cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<TranslationRowDto>>> GetList([FromQuery] string? keyword, [FromQuery] string? group, [FromQuery] string? source, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
        => ToActionResult(await _service.GetList(keyword, group, source, page, pageSize, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<MessageResponse>> Create(TranslationSaveRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Create(request, cancellationToken));

    [HttpPut]
    public async Task<ActionResult<MessageResponse>> Update(TranslationSaveRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Update(request, cancellationToken));

    [HttpPut("reset")]
    public async Task<ActionResult<MessageResponse>> Reset(TranslationResetRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.Reset(request, cancellationToken));

    [HttpDelete]
    public async Task<ActionResult<MessageResponse>> Delete([FromQuery] string resourceKey, CancellationToken cancellationToken)
        => ToActionResult(await _service.Delete(resourceKey, cancellationToken));

    [HttpPut("ai-translate")]
    public async Task<ActionResult<AiTranslateResponse>> AiTranslate(AiTranslateRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.AiTranslate(request, cancellationToken));

    // Dịch hàng loạt chạy nền: PUT để bắt đầu (cùng lý do với ai-translate — đây là ghi đè câu chữ, không phải
    // thêm mới), GET để hỏi tiến độ, PUT .../cancel để dừng.
    [HttpPut("ai-job")]
    public async Task<ActionResult<AiTranslateJobDto>> StartAiJob(AiTranslateJobRequest request, CancellationToken cancellationToken)
        => ToActionResult(await _service.StartAiJob(request, cancellationToken));

    /// <summary>Lượt gần nhất (để mở lại trang là thấy tiếp tiến độ); 204 nếu chưa có lượt nào.</summary>
    [HttpGet("ai-job")]
    public ActionResult<AiTranslateJobDto> GetLatestAiJob([FromQuery] int from = 0)
        => ToActionResult(_service.GetAiJob(null, from));

    [HttpGet("ai-job/{id}")]
    public ActionResult<AiTranslateJobDto> GetAiJob(string id, [FromQuery] int from = 0)
        => ToActionResult(_service.GetAiJob(id, from));

    [HttpPut("ai-job/{id}/cancel")]
    public ActionResult<AiTranslateJobDto> CancelAiJob(string id)
        => ToActionResult(_service.CancelAiJob(id));
}
