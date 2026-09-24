// File: Domain/Entities/DocumentChunk.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một đoạn văn bản (chunk) được cắt ra từ tài liệu PDF/Word đã nạp vào hệ thống, kèm vector nhúng
/// (embedding) do Ollama sinh ra. Dùng cho tìm kiếm ngữ nghĩa / hỏi đáp tài liệu đào tạo (RAG).</summary>
public class DocumentChunk
{
    public int Id { get; set; }

    /// <summary>Đoạn này từ đâu mà có: tài liệu Admin tải lên, hay nội dung khóa học do máy tự đồng bộ.
    /// Xem <see cref="KnowledgeSourceKind"/> — đây là ranh giới giữa phần người quản lý bằng tay và phần
    /// máy ghi đè mỗi lượt đồng bộ.</summary>
    public KnowledgeSourceKind SourceKind { get; set; } = KnowledgeSourceKind.UploadedDocument;

    /// <summary>Dấu vân tay (SHA-256, dạng hex) của ĐOẠN VĂN BẢN GỐC đã sinh ra chunk này — chỉ dùng cho
    /// nội dung khóa học do máy đồng bộ. Bài học không đổi chữ nào thì vân tay không đổi và lượt đồng bộ
    /// bỏ qua nó: sinh lại vector cho một bài học tốn hàng chục giây của Ollama, mà nội dung bài học thì
    /// hàng tháng mới sửa một lần. Null với tài liệu tải lên (nạp lại là do người bấm, luôn nạp thật).</summary>
    [MaxLength(64)]
    public string? SourceHash { get; set; }

    /// <summary>Tài liệu thuộc khóa học nào — null = tài liệu dùng chung toàn hệ thống.
    /// Dùng để giới hạn phạm vi tìm kiếm theo đúng khóa học người học đang xem.</summary>
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    /// <summary>Tài liệu thuộc bài học nào (không bắt buộc).</summary>
    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>Tên file gốc — cũng là khóa để xóa/nạp lại toàn bộ chunk của một tài liệu.</summary>
    [Required, MaxLength(255)]
    public string SourceFileName { get; set; } = null!;

    /// <summary>Đường dẫn tương đối tới file đã lưu trong wwwroot (nếu tài liệu được tải lên qua UploadService).</summary>
    [MaxLength(500)]
    public string? SourceFileUrl { get; set; }

    /// <summary>Thứ tự đoạn trong tài liệu (0, 1, 2...) — dùng để ghép lại ngữ cảnh liền kề khi trả lời.</summary>
    public int ChunkIndex { get; set; }

    [Required]
    public string TextContent { get; set; } = null!;

    /// <summary>Vector nhúng lưu dạng nhị phân (mỗi float 4 byte, little-endian) thay vì chuỗi JSON:
    /// nhẹ hơn ~3 lần và đọc lại nhanh hơn nhiều lần so với parse JSON cho MỌI dòng ở mỗi lần tìm kiếm.
    /// Chuyển đổi bằng <see cref="HbELearning.Domain.Common.VectorHelper"/>.</summary>
    public byte[] VectorData { get; set; } = [];

    /// <summary>Số chiều của vector — mỗi model embedding cho số chiều khác nhau (nomic-embed-text = 768).
    /// So sánh chỉ hợp lệ giữa các vector cùng model, nên lưu lại để lọc/kiểm tra.</summary>
    public int VectorDimension { get; set; }

    /// <summary>Tên model Ollama đã sinh ra vector này (VD "nomic-embed-text").</summary>
    [Required, MaxLength(100)]
    public string EmbeddingModel { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
