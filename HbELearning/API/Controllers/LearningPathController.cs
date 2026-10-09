// File: API/Controllers/LearningPathController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

/// <summary>Điểm cuối HTTP của chức năng LearningPath. Controller chỉ nhận yêu cầu và trả phản hồi — nghiệp vụ nằm ở <see cref="ILearningPathService"/>.</summary>
[Authorize]
public class LearningPathController : BaseApiController
{
    private readonly ILearningPathService _service;

    public LearningPathController(ILearningPathService service) => _service = service;

    [HttpGet("by-course/{courseId:int}")]
    [Authorize(Roles = RoleName.CourseViewers)]
    public async Task<ActionResult<LearningPathDto>> GetByCourse(int courseId)
        => ToActionResult(await _service.GetByCourse(courseId));

    /// <summary>Khóa học chưa có cấu trúc thì lưu cấu trúc mặc định xuống để bắt đầu chỉnh sửa; trả về cấu trúc.</summary>
    [HttpPost("ensure/{courseId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<LearningPathDto>> EnsureOrganization(int courseId)
        => ToActionResult(await _service.EnsureOrganization(courseId));

    [HttpPost]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Create(LearningPathRequest request)
        => ToActionResult(await _service.Create(request));

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> Update(int id, LearningPathRequest request)
        => ToActionResult(await _service.Update(id, request));

    [HttpPost("auto-generate/{courseId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AutoGenerate(int courseId)
        => ToActionResult(await _service.AutoGenerate(courseId));

    [HttpPost("{pathId:int}/item")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> AddItem(int pathId, PathItemRequest request)
        => ToActionResult(await _service.AddItem(pathId, request));

    [HttpPut("item/{itemId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> UpdateItem(int itemId, PathItemUpdateRequest request)
        => ToActionResult(await _service.UpdateItem(itemId, request));

    /// <summary>Lưu cả cây sau khi kéo thả — danh sách mọi mục theo thứ tự mới, kèm chương cha.</summary>
    [HttpPut("{pathId:int}/tree")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> MoveTree(int pathId, List<PathItemPlacement> placementList)
        => ToActionResult(await _service.MoveTree(pathId, placementList));

    [HttpPut("item/{itemId:int}/schedule")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> UpdateItemSchedule(int itemId, PathItemScheduleRequest request)
        => ToActionResult(await _service.UpdateItemSchedule(itemId, request));

    [HttpPut("item/reorder")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> ReorderItem(List<ReorderItem> itemList)
        => ToActionResult(await _service.ReorderItem(itemList));

    [HttpDelete("item/{itemId:int}")]
    [Authorize(Roles = RoleName.ContentAuthor)]
    public async Task<ActionResult<MessageResponse>> DeleteItem(int itemId)
        => ToActionResult(await _service.DeleteItem(itemId));
}
