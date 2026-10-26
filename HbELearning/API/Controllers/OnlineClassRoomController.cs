// File: API/Controllers/OnlineClassRoomController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng OnlineClassRoom. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IOnlineClassRoomService"/>.</summary>
public class OnlineClassRoomController : BaseApiController
{
    private readonly IOnlineClassRoomService _service;

    public OnlineClassRoomController(IOnlineClassRoomService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OnlineClassRoomListDto>>> GetList([FromQuery] OnlineClassRoomStatus? status)
        => ToActionResult(await _service.GetList(status));

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<OnlineClassRoomHistoryDto>>> GetHistory([FromQuery] OnlineClassRoomHistoryQuery query)
        => ToActionResult(await _service.GetHistory(query));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OnlineClassRoomListDto>> GetById(int id)
        => ToActionResult(await _service.GetById(id));

    [HttpGet("{id:int}/review")]
    public async Task<ActionResult<OnlineClassRoomReviewDto>> GetReview(int id)
        => ToActionResult(await _service.GetReview(id));

    [HttpGet("resolve-code/{code}")]
    public async Task<ActionResult<MessageResponse>> ResolveByCode(string code)
        => ToActionResult(await _service.ResolveByCode(code));

    [HttpGet("{id:int}/events")]
    public async Task<ActionResult<IEnumerable<OnlineClassRoomEventDto>>> GetEvents(int id)
        => ToActionResult(await _service.GetEvents(id));

    [HttpGet("{id:int}/recordings")]
    public async Task<ActionResult<IEnumerable<OnlineClassRoomRecordingDto>>> GetRecordings(int id)
        => ToActionResult(await _service.GetRecordings(id));

    [HttpPost("{id:int}/recording/start")]
    public async Task<ActionResult<OnlineClassRoomRecordingDto>> StartRecording(int id)
        => ToActionResult(await _service.StartRecording(id));

    [HttpPost("{id:int}/recording/{recordingId:int}/chunk")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<MessageResponse>> UploadRecordingChunk(int id, int recordingId, IFormFile file)
        => ToActionResult(await _service.UploadRecordingChunk(id, recordingId, file.ToFileUpload()));

    [HttpPost("{id:int}/recording/{recordingId:int}/finish")]
    public async Task<ActionResult<MessageResponse>> FinishRecording(int id, int recordingId, FinishRecordingRequest request)
        => ToActionResult(await _service.FinishRecording(id, recordingId, request));

    [HttpGet("ice-servers")]
    public ActionResult<List<IceServerDto>> GetIceServers()
        => ToActionResult(_service.GetIceServers());

    [HttpPost]
    [Authorize(Roles = RoleName.AllExceptAdmin)]
    public async Task<ActionResult<MessageResponse>> Create(OnlineClassRoomRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MessageResponse>> Update(int id, OnlineClassRoomRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<MessageResponse>> Cancel(int id)
        => ToActionResult(await _service.Cancel(id));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleName.Admin)]
    public async Task<ActionResult<MessageResponse>> Delete(int id)
        => ToActionResult(await _service.Delete(id));
}
