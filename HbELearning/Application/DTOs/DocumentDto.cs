// File: Application/DTOs/DocumentDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Kết quả nạp một tài liệu PDF/Word vào kho tri thức.</summary>
public record DocumentIngestResultDto(
    string SourceFileName,
    int ChunkCount,
    int CharacterCount,
    string EmbeddingModel,
    int RemovedOldChunkCount);

/// <summary>Một đoạn tài liệu khớp với câu hỏi tìm kiếm, kèm điểm tương đồng (0..1, càng cao càng sát).</summary>
public record DocumentChunkMatchDto(
    int Id,
    string SourceFileName,
    int ChunkIndex,
    string TextContent,
    double Score,
    int? CourseId,
    int? LessonId);

/// <summary>Trạng thái máy chủ Ollama — hiện trên màn hình quản trị để biết trước có nạp tài liệu được không.</summary>
public record AiDocumentStatusDto(
    bool IsReady, string BaseUrl, string Model, string Message,
    string? ChatModel = null, bool IsChatReady = false);

/// <summary>Một tài liệu đã nạp trong kho tri thức (gộp các đoạn theo tên file).</summary>
public record AiDocumentListItemDto(
    string SourceFileName,
    string? SourceFileUrl,
    int ChunkCount,
    int? CourseId,
    string? CourseTitle,
    string EmbeddingModel,
    DateTime CreatedAt);

/// <summary>Câu hỏi thử tìm kiếm ngữ nghĩa trên kho tri thức (dùng để quản trị viên kiểm tra chất lượng nạp).</summary>
public record AiDocumentSearchRequest(string Query, int TopCount = 5, int? CourseId = null);

/// <summary>Câu hỏi gửi cho trợ lý tài liệu AI.</summary>
public record AiDocumentAskRequest(string Question, int? CourseId = null);

/// <summary>Một nguồn được trích dẫn kèm câu trả lời — số [1], [2]... trong câu trả lời ứng với Index ở đây.</summary>
public record AiAnswerSourceDto(int Index, string SourceFileName, int ChunkIndex, string TextContent, double Score);

/// <summary>Câu hỏi học viên gửi cho HB Minerva.
///
/// Không có CourseId như <see cref="AiDocumentAskRequest"/>: phạm vi tài liệu của học viên do máy chủ tự
/// suy ra từ các khóa học đã được phân công, không phải thứ phía trình duyệt gửi lên và chọn được.</summary>
/// <para><paramref name="SessionId"/> là hội thoại đang mở trong lịch sử. Null = bắt đầu hội thoại mới,
/// máy chủ tự tạo và báo lại mã qua sự kiện SSE "session" để trình duyệt gắn các câu sau vào đúng chỗ.</para></summary>
public record AiTutorAskRequest(string Question, int? SessionId = null, IReadOnlyList<AiTutorAttachment>? AttachmentList = null);

/// <summary>Một tệp người học đính kèm vào câu hỏi gửi HB Minerva (ảnh, PDF, Word, văn bản). Giữ nguyên byte trong
/// bộ nhớ cho đúng một lượt hỏi — không lưu xuống đĩa, không ghi vào lịch sử (lịch sử chỉ nhớ TÊN tệp).</summary>
public record AiTutorAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Một lượt hỏi-đáp đã xong, dùng làm ngữ cảnh cho câu hỏi kế tiếp trong cùng hội thoại — nhờ nó
/// người dùng hỏi tiếp được kiểu "còn trường hợp nghỉ nửa ngày thì sao?" mà không phải nhắc lại chủ đề.</summary>
public record AiChatTurnDto(string Question, string Answer);

/// <summary>Trạng thái HB Minerva hiện cho học viên: chỉ "dùng được hay không" kèm một câu giải thích.
///
/// Cố ý KHÔNG mang địa chỉ máy chủ AI và tên model như <see cref="AiDocumentStatusDto"/> — đó là thông tin
/// hạ tầng, chỉ có ích cho quản trị viên và không nên lộ ra mọi tài khoản học viên.</summary>
public record AiTutorStatusDto(bool IsReady, string Message);
