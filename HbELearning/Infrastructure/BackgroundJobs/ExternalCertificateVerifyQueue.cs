// File: Infrastructure/BackgroundJobs/ExternalCertificateVerifyQueue.cs
using System.Threading.Channels;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Hiện thực <see cref="IExternalCertificateVerifyQueue"/> trong bộ nhớ — cùng cách với
/// <see cref="CourseKnowledgeSyncQueue"/>: một TẬP mã giấy, Channel chỉ đóng vai chuông báo có việc.</summary>
public sealed class ExternalCertificateVerifyQueue : IExternalCertificateVerifyQueue
{
    private readonly HashSet<int> _pendingIdSet = [];
    private readonly Lock _lock = new();
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(int certificateId)
    {
        if (certificateId <= 0) return;

        lock (_lock) _pendingIdSet.Add(certificateId);
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
