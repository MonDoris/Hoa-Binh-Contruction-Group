// File: API/Services/ServerSentEventStream.cs
using System.Text.Json;

namespace HbELearning.API.Services;

/// <summary>
/// Hiện thực IEventStream bằng Server-Sent Events (text/event-stream) trên phản hồi HTTP hiện tại.
///
/// Header và định dạng mỗi sự kiện chép nguyên từ hàm SendEventAsync từng lặp lại ở AiController,
/// AiDocumentController và AiTutorController — giao diện đọc luồng theo đúng định dạng này.
/// </summary>
public sealed class ServerSentEventStream : IEventStream
{
    private static readonly JsonSerializerOptions JsonOption = new(JsonSerializerDefaults.Web);

    private readonly HttpResponse _response;

    /// <summary>Đặt header SSE ngay khi tạo — phải xong TRƯỚC khi byte đầu tiên được ghi ra.</summary>
    public ServerSentEventStream(HttpResponse response)
    {
        _response = response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        // Chặn proxy/IIS gom sẵn dữ liệu rồi mới đẩy đi một cục — làm vậy thì stream mất tác dụng.
        response.Headers["X-Accel-Buffering"] = "no";
    }

    public async Task SendAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOption);
        await _response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await _response.Body.FlushAsync(cancellationToken);
    }
}
