// File: API/Controllers/NotificationController.cs
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng Notification. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="INotificationService"/>.</summary>
public class NotificationController : BaseApiController
{
    private readonly INotificationService _service;

    public NotificationController(INotificationService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetList()
        => ToActionResult(await _service.GetList());

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
        => ToActionResult(await _service.GetUnreadCount());

    [HttpPost("{id:int}/read")]
    public async Task<ActionResult<MessageResponse>> MarkAsRead(int id)
        => ToActionResult(await _service.MarkAsRead(id));

    [HttpPost("read-all")]
    public async Task<ActionResult<MessageResponse>> MarkAllAsRead()
        => ToActionResult(await _service.MarkAllAsRead());
}
