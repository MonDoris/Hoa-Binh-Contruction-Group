// File: Application/Features/VideoLearnings/IVideoLearningService.cs

namespace HbELearning.Application.Features.VideoLearnings;

/// <summary>Các ca sử dụng của chức năng VideoLearning. Tầng API gọi qua cổng này (xem API/Controllers/VideoLearningService.cs).</summary>
public interface IVideoLearningService
{
    /// <summary>Danh sách câu hỏi của một bài học video kèm đáp án đúng. Chỉ ĐỌC nên mở cho cả Trưởng phòng/
    /// Giám Đốc để duyệt nội dung; mọi thao tác SỬA vẫn giữ RoleName.ContentAuthor.</summary>
    Task<Result> GetForManage(int lessonId);

    Task<Result> Create(int lessonId, VideoQuestionRequest request);

    Task<Result> Update(int id, VideoQuestionRequest request);

    Task<Result> Delete(int id);

    /// <summary>Bản đồ nội dung của một video: đoạn nào là chương gì, thuộc chủ đề gì. Trả cả chương đang ẩn
    /// vì đây là màn hình soạn thảo — người soạn phải thấy được thứ mình vừa ẩn để bật lại.</summary>
    Task<Result> GetChapterForManage(int lessonId);

    Task<Result> CreateChapter(int lessonId, VideoChapterRequest request);

    Task<Result> UpdateChapter(int id, VideoChapterRequest request);

    Task<Result> DeleteChapter(int id);

    /// <summary>Mốc câu hỏi của một bài học video + vị trí xem dở lần trước. Không kèm đáp án đúng.
    /// Admin/Trưởng Nhóm gọi không kèm enrollmentId để xem thử (mọi mốc đều coi như chưa trả lời).</summary>
    Task<Result> GetForLearner(int lessonId, int enrollmentId = 0);

    /// <summary>Chấm một câu hỏi ngay khi học viên bấm gửi. Trả về CanContinue = false nếu câu bắt buộc đúng
    /// mà trả lời sai — lúc đó KHÔNG trả về đáp án đúng để học viên tự làm lại.</summary>
    Task<Result> Answer(VideoAnswerRequest request);
}
