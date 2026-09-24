// File: Domain/Entities/VideoLearning.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một câu hỏi được chèn vào giữa video bài học tại một mốc thời gian ("video cắt khúc").
/// Khi video chạy tới <see cref="AtSecond"/>, trình phát dừng lại và hiện câu hỏi; trả lời xong video chạy tiếp.</summary>
public class VideoQuestion
{
    public int Id { get; set; }

    /// <summary>Bài học chứa video — bài học phải có ContentType = Video.</summary>
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    /// <summary>Mốc thời gian chèn câu hỏi, tính bằng giây kể từ đầu video.</summary>
    public int AtSecond { get; set; }

    [Required, MaxLength(1000)]
    public string Content { get; set; } = null!;

    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;

    /// <summary>Gợi ý hiển thị kèm câu hỏi (không bắt buộc).</summary>
    [MaxLength(1000)]
    public string? Hint { get; set; }

    /// <summary>true = phải trả lời ĐÚNG mới được xem tiếp (sai thì làm lại);
    /// false = trả lời xong là video chạy tiếp, đúng/sai chỉ để ghi nhận.</summary>
    public bool MustAnswerCorrectly { get; set; }

    /// <summary>true = chặn học viên tua (seek) vượt qua mốc này khi chưa vượt qua câu hỏi.</summary>
    public bool BlockSeek { get; set; } = true;

    /// <summary>Số lần trả lời tối đa cho câu <see cref="MustAnswerCorrectly"/>. Hết lượt mà vẫn sai thì
    /// video VẪN chạy tiếp nhưng câu này bị 0 điểm — nếu không có giới hạn, một câu quá khó sẽ khóa cứng
    /// bài học, học viên không còn cách nào đi tiếp ngoài việc đoán mò cho tới khi trúng.
    /// 0 = không giới hạn (phải làm tới khi đúng). Không áp dụng cho câu không bắt buộc đúng.</summary>
    public int MaxAttempt { get; set; }

    /// <summary>Điểm của câu hỏi (giống <see cref="Question.Score"/> của bài kiểm tra).</summary>
    public decimal Score { get; set; } = 1m;

    /// <summary>Thứ tự hiển thị khi nhiều câu hỏi trùng mốc thời gian — bình thường sắp theo AtSecond.</summary>
    public int OrderIndex { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<VideoQuestionOption> OptionList { get; set; } = new List<VideoQuestionOption>();
    public ICollection<VideoQuestionAnswer> AnswerList { get; set; } = new List<VideoQuestionAnswer>();
}

/// <summary>Một lựa chọn trả lời của câu hỏi trong video.</summary>
public class VideoQuestionOption
{
    public int Id { get; set; }

    public int VideoQuestionId { get; set; }
    public VideoQuestion VideoQuestion { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Content { get; set; } = null!;

    public bool IsCorrect { get; set; }

    /// <summary>Giải thích hiện ra sau khi học viên chọn lựa chọn này.</summary>
    [MaxLength(500)]
    public string? Feedback { get; set; }

    public int OrderIndex { get; set; }
}

/// <summary>Bản ghi trả lời của một học viên cho một câu hỏi trong video — mỗi (phân công, câu hỏi) chỉ có
/// một bản ghi, làm lại thì cập nhật đè và tăng <see cref="TryCount"/>.</summary>
public class VideoQuestionAnswer
{
    public int Id { get; set; }

    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public int VideoQuestionId { get; set; }
    public VideoQuestion VideoQuestion { get; set; } = null!;

    /// <summary>Danh sách Id lựa chọn đã chọn, ngăn cách bởi dấu phẩy (VD "12,15") — chứa nhiều giá trị
    /// khi câu hỏi là loại chọn nhiều đáp án.</summary>
    [MaxLength(200)]
    public string SelectedOptionIdList { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    /// <summary>Số lần đã trả lời câu hỏi này (câu bắt buộc đúng có thể phải làm lại nhiều lần).</summary>
    public int TryCount { get; set; } = 1;

    /// <summary>Điểm thực nhận: bằng <see cref="VideoQuestion.Score"/> khi trả lời đúng, 0 khi sai.</summary>
    public decimal EarnedScore { get; set; }

    /// <summary>true = đã dùng hết số lần trả lời cho phép mà vẫn sai nên hệ thống cho đi tiếp và trừ trọn
    /// điểm câu này. Tách riêng khỏi IsCorrect = false để báo cáo phân biệt được "sai rồi thôi" với
    /// "sai tới mức bị hệ thống cho qua".</summary>
    public bool IsForcedPass { get; set; }

    public DateTime AnsweredAt { get; set; } = DateTime.Now;
}
