// File: Application/Common/Results/Result.cs

namespace HbELearning.Application.Common.Results;

/// <summary>Loại kết quả của một ca sử dụng. Mô tả bằng NGHĨA nghiệp vụ, không bằng mã HTTP — đổi sang mã
/// HTTP là việc của tầng API (xem BaseApiController.ToActionResult).</summary>
public enum ResultKind
{
    /// <summary>Thành công, có thể kèm dữ liệu.</summary>
    Success,
    /// <summary>Thành công và vừa tạo ra một bản ghi có địa chỉ riêng.</summary>
    Created,
    /// <summary>Thành công, kết quả là một tệp để tải về.</summary>
    File,
    /// <summary>Dữ liệu gửi lên không hợp lệ hoặc vi phạm quy tắc nghiệp vụ.</summary>
    Invalid,
    /// <summary>Chưa xác thực được danh tính (VD sai mật khẩu, mã OTP hết hạn).</summary>
    Unauthorized,
    /// <summary>Đã biết là ai nhưng không có quyền — không kèm lời giải thích.</summary>
    Forbidden,
    /// <summary>Không có quyền, KÈM lời giải thích cho người dùng.</summary>
    Denied,
    /// <summary>Không tìm thấy thứ được yêu cầu.</summary>
    NotFound,
    /// <summary>Xung đột với trạng thái hiện tại (VD trùng mã, đã tồn tại).</summary>
    Conflict,
    /// <summary>Dịch vụ bên ngoài (AI, SMS, Email...) trả lỗi.</summary>
    ExternalServiceFailed,
    /// <summary>Dịch vụ phụ thuộc chưa sẵn sàng (VD model AI chưa tải xong).</summary>
    ServiceUnavailable,
}

/// <summary>Tệp do ca sử dụng sinh ra để người dùng tải về.</summary>
public sealed record FileContent(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Kết quả của một ca sử dụng trong lớp Application.
///
/// Trước khi tách lớp, Controller trả thẳng Ok()/NotFound()/BadRequest() — tức nghiệp vụ nói bằng ngôn ngữ
/// của HTTP, và không gọi lại được từ chỗ nào khác ngoài một yêu cầu web (vòng lặp nền, SignalR Hub...).
/// Giờ nghiệp vụ trả Result; tầng API mới là nơi quyết định Result ấy thành mã HTTP nào.
///
/// Cố ý KHÔNG dùng Result&lt;T&gt; có kiểu: phần dữ liệu (<see cref="Value"/>) giữ nguyên đúng đối tượng mà
/// Controller cũ đã trả ra, để JSON gửi cho giao diện giống hệt như trước. Kiểu dữ liệu vẫn được tài liệu hóa
/// cho Swagger nhờ kiểu trả về ActionResult&lt;T&gt; khai báo ở Controller.
///
/// <see cref="HasValue"/> tách riêng khỏi "Value khác null" vì "thành công, không kèm gì" và "thành công, kèm
/// null" ra hai phản hồi HTTP khác nhau.
/// </summary>
public sealed class Result
{
    private Result(ResultKind kind, object? value, bool hasValue)
    {
        Kind = kind;
        Value = value;
        HasValue = hasValue;
    }

    public ResultKind Kind { get; }
    public object? Value { get; }
    public bool HasValue { get; }

    /// <summary>Tên ca sử dụng dùng để đọc lại bản ghi vừa tạo (chỉ có với <see cref="ResultKind.Created"/>).</summary>
    public string? CreatedActionName { get; private init; }
    public object? CreatedRouteValues { get; private init; }

    /// <summary>Tệp trả về (chỉ có với <see cref="ResultKind.File"/>).</summary>
    public FileContent? File { get; private init; }

    public bool IsSuccess => Kind is ResultKind.Success or ResultKind.Created or ResultKind.File;

    public static Result Success() => new(ResultKind.Success, null, false);
    public static Result Success(object? value) => new(ResultKind.Success, value, true);

    public static Result Created(string actionName, object? routeValues, object? value)
        => new(ResultKind.Created, value, true) { CreatedActionName = actionName, CreatedRouteValues = routeValues };

    public static Result FileDownload(byte[] content, string contentType, string fileName)
        => new(ResultKind.File, null, false) { File = new FileContent(content, contentType, fileName) };

    /// <summary>Tệp trả về dạng LUỒNG (chỉ có với <see cref="ResultKind.File"/>) — cho tệp lớn như gói SCORM xuất ra,
    /// dồn cả vào một mảng byte thì mỗi lượt tải chiếm trọn dung lượng gói trong RAM máy chủ. Tầng API đóng luồng
    /// sau khi gửi xong.</summary>
    public Stream? FileStream { get; private init; }

    public static Result FileStreamDownload(Stream stream, string contentType, string fileName)
        => new(ResultKind.File, null, false)
        {
            File = new FileContent(Array.Empty<byte>(), contentType, fileName),
            FileStream = stream,
        };

    public static Result Invalid() => new(ResultKind.Invalid, null, false);
    public static Result Invalid(object? error) => new(ResultKind.Invalid, error, true);

    public static Result Unauthorized() => new(ResultKind.Unauthorized, null, false);
    public static Result Unauthorized(object? error) => new(ResultKind.Unauthorized, error, true);

    public static Result Forbidden() => new(ResultKind.Forbidden, null, false);
    public static Result Denied(object? error) => new(ResultKind.Denied, error, true);

    public static Result NotFound() => new(ResultKind.NotFound, null, false);
    public static Result NotFound(object? error) => new(ResultKind.NotFound, error, true);

    public static Result Conflict() => new(ResultKind.Conflict, null, false);
    public static Result Conflict(object? error) => new(ResultKind.Conflict, error, true);

    public static Result ExternalServiceFailed(object? error) => new(ResultKind.ExternalServiceFailed, error, true);
    public static Result ServiceUnavailable(object? error) => new(ResultKind.ServiceUnavailable, error, true);
}
