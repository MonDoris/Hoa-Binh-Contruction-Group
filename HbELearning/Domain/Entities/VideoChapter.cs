// File: Domain/Entities/VideoChapter.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>Một đoạn nội dung của video bài học: từ giây A tới giây B là chương/mục nào, thuộc chủ đề nào.
/// Trình phát dựa vào đây để báo cho học viên biết đoạn đang xem nói về cái gì — xem
/// wwwroot/js/video-learning.js.
///
/// Vì sao lưu cả StartSecond lẫn EndSecond thay vì chỉ lưu mốc bắt đầu rồi suy đoạn kết thúc từ chương kế
/// tiếp: video thường có đoạn dẫn nhập, đoạn nghỉ chuyển cảnh hay lời chào cuối không thuộc chương nào cả.
/// Suy từ chương kế tiếp thì những đoạn đó bị gán bừa vào chương liền trước, và học viên đang xem lời chào
/// cuối vẫn thấy hiện tên chương cuối cùng. Có mốc kết thúc riêng thì khoảng trống được phép tồn tại và
/// trình phát chỉ việc không hiện gì.</summary>
public class VideoChapter
{
    public int Id { get; set; }

    /// <summary>Bài học chứa video — bài học phải có ContentType = Video.</summary>
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    /// <summary>Giây bắt đầu đoạn (tính từ đầu video).</summary>
    public int StartSecond { get; set; }

    /// <summary>Giây kết thúc đoạn. Luôn lớn hơn <see cref="StartSecond"/>; các đoạn trong cùng một bài học
    /// không được chồng lấn nhau (kiểm ở VideoLearningService — CSDL không diễn tả được ràng buộc này).</summary>
    public int EndSecond { get; set; }

    /// <summary>Tên chương/mục của đoạn này, VD "Quy trình nghiệm thu cốp pha".</summary>
    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Chủ đề bao trùm nhiều chương, VD "Chủ đề 1 — An toàn lao động". Để trống khi video không
    /// chia theo chủ đề — khi đó trình phát chỉ hiện tên chương.</summary>
    [MaxLength(250)]
    public string? Topic { get; set; }

    /// <summary>Thứ tự hiển thị. Bình thường sắp theo StartSecond; giữ cột này để người soạn bài đổi thứ tự
    /// trong danh sách quản trị mà không phải sửa mốc thời gian.</summary>
    public int OrderIndex { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
