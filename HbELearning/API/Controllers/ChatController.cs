// File: API/Controllers/ChatController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Chat. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="IChatService"/>.</summary>
public class ChatController : BaseApiController
{
    private readonly IChatService _service;

    public ChatController(IChatService service) => _service = service;

    [HttpGet("conversation")]
    public async Task<ActionResult<IEnumerable<ConversationDto>>> GetConversationList()
        => ToActionResult(await _service.GetConversationList());

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
        => ToActionResult(await _service.GetUnreadCount());

    [HttpPost("direct/{userId:int}")]
    public async Task<ActionResult<ConversationDto>> OpenDirect(int userId)
        => ToActionResult(await _service.OpenDirect(userId));

    [HttpPost("group")]
    public async Task<ActionResult<ConversationDto>> CreateGroup(CreateGroupRequest request)
        => ToActionResult(await _service.CreateGroup(request));

    [HttpPut("group/{id:int}")]
    public async Task<ActionResult<MessageResponse>> RenameGroup(int id, RenameGroupRequest request)
        => ToActionResult(await _service.RenameGroup(id, request));

    [HttpPost("group/{id:int}/avatar")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<ConversationDto>> ChangeGroupAvatar(int id, IFormFile file)
        => ToActionResult(await _service.ChangeGroupAvatar(id, file.ToFileUpload()));

    [HttpDelete("group/{id:int}/avatar")]
    public async Task<ActionResult<MessageResponse>> RemoveGroupAvatar(int id)
        => ToActionResult(await _service.RemoveGroupAvatar(id));

    [HttpPost("conversation/{id:int}/background")]
    public async Task<ActionResult<MessageResponse>> SetBackground(int id, SetBackgroundRequest request)
        => ToActionResult(await _service.SetBackground(id, request));

    [HttpGet("conversation/{id:int}/member")]
    public async Task<ActionResult<IEnumerable<GroupMemberDto>>> GetMemberList(int id)
        => ToActionResult(await _service.GetMemberList(id));

    [HttpPost("group/{id:int}/member")]
    public async Task<ActionResult<MessageResponse>> AddMember(int id, AddGroupMemberRequest request)
        => ToActionResult(await _service.AddMember(id, request));

    [HttpDelete("group/{id:int}/member/{userId:int}")]
    public async Task<ActionResult<MessageResponse>> RemoveMember(int id, int userId)
        => ToActionResult(await _service.RemoveMember(id, userId));

    [HttpPost("group/{id:int}/leave")]
    public async Task<ActionResult<MessageResponse>> LeaveGroup(int id)
        => ToActionResult(await _service.LeaveGroup(id));

    [HttpPost("conversation/{id:int}/background-image")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<UploadResultDto>> SetBackgroundImage(int id, IFormFile file)
        => ToActionResult(await _service.SetBackgroundImage(id, file.ToFileUpload()));

    [HttpGet("conversation/{id:int}/message")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetMessageList(int id, int? beforeId)
        => ToActionResult(await _service.GetMessageList(id, beforeId));

    [HttpPost("conversation/{id:int}/message")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(int id, SendMessageRequest request)
        => ToActionResult(await _service.SendMessage(id, request));

    [HttpPost("conversation/{id:int}/read")]
    public async Task<ActionResult<MessageResponse>> MarkRead(int id)
        => ToActionResult(await _service.MarkRead(id));

    [HttpPost("message/{id:int}/delete")]
    public async Task<ActionResult<MessageResponse>> DeleteMessage(int id)
        => ToActionResult(await _service.DeleteMessage(id));

    [HttpPost("attachment")]
    [RequestSizeLimit(ChatService.MaxAttachmentByte + 1024 * 1024)]
    public async Task<ActionResult<UploadResultDto>> UploadAttachment(IFormFile file)
        => ToActionResult(await _service.UploadAttachment(file.ToFileUpload()));

    [HttpGet("contact")]
    public async Task<ActionResult<IEnumerable<ChatContactDto>>> GetContactList(string? keyword)
        => ToActionResult(await _service.GetContactList(keyword));
}
