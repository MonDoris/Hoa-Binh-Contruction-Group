// File: Infrastructure/BackgroundJobs/AiTranslateJobWorker.cs
namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Dịch vụ nền chạy các lượt "dịch bằng AI" hàng loạt (xem <see cref="IAiTranslateJobQueue"/>): cắt danh
/// sách khóa thành lô, gửi từng lô cho AI, mỗi lô xong là ghi ngay vào bảng dịch.
///
/// Lô lỗi (Ollama bận, vừa khởi động lại...) được thử lại MỘT lần sau <see cref="RetryDelay"/>; hỏng tiếp thì dừng
/// cả lượt — lỗi kiểu này gần như luôn là máy chủ AI đang tắt, chạy tiếp chỉ để hỏng y hệt ở mọi lô sau.</summary>
public class AiTranslateJobWorker : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiTranslateJobQueue _queue;
    private readonly ILogger<AiTranslateJobWorker> _logger;

    public AiTranslateJobWorker(IServiceScopeFactory scopeFactory, IAiTranslateJobQueue queue, ILogger<AiTranslateJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AiTranslateJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunJobAsync(job, stoppingToken);
        }
    }

    private async Task RunJobAsync(AiTranslateJob job, CancellationToken stoppingToken)
    {
        // Một token cho cả hai đường dừng: người dùng bấm Dừng, hoặc máy chủ đang tắt.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.CancellationToken);
        var token = linked.Token;

        job.MarkRunning();
        var batchNumber = 0;

        try
        {
            foreach (var batch in job.Batches())
            {
                token.ThrowIfCancellationRequested();
                job.BeginBatch(++batchNumber);

                var response = await TranslateBatchWithRetryAsync(job, batch, batchNumber, token);
                if (response is null) return;   // đã ghi lý do vào job

                job.ReportBatch(batch.Count, response);
            }

            job.Finish(AiTranslateJob.StatusCompleted);
            var done = job.Snapshot(int.MaxValue);
            _logger.LogInformation("Dich nen bang AI xong {Source} -> {Target}: {Translated}/{Total} cau, {Failed} cau AI khong tra ve.",
                job.SourceLang, job.TargetLang, done.TranslatedCount, done.TotalCount, done.FailedCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.Finish(AiTranslateJob.StatusCancelled, stoppingToken.IsCancellationRequested
                ? "Máy chủ đang khởi động lại nên lượt dịch dừng giữa chừng. Bấm dịch lại để dịch nốt phần còn trống."
                : "Đã dừng theo yêu cầu.");
            _logger.LogInformation("Dich nen bang AI {Source} -> {Target} da dung o lo {Batch}/{BatchCount}.",
                job.SourceLang, job.TargetLang, batchNumber, job.BatchCount);
        }
        catch (Exception ex)
        {
            job.Finish(AiTranslateJob.StatusFailed, "Lượt dịch gặp lỗi ngoài dự kiến và đã dừng. Các câu dịch xong trước đó vẫn được giữ.");
            _logger.LogError(ex, "Dich nen bang AI {Source} -> {Target} loi o lo {Batch}.", job.SourceLang, job.TargetLang, batchNumber);
        }
    }

    /// <summary>Dịch một lô, thử lại một lần nếu hỏng. Trả null (và đánh dấu job Failed) nếu cả hai lần đều hỏng.</summary>
    private async Task<AiTranslateResponse?> TranslateBatchWithRetryAsync(
        AiTranslateJob job, IReadOnlyList<string> batch, int batchNumber, CancellationToken token)
    {
        var request = new AiTranslateRequest(job.SourceLang, job.TargetLang, [.. batch], job.Overwrite, Preview: false);

        for (var attempt = 1; ; attempt++)
        {
            // Mỗi lô một scope riêng: DbContext sống ngắn, không tích dần cả nghìn dòng đang theo dõi.
            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ITranslationService>();
                var result = await service.AiTranslateAs(request, job.UserId, token);

                if (result.IsSuccess && result.Value is AiTranslateResponse response) return response;

                var message = (result.Value as MessageResponse)?.Message ?? "Máy chủ AI không phản hồi.";
                if (attempt >= 2)
                {
                    job.Finish(AiTranslateJob.StatusFailed, $"Dừng ở lô {batchNumber}/{job.BatchCount}: {message}");
                    _logger.LogWarning("Dich nen bang AI {Source} -> {Target} dung o lo {Batch}/{BatchCount}: {Message}",
                        job.SourceLang, job.TargetLang, batchNumber, job.BatchCount, message);
                    return null;
                }
            }

            await Task.Delay(RetryDelay, token);
        }
    }
}
