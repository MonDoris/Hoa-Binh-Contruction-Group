// File: Application/Features/Feedbacks/CourseRating.cs

namespace HbELearning.Application.Features.Feedbacks;

/// <summary>Cách làm tròn điểm sao hiển thị trên thẻ khóa học — một chỗ duy nhất để mọi danh sách khóa cùng ra
/// một con số (1 chữ số thập phân). Khóa chưa có đánh giá nào thì điểm là null, không phải 0: số 0 sao là một
/// đánh giá không tồn tại (thấp nhất là 1 sao), hiện ra sẽ thành "khóa này bị chấm tệ".</summary>
internal static class CourseRating
{
    public static decimal? Round(double? average)
        => average.HasValue ? Math.Round((decimal)average.Value, 1) : null;
}
