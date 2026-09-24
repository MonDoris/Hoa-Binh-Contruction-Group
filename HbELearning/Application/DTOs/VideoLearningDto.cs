// File: Application/DTOs/VideoLearningDto.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Application.DTOs;

// ==================== QUẢN TRỊ (Admin / Trưởng Nhóm) ====================

/// <summary>Câu hỏi trong video kèm đáp án đúng — chỉ trả cho người quản trị nội dung.</summary>
public record VideoQuestionManageDto(int Id, int LessonId, int AtSecond, string Content, string QuestionType,
    string? Hint, bool MustAnswerCorrectly, bool BlockSeek, int MaxAttempt, decimal Score, int OrderIndex,
    bool IsActive, int AnsweredCount, int ForcedPassCount, List<VideoQuestionOptionManageDto> OptionList);

public record VideoQuestionOptionManageDto(int Id, string Content, bool IsCorrect, string? Feedback, int OrderIndex);

public class VideoQuestionRequest
{
    [Range(0, 360000)] public int AtSecond { get; set; }
    [Required, MaxLength(1000)] public string Content { get; set; } = null!;
    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;
    [MaxLength(1000)] public string? Hint { get; set; }
    public bool MustAnswerCorrectly { get; set; }
    public bool BlockSeek { get; set; } = true;
    /// <summary>0 = không giới hạn số lần trả lời.</summary>
    [Range(0, 20)] public int MaxAttempt { get; set; }
    [Range(0, 1000)] public decimal Score { get; set; } = 1m;
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public List<VideoQuestionOptionRequest> OptionList { get; set; } = new();
}

public class VideoQuestionOptionRequest
{
    [Required, MaxLength(500)] public string Content { get; set; } = null!;
    public bool IsCorrect { get; set; }
    [MaxLength(500)] public string? Feedback { get; set; }
    public int OrderIndex { get; set; }
}

// ==================== CHƯƠNG MỤC TRONG VIDEO ====================

/// <summary>Một đoạn nội dung của video: từ giây nào tới giây nào là chương gì, thuộc chủ đề gì.
/// Dùng chung cho cả người soạn bài lẫn học viên — dữ liệu này không có gì phải giấu, khác câu hỏi
/// (câu hỏi phải cắt đáp án đúng trước khi trả cho học viên).</summary>
public record VideoChapterDto(int Id, int StartSecond, int EndSecond, string Title, string? Topic,
    int OrderIndex, bool IsActive);

public class VideoChapterRequest
{
    [Range(0, 360000)] public int StartSecond { get; set; }
    [Range(0, 360000)] public int EndSecond { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = null!;
    [MaxLength(250)] public string? Topic { get; set; }
    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
}

// ==================== HỌC VIÊN ====================

/// <summary>Toàn bộ dữ liệu trình phát cần cho một bài học video: danh sách mốc câu hỏi + vị trí xem dở lần
/// trước + tổng điểm câu hỏi trong video (để trình phát hiện thanh điểm ngay từ lúc mở bài).
/// <paramref name="SeekPolicy"/> là cách tua do người soạn bài chọn (Free / WatchFull / JumpToQuestion) và
/// <paramref name="MaxWatchedSecond"/> là mốc xa nhất học viên đã xem — trình phát cần cả hai để biết khóa
/// thanh tiến trình tới đâu ngay khi vừa mở bài, chưa cần chờ học viên phát video.
/// <paramref name="ChapterList"/> là bản đồ nội dung theo thời gian: trình phát dựa vào đó để hiện "đoạn
/// đang xem thuộc chương nào, chủ đề nào". Đi kèm luôn trong payload này chứ không tách thành một lượt gọi
/// riêng — hai thứ luôn được dùng cùng lúc, tách ra chỉ thêm một vòng chờ mạng trước khi video hiện ra.</summary>
public record VideoLearningDto(int LessonId, int LastPositionSecond, decimal EarnedScore, decimal TotalScore,
    string SeekPolicy, int MaxWatchedSecond, List<VideoCheckpointDto> CheckpointList,
    List<VideoChapterDto> ChapterList);

/// <summary>Một mốc câu hỏi nhìn từ phía học viên — KHÔNG chứa đáp án đúng để không lộ qua DevTools.
/// <paramref name="MaxAttempt"/> = 0 nghĩa là không giới hạn số lần trả lời.</summary>
public record VideoCheckpointDto(int Id, int AtSecond, string Content, string QuestionType, string? Hint,
    bool MustAnswerCorrectly, bool BlockSeek, int MaxAttempt, decimal Score, bool IsAnswered, bool IsPassed,
    bool IsForcedPass, decimal EarnedScore, int TryCount, List<VideoOptionDto> OptionList);

public record VideoOptionDto(int Id, string Content);

public class VideoAnswerRequest
{
    [Required] public int EnrollmentId { get; set; }
    [Required] public int VideoQuestionId { get; set; }
    public List<int> SelectedOptionIdList { get; set; } = new();
}

/// <summary>Kết quả chấm một câu hỏi trong video. <paramref name="CanContinue"/> = false nghĩa là câu hỏi bắt
/// buộc đúng mà học viên trả lời sai và vẫn còn lượt — trình phát giữ nguyên hộp thoại cho làm lại.
/// <paramref name="IsForcedPass"/> = true nghĩa là đã hết lượt: video chạy tiếp nhưng câu này 0 điểm.
/// <paramref name="RemainingAttempt"/> = -1 khi câu hỏi không giới hạn số lần trả lời.</summary>
public record VideoAnswerResultDto(bool IsCorrect, bool CanContinue, bool IsForcedPass, string Message,
    string? Feedback, List<int> CorrectOptionIdList, int TryCount, int RemainingAttempt,
    decimal EarnedScore, decimal QuestionScore, decimal TotalEarnedScore, decimal TotalScore);
