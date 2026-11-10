// File: Infrastructure/BackgroundJobs/AiTranslateJobQueue.cs
using System.Threading.Channels;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Hiện thực <see cref="IAiTranslateJobQueue"/> trong bộ nhớ. Máy chủ tắt thì mất lượt đang chạy, nhưng các
/// lô đã dịch đều đã nằm trong bảng dịch — bấm dịch lại chỉ gửi đi những câu còn trống.</summary>
public sealed class AiTranslateJobQueue : IAiTranslateJobQueue
{
    /// <summary>Đo trên máy chạy CPU với qwen2.5:7b (~3 token/giây): 10 câu ≈ 2–3 phút một lô. Lô to hơn thì
    /// thanh tiến độ đứng lâu, nút Dừng chờ lâu mới ăn, và lỗi giữa lô mất nhiều câu hơn.</summary>
    private const int DefaultBatchSize = 10;

    private readonly Channel<AiTranslateJob> _channel = Channel.CreateUnbounded<AiTranslateJob>();
    private readonly Lock _lock = new();
    private AiTranslateJob? _latest;

    public AiTranslateJobQueue(IConfiguration config)
    {
        BatchSize = Math.Max(1, config.GetValue<int?>("Translation:AiBatchSize") ?? DefaultBatchSize);
    }

    public int BatchSize { get; }

    public AiTranslateJob? Latest
    {
        get { lock (_lock) return _latest; }
    }

    public bool TryEnqueue(AiTranslateJob job)
    {
        lock (_lock)
        {
            if (_latest is { IsFinished: false }) return false;
            _latest = job;
        }
        _channel.Writer.TryWrite(job);
        return true;
    }

    public ValueTask<AiTranslateJob> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);

    /// <summary>Chỉ giữ lượt gần nhất — đủ cho việc "mở lại trang thấy tiếp tiến độ", và không phình bộ nhớ theo thời gian.</summary>
    public AiTranslateJob? Get(string id)
    {
        lock (_lock) return _latest?.Id == id ? _latest : null;
    }
}
