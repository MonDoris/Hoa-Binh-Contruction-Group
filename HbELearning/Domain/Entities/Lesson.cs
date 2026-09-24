// File: Domain/Entities/Lesson.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

public class Lesson
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    [Required, MaxLength(250)]
    public string Title { get; set; } = null!;

    /// <summary>Nội dung dạng HTML/text — ánh xạ NCLOB trên Oracle.</summary>
    public string? Content { get; set; }

    public LessonContentType ContentType { get; set; } = LessonContentType.Text;

    /// <summary>Đường dẫn tài liệu / video / hình ảnh trong wwwroot/uploads.</summary>
    [MaxLength(500)]
    public string? ResourceUrl { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Chỉ dùng cho bài học dạng Video: học viên được tua tới đâu (xem <see cref="Helpers.VideoSeekPolicy"/>).
    /// Đặt ở bài học chứ không ở từng câu hỏi vì đây là quyết định về cách xem CẢ video — mỗi câu hỏi vẫn có
    /// cờ BlockSeek riêng cho đúng mốc của nó.</summary>
    public VideoSeekPolicy VideoSeekPolicy { get; set; } = VideoSeekPolicy.Free;

    public int OrderIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<LessonProgress> LessonProgressList { get; set; } = new List<LessonProgress>();
    public ICollection<PathItem> PathItemList { get; set; } = new List<PathItem>();
    public ICollection<Quiz> QuizList { get; set; } = new List<Quiz>();
    /// <summary>Mô phỏng 3D gắn theo bài học này — mirror QuizList, để trang quản lý bài học hiện được
    /// bài học nào đã có mô phỏng đi kèm.</summary>
    public ICollection<ScenarioScene> ScenarioSceneList { get; set; } = new List<ScenarioScene>();
}
