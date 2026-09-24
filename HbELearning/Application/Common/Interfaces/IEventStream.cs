// File: Application/Common/Interfaces/IEventStream.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Luồng sự kiện đẩy dần về cho người dùng trong lúc một việc dài đang chạy (AI viết bài, AI trả lời).
///
/// Ba ca sử dụng AI từng tự ghi thẳng vào HttpResponse theo định dạng Server-Sent Events — tức nghiệp vụ biết cả
/// header HTTP lẫn cú pháp "event:/data:" của giao thức. Giờ nó chỉ nói "gửi sự kiện tên X kèm dữ liệu Y"; cách
/// đẩy đi là việc của tầng API (xem API/Services/ServerSentEventStream.cs).
/// </summary>
public interface IEventStream
{
    /// <summary>Gửi một sự kiện và đẩy đi ngay, không chờ đầy bộ đệm.</summary>
    Task SendAsync(string eventName, object data, CancellationToken cancellationToken);
}
