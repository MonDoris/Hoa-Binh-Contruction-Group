// File: Application/Features/Translations/AiTranslateJob.cs

namespace HbELearning.Application.Features.Translations;

/// <summary>Một lượt "nhờ AI dịch hàng loạt" chạy nền (xem <see cref="IAiTranslateJobQueue"/>).
///
/// Chỉ sống trong bộ nhớ: máy chủ khởi động lại là mất lượt đang chạy, nhưng không mất câu nào — mỗi lô dịch
/// xong được ghi thẳng vào bảng dịch, và bấm "Dịch bằng AI" lần nữa thì chỉ những câu còn trống được gửi đi.
///
/// Dịch vụ nền ghi tiến độ, còn yêu cầu HTTP đọc ra cùng lúc — mọi thao tác đọc/ghi trạng thái đều đi qua khóa.</summary>
public sealed class AiTranslateJob
{
    public const string StatusQueued = "Queued";
    public const string StatusRunning = "Running";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";
    public const string StatusFailed = "Failed";

    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<AiTranslateJobTextDto> _textList = [];
    private readonly List<string> _failedKeyList = [];

    private string _status = StatusQueued;
    private int _doneCount;
    private int _translatedCount;
    private int _skippedCount;
    private int _currentBatch;
    private string? _message;
    private DateTime? _finishedAt;

    public AiTranslateJob(string sourceLang, string targetLang, IReadOnlyList<string> keyList, bool overwrite, int batchSize, int userId)
    {
        SourceLang = sourceLang;
        TargetLang = targetLang;
        KeyList = keyList;
        Overwrite = overwrite;
        BatchSize = Math.Max(1, batchSize);
        UserId = userId;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string SourceLang { get; }
    public string TargetLang { get; }
    public IReadOnlyList<string> KeyList { get; }
    public bool Overwrite { get; }
    public int BatchSize { get; }
    /// <summary>Người bấm dịch — ghi vào UpdatedById của các dòng AI dịch, như khi họ tự bấm từng lô.</summary>
    public int UserId { get; }
    public DateTime CreatedAt { get; } = DateTime.Now;

    public int BatchCount => (KeyList.Count + BatchSize - 1) / BatchSize;

    /// <summary>Bị hủy khi người dùng bấm Dừng — dịch vụ nền nối nó vào lượt gọi AI đang chạy để dừng ngay giữa lô.</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    public bool IsFinished
    {
        get { lock (_lock) return _status is StatusCompleted or StatusCancelled or StatusFailed; }
    }

    /// <summary>Các lô theo đúng thứ tự gửi đi.</summary>
    public IEnumerable<IReadOnlyList<string>> Batches() => KeyList.Chunk(BatchSize);

    public void MarkRunning()
    {
        lock (_lock) if (_status == StatusQueued) _status = StatusRunning;
    }

    public void BeginBatch(int batchNumber)
    {
        lock (_lock) _currentBatch = batchNumber;
    }

    /// <summary>Ghi kết quả một lô vừa dịch xong (đã lưu vào bảng dịch).</summary>
    public void ReportBatch(int batchKeyCount, AiTranslateResponse response)
    {
        lock (_lock)
        {
            _doneCount += batchKeyCount;
            _translatedCount += response.TranslatedCount;
            _skippedCount += response.SkippedCount;
            _failedKeyList.AddRange(response.FailedKeyList);
            _textList.AddRange(response.TextByKey.Select(x => new AiTranslateJobTextDto(x.Key, x.Value)));
        }
    }

    /// <summary>Người dùng bấm Dừng. Lượt đã xong thì không làm gì.</summary>
    public void RequestCancel()
    {
        lock (_lock) if (_status is StatusCompleted or StatusCancelled or StatusFailed) return;
        _cancellation.Cancel();
    }

    public void Finish(string status, string? message = null)
    {
        lock (_lock)
        {
            if (_status is StatusCompleted or StatusCancelled or StatusFailed) return;
            _status = status;
            _message = message;
            _finishedAt = DateTime.Now;
        }
    }

    /// <summary>Ảnh chụp tiến độ. <paramref name="fromText"/>: chỉ trả các câu đã dịch kể từ vị trí này — trang
    /// hỏi tiến độ vài giây một lần, gửi lại cả nghìn câu đã nhận rồi mỗi lần là phí.</summary>
    public AiTranslateJobDto Snapshot(int fromText)
    {
        lock (_lock)
        {
            var from = Math.Clamp(fromText, 0, _textList.Count);
            return new AiTranslateJobDto(
                Id, SourceLang, TargetLang, _status,
                KeyList.Count, _doneCount, _translatedCount, _skippedCount, _failedKeyList.Count,
                BatchSize, BatchCount, _currentBatch,
                _textList.Count, _textList.Skip(from).ToList(),
                _message, CreatedAt, _finishedAt);
        }
    }
}
