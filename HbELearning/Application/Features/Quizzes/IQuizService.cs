// File: Application/Features/Quizzes/IQuizService.cs

namespace HbELearning.Application.Features.Quizzes;

/// <summary>Các ca sử dụng của chức năng Quiz. Tầng API gọi qua cổng này (xem API/Controllers/QuizService.cs).</summary>
public interface IQuizService
{
    Task<Result> GetByCourse(int courseId);

    /// <summary>Chi tiết bài kiểm tra kèm đáp án đúng. Chỉ ĐỌC nên mở cho cả Trưởng phòng/Giám Đốc: phải xem
    /// được đáp án thì mới duyệt được nội dung. Mọi thao tác SỬA vẫn giữ RoleName.ContentAuthor.</summary>
    Task<Result> GetForManage(int id);

    Task<Result> Create(QuizRequest request);

    Task<Result> Update(int id, QuizRequest request);

    Task<Result> Delete(int id);

    Task<Result> AddQuestion(int quizId, QuestionRequest request);

    Task<Result> UpdateQuestion(int questionId, QuestionRequest request);

    Task<Result> DeleteQuestion(int questionId);

    /// <summary>Bắt đầu một lượt làm bài. Không trả về đáp án đúng.</summary>
    Task<Result> Start(int quizId, int enrollmentId);

    /// <summary>Nộp bài và chấm điểm tự động.</summary>
    Task<Result> Submit(int attemptId, SubmitQuizRequest request);

    /// <summary>Xem lại kết quả một lượt làm bài (kèm đáp án đúng và giải thích).</summary>
    Task<Result> GetResult(int attemptId);

    /// <summary>Lịch sử các lượt làm bài của tôi trên một bài kiểm tra.</summary>
    Task<Result> GetMyAttempt(int quizId, int enrollmentId);
}
