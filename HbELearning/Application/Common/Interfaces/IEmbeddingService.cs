// File: Application/Common/Interfaces/IEmbeddingService.cs
// Cong (port) tach ra tu Infrastructure/Ai/OllamaEmbeddingService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IEmbeddingService
{
    /// <summary>Tên model embedding đang dùng — lưu kèm vector vì vector của 2 model khác nhau không so sánh được với nhau.</summary>
    string Model { get; }

    /// <summary>Sinh vector nhúng cho một đoạn văn bản.</summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Sinh vector nhúng cho nhiều đoạn trong MỘT lần gọi — nhanh hơn nhiều so với gọi lần lượt từng đoạn.
    /// Thứ tự kết quả trùng với thứ tự đầu vào.</summary>
    Task<List<float[]>> GetEmbeddingListAsync(IReadOnlyList<string> textList, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra Ollama có đang chạy và đã cài đúng model chưa — để màn hình quản trị báo trước
    /// cho người dùng thay vì để họ tải file lên rồi mới nhận lỗi.
    /// Truyền <paramref name="chatModel"/> để kiểm tra luôn model sinh câu trả lời trong cùng một lần gọi.</summary>
    Task<AiDocumentStatusDto> CheckStatusAsync(string? chatModel = null, CancellationToken cancellationToken = default);
}
