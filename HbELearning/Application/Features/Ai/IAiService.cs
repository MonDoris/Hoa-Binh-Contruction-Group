// File: Application/Features/Ai/IAiService.cs
using System.Text.Json;

namespace HbELearning.Application.Features.Ai;

/// <summary>Các ca sử dụng của chức năng Ai. Tầng API gọi qua cổng này (xem API/Controllers/AiService.cs).</summary>
public interface IAiService
{
    /// <summary>Kho tri thức có gì dùng được cho khóa học này — màn soạn bài gọi trước để biết có bật được
    /// chế độ "bám sát tài liệu công ty" không, thay vì để người dùng bấm tạo rồi mới nhận lỗi.</summary>
    Task<Result> GetKnowledgeScope(int? courseId, CancellationToken cancellationToken);

    Task<Result> GenerateLessonContent(GenerateLessonContentRequest request, CancellationToken cancellationToken);

    /// <summary>Y hệt <see cref="GenerateLessonContent"/> nhưng trả kết quả dạng SSE (text/event-stream):
    /// bảng nguồn tài liệu hiện ra ngay sau vài giây, rồi chữ của bài học hiện dần đúng lúc model viết.
    ///
    /// Lý do có endpoint riêng thay vì đổi luôn endpoint cũ: soạn một bài học trên máy chạy CPU mất 4-6
    /// phút, và suốt quãng đó bản chờ-trọn-gói không nói được gì ngoài một vòng xoay — người soạn không phân
    /// biệt được máy đang viết hay đã treo. Endpoint cũ vẫn giữ nguyên cho các nơi gọi khác và làm đường lui
    /// khi trình duyệt không đọc được luồng.
    ///
    /// Các sự kiện gửi đi:
    ///   sources  - danh sách đoạn tài liệu công ty sẽ dùng (gửi ngay, trước khi model bắt đầu viết)
    ///   token    - một mẩu chữ model vừa viết (BẢN NHÁP THÔ, chỉ để xem cho biết tiến độ)
    ///   rewrite  - lượt đầu chỉ ra dàn ý, hệ thống đang bắt model viết lại: giao diện phải xóa phần đã hiện
    ///   result   - bài học hoàn chỉnh đã dọn HTML kèm số liệu cảnh báo — ĐÂY mới là thứ được lưu
    ///   error    - không đi tiếp được, kèm câu giải thích cho người soạn
    /// </summary>
    Task GenerateLessonContentStream(GenerateLessonContentRequest request, IEventStream events, CancellationToken cancellationToken);

    Task<Result> GenerateQuizQuestions(GenerateQuizQuestionsRequest request);
}
