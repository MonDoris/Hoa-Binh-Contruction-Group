// File: Domain/Entities/PathItem.cs
using System.ComponentModel.DataAnnotations;

namespace HbELearning.Domain.Entities;

/// <summary>
/// Một mục trong cấu trúc khóa học — chính là một &lt;item&gt; của SCORM Organization (LearningPath là thẻ
/// &lt;organization&gt;). Mục loại <see cref="PathItemType.Module"/> là chương (cluster) gom các mục con qua
/// <see cref="ParentItemId"/>; các loại còn lại là hoạt động lá trỏ tới một SCO: bài học, bài kiểm tra, mô phỏng 3D
/// hoặc một gói SCORM nhập từ ngoài.
///
/// Các trường thiết lập ở đây là phần SEQUENCING của mục, được dịch sang imsss:sequencing khi dựng cây hoạt động
/// (xem Application/Features/Scorm/CoursePackage/CourseOrganizationBuilder): IsRequired → rollup, StartDate/EndDate →
/// limitConditions beginTimeLimit / endTimeLimit, AttemptLimit → limitConditions attemptLimit, CompletionThreshold →
/// adlcp:completionThreshold, IsVisible → isvisible.
/// </summary>
public class PathItem
{
    public int Id { get; set; }

    public int LearningPathId { get; set; }
    public LearningPath LearningPath { get; set; } = null!;

    public PathItemType ItemType { get; set; }

    /// <summary>Chương chứa mục này; null = nằm ngay dưới gốc tổ chức.</summary>
    public int? ParentItemId { get; set; }
    public PathItem? ParentItem { get; set; }

    /// <summary>Tên chương (bắt buộc với Module). Với mục lá thì bỏ trống — tên lấy theo tài nguyên.</summary>
    [MaxLength(250)]
    public string? Title { get; set; }

    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public int? QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public int? ScenarioSceneId { get; set; }
    public ScenarioScene? ScenarioScene { get; set; }

    /// <summary>Bước học một gói SCORM nhập từ ngoài — null = bước này không phải gói ngoài.</summary>
    public int? ScormPackageId { get; set; }
    public ScormPackage? ScormPackage { get; set; }

    /// <summary>Thứ tự trong cả cây (duy nhất trong một lộ trình). Thứ tự giữa các mục cùng chương là thứ tự của số này.</summary>
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;

    /// <summary>isvisible — false thì mục vẫn được học theo luồng nhưng không hiện trong mục lục.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Số lượt thử tối đa trên mục (limitConditions attemptLimit); null = không giới hạn.</summary>
    public int? AttemptLimit { get; set; }

    /// <summary>Ngưỡng hoàn thành theo tiến độ 0..1 (adlcp:completionThreshold minProgressMeasure, completedByMeasure);
    /// null = hoàn thành do nội dung tự báo.</summary>
    public decimal? CompletionThreshold { get; set; }

    /// <summary>Null = không giới hạn theo thời gian, chỉ xét tuần tự như cũ.</summary>
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
