// File: Application/Common/Interfaces/IAiTranslateJobQueue.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Hàng đợi các lượt "nhờ AI dịch hàng loạt" ở màn hình Dịch ngôn ngữ.
///
/// Người dùng gửi CẢ danh sách khóa một lần (vài trăm, vài nghìn câu); dịch vụ nền tự cắt thành lô nhỏ
/// <see cref="BatchSize"/> khóa rồi dịch lần lượt. Trước đây trình duyệt tự cắt lô và chờ từng lô — mà một lô
/// trên máy chạy CPU mất nhiều phút, nên tải lại trang hay đóng tab là cắt ngang lô đang dịch dở. Giờ trình
/// duyệt chỉ hỏi tiến độ; việc dịch sống cùng máy chủ, không phụ thuộc trang còn mở hay không.
///
/// Mỗi lúc chỉ chạy MỘT lượt: Ollama chạy trên CPU, hai lượt song song không nhanh hơn mà chỉ cùng chậm đi gấp đôi.
/// </summary>
public interface IAiTranslateJobQueue
{
    /// <summary>Số khóa mỗi lô gửi cho AI (cấu hình Translation:AiBatchSize).</summary>
    int BatchSize { get; }

    /// <summary>Thêm một lượt dịch. Trả về false nếu đang có lượt khác chưa xong.</summary>
    bool TryEnqueue(AiTranslateJob job);

    /// <summary>Chờ tới khi có lượt cần chạy (dùng cho dịch vụ nền).</summary>
    ValueTask<AiTranslateJob> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Lượt theo mã, null nếu không còn giữ.</summary>
    AiTranslateJob? Get(string id);

    /// <summary>Lượt gần nhất (đang chạy, đang chờ, hoặc vừa xong) — để mở lại trang là thấy tiếp tiến độ.</summary>
    AiTranslateJob? Latest { get; }
}
