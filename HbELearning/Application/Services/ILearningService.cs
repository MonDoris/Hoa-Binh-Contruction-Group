// File: Application/Services/ILearningService.cs

namespace HbELearning.Application.Services;

public interface ILearningService
{
    /// <summary>Dựng danh sách các bước trong lộ trình kèm trạng thái + khóa/mở của người học.</summary>
    Task<List<PathProgressDto>> BuildPathProgressAsync(int enrollmentId);

    /// <summary>Tính lại tiến độ, cập nhật trạng thái và tự cấp chứng nhận khi đủ điều kiện.</summary>
    Task RecalculateAsync(int enrollmentId);
}
