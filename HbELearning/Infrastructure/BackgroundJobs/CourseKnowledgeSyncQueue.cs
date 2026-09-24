// File: Infrastructure/BackgroundJobs/CourseKnowledgeSyncQueue.cs
using System.Threading.Channels;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Hiện thực <see cref="ICourseKnowledgeSyncQueue"/> trong bộ nhớ. Máy chủ tắt thì mất phần đang chờ,
/// nhưng không sao: lượt quét toàn bộ lúc khởi động (xem <see cref="CourseKnowledgeSyncWorker"/>) nạp bù.
///
/// Giữ một TẬP mã khóa học chứ không phải hàng đợi thô: người soạn lưu một bài học năm lần liền thì vẫn chỉ
/// cần học lại khóa đó một lần. Channel chỉ đóng vai "chuông báo" có việc.</summary>
public sealed class CourseKnowledgeSyncQueue : ICourseKnowledgeSyncQueue
{
    private readonly HashSet<int> _pendingIdSet = [];
    private readonly Lock _lock = new();
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(int courseId)
    {
        if (courseId <= 0) return;

        lock (_lock) _pendingIdSet.Add(courseId);
        _signal.Writer.TryWrite(true);
    }

    public async Task<IReadOnlyCollection<int>> DequeueAllAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.Reader.ReadAsync(cancellationToken);

            lock (_lock)
            {
                if (_pendingIdSet.Count == 0) continue;

                var idList = _pendingIdSet.ToList();
                _pendingIdSet.Clear();
                return idList;
            }
        }
    }
}
