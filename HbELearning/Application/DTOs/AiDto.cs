// File: Application/DTOs/AiDto.cs
namespace HbELearning.Application.DTOs;

/// <summary>Yêu cầu AI soạn nội dung một bài học.
/// <para><paramref name="UseCompanyDocument"/> = true thì hệ thống tra kho tri thức "Tài liệu AI" trước,
/// lấy các đoạn tài liệu nội bộ liên quan rồi BẮT AI chỉ viết dựa trên đó — nhờ vậy bài học nói đúng
/// quy trình/quy định của chính công ty thay vì kiến thức chung chung model tự nghĩ ra.
/// <paramref name="CourseId"/> giới hạn phạm vi tra cứu đúng tài liệu của khóa học đó cộng tài liệu dùng chung.</para></summary>
public record GenerateLessonContentRequest(
    string? CourseTitle,
    string LessonTitle,
    string? Topic,
    string? Instructions,
    int? CourseId = null,
    bool UseCompanyDocument = false,
    string? DetailLevel = null);

/// <summary><paramref name="SourceList"/> là các đoạn tài liệu nội bộ AI đã dựa vào — hiện lại cho người
/// soạn bài đối chiếu, vì nội dung bám tài liệu mà không chỉ ra tài liệu nào thì không kiểm chứng được.
/// Rỗng khi không dùng kho tri thức.
///
/// <para><paramref name="WordCount"/> là số từ thân bài (không tính tiêu đề) — màn soạn bài hiện con số này
/// để người soạn biết ngay AI viết được một bài đầy đủ hay mới ra dàn ý. <paramref name="IsThin"/> = true khi
/// bài vẫn mỏng sau cả lượt viết lại, <paramref name="IsTruncated"/> = true khi bài có thể thiếu phần cuối
/// vì model chạm trần token: cả hai đều cần cảnh báo chứ không được lặng lẽ ghi đè ô nội dung.</para></summary>
public record GenerateLessonContentResponse(
    string Content,
    List<AiAnswerSourceDto> SourceList,
    int WordCount = 0,
    bool IsThin = false,
    bool IsTruncated = false);

public record GenerateQuizQuestionsRequest(string? CourseTitle, string Topic, int Count, string? Difficulty);

public record GeneratedAnswerDto(string Content, bool IsCorrect);

public record GeneratedQuestionDto(string Content, int QuestionType, string? Explanation, List<GeneratedAnswerDto> AnswerList);

public record GenerateQuizQuestionsResponse(List<GeneratedQuestionDto> QuestionList);

/// <summary>Kho tri thức hiện có gì cho khóa học này — để màn soạn bài biết trước có bật được chế độ
/// "bám sát tài liệu công ty" hay không, thay vì bấm tạo rồi mới nhận lỗi.</summary>
public record AiKnowledgeScopeDto(int DocumentCount, int ChunkCount, List<string> FileNameList, bool IsEmbeddingReady, string Message);

// ==================== AI HỌC NỘI DUNG KHÓA HỌC ====================

/// <summary>Kết quả một lượt đồng bộ nội dung khóa học vào kho tri thức.
/// <paramref name="SkippedUnitCount"/> là số mục KHÔNG đổi chữ nào nên được bỏ qua — con số này mới nói lên
/// lượt đồng bộ có thực sự phải làm gì hay chỉ chạy không, và nó là thứ giữ cho việc chạy định kỳ đủ rẻ.</summary>
public record CourseKnowledgeSyncResultDto(
    int CourseCount,
    int IndexedCourseCount,
    int IndexedUnitCount,
    int SkippedUnitCount,
    int ChunkCount,
    int RemovedChunkCount);

/// <summary>Một khóa học trong bảng "AI đã học tới đâu".</summary>
public record CourseKnowledgeItemDto(
    int CourseId,
    string CourseTitle,
    int LessonCount,
    int IndexedLessonCount,
    int ChunkCount,
    DateTime? SyncedAt);

/// <summary>Toàn cảnh phần kiến thức khóa học của AI — cho màn hình quản trị "Tài liệu AI".</summary>
public record CourseKnowledgeStatusDto(
    int CourseCount,
    int IndexedCourseCount,
    int ChunkCount,
    DateTime? LastSyncedAt,
    bool IsEmbeddingReady,
    string Message,
    List<CourseKnowledgeItemDto> ItemList);

// ==================== LỊCH SỬ HỎI ĐÁP VỚI PICO B ====================

/// <summary>Một hội thoại trong danh sách lịch sử (chưa kèm nội dung các lượt nói).</summary>
public record AiChatSessionDto(int Id, string Title, int MessageCount, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>Một lượt nói khi mở lại hội thoại cũ. <paramref name="Role"/> dùng chữ ("user"/"assistant")
/// chứ không dùng số: giao diện đọc thẳng giá trị này, mà một con số 0/1 trong JSON thì không ai đoán được
/// nghĩa lúc đọc lại mã nguồn hay lúc dò lỗi trên tab Network.</summary>
public record AiChatMessageDto(int Id, string Role, string Content, List<AiAnswerSourceDto> SourceList, DateTime CreatedAt);

/// <summary>Nội dung đầy đủ của một hội thoại cũ.</summary>
public record AiChatSessionDetailDto(int Id, string Title, DateTime CreatedAt, DateTime UpdatedAt, List<AiChatMessageDto> MessageList);

/// <summary>Đổi tên một hội thoại trong lịch sử.</summary>
public record AiChatSessionRenameRequest(string Title);

// ==================== CÂU TRẢ LỜI CÀI SẴN ====================

/// <summary>Một dòng câu trả lời cài sẵn trong màn hình quản trị.</summary>
public record AiQuickReplyDto(
    int Id, string Keyword, string Answer, string MatchType, bool IsActive, int OrderIndex, DateTime CreatedAt);

/// <summary>Thêm/sửa một câu trả lời cài sẵn. <paramref name="MatchType"/> nhận "Exact" (khớp cả câu) hoặc
/// "Contains" (chứa từ khóa) — dùng chữ thay vì số để đọc thẳng được ở tab Network lúc dò lỗi.</summary>
public record AiQuickReplyRequest(
    string Keyword, string Answer, string? MatchType = null, bool IsActive = true, int OrderIndex = 0);

/// <summary>Kết quả bấm "Thử" trên màn hình cài đặt: câu hỏi mẫu này có khớp dòng nào không, và câu trả lời
/// người dùng SẼ nhận (đã thay các chỗ giữ chỗ bằng thông tin của chính người đang thử).</summary>
public record AiQuickReplyTestResult(bool IsMatched, int? Id, string? Keyword, string? Answer);
