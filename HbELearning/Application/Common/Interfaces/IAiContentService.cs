// File: Application/Common/Interfaces/IAiContentService.cs
// Cong (port) tach ra tu Infrastructure/Ai/OllamaContentService.cs khi chuyen sang Clean Architecture.

namespace HbELearning.Application.Common.Interfaces;

public interface IAiContentService
{
    /// <summary>Máy chủ AI nội bộ đã được cấu hình hay chưa. Không kiểm tra Ollama có đang CHẠY không —
    /// việc đó phải gọi mạng, mà thuộc tính đồng bộ thì không gọi được; Ollama tắt sẽ lộ ra ở lượt gọi thật
    /// dưới dạng lỗi 502 kèm câu hướng dẫn.</summary>
    bool IsConfigured { get; }

    /// <summary>Tên model đang dùng (VD "qwen2.5:7b") — dùng cho log và câu báo lỗi.</summary>
    string Model { get; }

    /// <summary>Soạn nội dung bài học. <paramref name="companyContextList"/> là các đoạn tài liệu nội bộ
    /// lấy từ kho tri thức "Tài liệu AI": truyền vào thì AI bị buộc viết BÁM SÁT chính các đoạn đó
    /// (đúng quy trình/quy định của công ty); để rỗng thì AI viết theo kiến thức chung như trước.
    ///
    /// <para><paramref name="detailLevel"/> nhận "short" | "standard" | "detailed" — quyết định độ dài bài
    /// học yêu cầu model viết và trần token cho lượt gọi. Trả về cả số từ THÂN BÀI để nơi gọi báo cho người
    /// soạn biết bài dày hay mỏng, xem <see cref="LessonContentResult"/>.</para></summary>
    Task<LessonContentResult> GenerateLessonContentAsync(string? courseTitle, string lessonTitle, string? topic,
        string? instructions, IReadOnlyList<DocumentChunkMatchDto>? companyContextList = null,
        string? detailLevel = null, CancellationToken cancellationToken = default);

    /// <summary>Y hệt <see cref="GenerateLessonContentAsync"/> nhưng trả CHỮ RA DẦN ngay khi model viết,
    /// thay vì bắt người soạn nhìn màn hình đứng im 4-6 phút rồi mới thấy cả bài hiện ra một lúc.
    ///
    /// Phần chữ trả dần là BẢN NHÁP THÔ của model — chưa qua bước dọn HTML, và có thể bị BỎ HẲN nếu lượt
    /// đầu chỉ ra dàn ý (khi đó phát <see cref="LessonStreamPiece.IsRewriteStart"/> để nơi gọi xóa phần
    /// đang hiện đi và chờ bản viết lại). Bản dùng được là <see cref="LessonStreamPiece.Result"/> ở mẩu
    /// cuối cùng — đừng lưu phần chữ trả dần vào bài học.</summary>
    IAsyncEnumerable<LessonStreamPiece> StreamLessonContentAsync(string? courseTitle, string lessonTitle, string? topic,
        string? instructions, IReadOnlyList<DocumentChunkMatchDto>? companyContextList = null,
        string? detailLevel = null, CancellationToken cancellationToken = default);

    Task<List<GeneratedQuestionDto>> GenerateQuizQuestionsAsync(string? courseTitle, string topic, int count, string? difficulty);

    /// <summary>Dịch một LÔ câu chữ giao diện sang ngôn ngữ khác, trả về từ điển khóa dịch -> câu đã dịch.
    ///
    /// Dịch theo lô chứ không từng câu một không phải để tiết kiệm lượt gọi: bảng dịch có hàng nghìn khóa,
    /// gọi mỗi câu một lượt thì vừa chậm vừa mất ngữ cảnh — model nhìn cả cụm câu cùng một màn hình mới
    /// dịch nhất quán được ("Lưu" / "Hủy" / "Xóa" của cùng một hộp thoại).
    ///
    /// Khóa nào model không trả về thì KHÔNG có mặt trong kết quả — nơi gọi phải coi đó là dịch trượt chứ
    /// đừng suy ra chuỗi rỗng: ghi chuỗi rỗng đè lên là xóa mất chữ đang hiển thị trên giao diện.
    ///
    /// Truyền cả TÊN lẫn MÃ ngôn ngữ (vi-VN, ja-JP): tên hiển thị được đặt bằng chính tiếng đó ("日本語"),
    /// mà model chạy nội bộ nhìn mấy chữ Hán trong tên là dịch nhầm sang tiếng Trung — mã ngôn ngữ theo
    /// chuẩn là thứ duy nhất không mập mờ.</summary>
    Task<Dictionary<string, string>> TranslateUiTextAsync(
        string sourceLanguageName, string sourceLanguageTag,
        string targetLanguageName, string targetLanguageTag,
        IReadOnlyList<UiTextToTranslateDto> itemList, CancellationToken cancellationToken = default);
}
