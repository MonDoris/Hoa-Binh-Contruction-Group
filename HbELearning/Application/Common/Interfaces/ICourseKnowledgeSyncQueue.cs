// File: Application/Common/Interfaces/ICourseKnowledgeSyncQueue.cs

namespace HbELearning.Application.Common.Interfaces;

/// <summary>
/// Hàng đợi "khóa học này vừa đổi, Pico B cần học lại".
///
/// Có hàng đợi này để khóa học vừa XUẤT BẢN (hoặc bài học của nó vừa sửa) được học ngay trong vài chục giây,
/// thay vì chờ lượt quét định kỳ vài giờ một lần. Việc học vẫn chạy nền (sinh vector tốn hàng chục giây), nên
/// người bấm Duyệt / Lưu không phải chờ, và máy chủ AI tắt cũng không làm hỏng thao tác của họ.
/// </summary>
public interface ICourseKnowledgeSyncQueue
{
    /// <summary>Báo khóa học cần học lại. Gọi nhiều lần cho cùng một khóa là vô hại — hàng đợi tự gộp.</summary>
    void Enqueue(int courseId);

    /// <summary>Chờ tới khi có khóa học cần học, rồi trả về (và xóa khỏi hàng đợi) TẤT CẢ mã khóa đang chờ.</summary>
    Task<IReadOnlyCollection<int>> DequeueAllAsync(CancellationToken cancellationToken);
}
