// File: API/Controllers/BaseApiController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HbELearning.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Đổi kết quả của một ca sử dụng (lớp Application) thành phản hồi HTTP.
    ///
    /// Đây là chỗ DUY NHẤT quyết định loại kết quả nào ra mã HTTP nào. Mỗi nhánh gọi lại đúng helper của
    /// ControllerBase mà Controller cũ từng gọi trực tiếp (Ok, NotFound, Forbid...), nên phản hồi — mã trạng thái
    /// lẫn thân JSON — giống hệt như trước khi tách nghiệp vụ ra khỏi Controller.
    /// </summary>
    protected ActionResult ToActionResult(Result result) => result.Kind switch
    {
        ResultKind.Success => result.HasValue ? Ok(result.Value) : Ok(),
        ResultKind.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
        ResultKind.File => result.FileStream is not null
            ? File(result.FileStream, result.File!.ContentType, result.File.FileName)
            : File(result.File!.Content, result.File.ContentType, result.File.FileName),
        ResultKind.Invalid => result.HasValue ? BadRequest(result.Value) : BadRequest(),
        ResultKind.Unauthorized => result.HasValue ? Unauthorized(result.Value) : Unauthorized(),
        ResultKind.Forbidden => Forbid(),
        ResultKind.Denied => StatusCode(StatusCodes.Status403Forbidden, result.Value),
        ResultKind.NotFound => result.HasValue ? NotFound(result.Value) : NotFound(),
        ResultKind.Conflict => result.HasValue ? Conflict(result.Value) : Conflict(),
        ResultKind.ExternalServiceFailed => StatusCode(StatusCodes.Status502BadGateway, result.Value),
        ResultKind.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Value),
        _ => throw new InvalidOperationException($"Chưa có cách đổi loại kết quả {result.Kind} sang HTTP.")
    };
}
