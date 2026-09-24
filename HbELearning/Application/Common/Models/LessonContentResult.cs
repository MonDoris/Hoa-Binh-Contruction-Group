// File: Application/Common/Models/LessonContentResult.cs
// Kiểu kết quả của cổng IAiContentService, tách ra từ Infrastructure/Ai/OllamaContentService.cs.
// Chúng mô tả HỢP ĐỒNG của cổng chứ không phải chi tiết của Ollama, nên phải nằm cùng lớp với cổng —
// nếu mai này thay Ollama bằng nhà cung cấp khác, hai kiểu này không đổi.

namespace HbELearning.Application.Common.Models;

/// <summary>Kết quả một lượt soạn bài học bằng AI.
///
/// <para><paramref name="BodyWordCount"/> đếm số từ NẰM NGOÀI các thẻ tiêu đề — đó mới là phần học viên
/// thực sự đọc được. Đếm cả bài thì một dàn ý mười tiêu đề cũng ra vài chục từ và trông như đã có nội dung.</para>
/// <para><paramref name="WasExpanded"/> = true nghĩa là lượt đầu chỉ ra dàn ý nên hệ thống đã bắt model viết
/// lại lần hai; <paramref name="IsTruncated"/> = true nghĩa là model chạm trần token nên bài có thể còn
/// thiếu phần cuối — cả hai đều đáng nói cho người soạn bài biết để đọc lại trước khi lưu.</para></summary>
public record LessonContentResult(string Html, int BodyWordCount, bool WasExpanded, bool IsTruncated);

/// <summary>Một mẩu trong luồng soạn bài học. Đúng MỘT trong ba trường có giá trị:
/// <list type="bullet">
/// <item><paramref name="Token"/> — mẩu chữ model vừa viết ra, để hiện dần cho người soạn nhìn.</item>
/// <item><paramref name="IsRewriteStart"/> — lượt đầu chỉ ra dàn ý nên hệ thống bắt model viết lại từ đầu;
/// nơi gọi phải XÓA phần đã hiện, vì bản sắp tới sẽ thay thế nó hoàn toàn.</item>
/// <item><paramref name="Result"/> — mẩu CUỐI CÙNG, mang bài học đã dọn HTML cùng các số liệu để cảnh báo
/// người soạn. Đây mới là thứ được lưu vào bài học.</item>
/// </list></summary>
public record LessonStreamPiece(string? Token, bool IsRewriteStart, LessonContentResult? Result)
{
    public static LessonStreamPiece FromToken(string token) => new(token, false, null);
    public static LessonStreamPiece RewriteStart() => new(null, true, null);
    public static LessonStreamPiece FromResult(LessonContentResult result) => new(null, false, result);
}
