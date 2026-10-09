// File: Infrastructure/BackgroundJobs/ExternalCertificateVerifyWorker.cs
using HbELearning.Application.Features.ExternalCertificates;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Dịch vụ nền để AI đối chiếu những chứng nhận bên ngoài gửi lên mà chưa có kết quả quét sẵn (xem
/// <see cref="ExternalCertificateVerifier"/>). Chạy song song hai việc, cùng khuôn với
/// <see cref="CourseKnowledgeSyncWorker"/>:
///   1. LÀM NGAY: giấy vừa gửi được báo qua <see cref="IExternalCertificateVerifyQueue"/>.
///   2. QUÉT ĐỊNH KỲ: vét mọi giấy đang chờ — lưới an toàn khi máy chủ AI tắt lúc gửi, hoặc máy chủ web khởi
///      động lại làm mất hàng đợi trong bộ nhớ. Giấy gửi từ hồi còn Admin duyệt tay mà chưa ai duyệt cũng được
///      vét ở lượt đầu tiên.
///
/// Mỗi lúc chỉ đọc MỘT giấy: Ollama trên máy CPU xử lý tuần tự, gửi dồn nhiều lượt chỉ làm lượt nào cũng chậm và
/// chặn luôn lượt quét của người đang đứng chờ trước hộp thoại.</summary>
public class ExternalCertificateVerifyWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IExternalCertificateVerifyQueue _queue;
    private readonly ILogger<ExternalCertificateVerifyWorker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ExternalCertificateVerifyWorker(IServiceScopeFactory scopeFactory, IExternalCertificateVerifyQueue queue,
        ILogger<ExternalCertificateVerifyWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(RunQueueLoopAsync(stoppingToken), RunSweepLoopAsync(stoppingToken));

    private async Task RunQueueLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var idList = await _queue.DequeueAllAsync(stoppingToken);
                await VerifyListAsync(idList, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunSweepLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(SweepInterval);
            do
            {
                List<int> idList;
                using (var scope = _scopeFactory.CreateScope())
                    idList = await scope.ServiceProvider.GetRequiredService<ExternalCertificateVerifier>()
                        .GetPendingIdListAsync(stoppingToken);
                await VerifyListAsync(idList, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // máy chủ đang tắt
        }
    }

    private async Task VerifyListAsync(IReadOnlyCollection<int> idList, CancellationToken stoppingToken)
    {
        if (idList.Count == 0) return;

        await _gate.WaitAsync(stoppingToken);
        try
        {
            foreach (var id in idList)
            {
                // Nhường người đang ngồi chờ AI quét trong hộp thoại — việc nền chậm vài phút không ai thấy.
                while (ExternalCertificateVerifier.IsInteractiveScanActive)
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

                ExternalCertificateVerifyOutcome outcome;
                try
                {
                    // Mỗi giấy một scope (một DbContext) riêng: giấy trước lỗi không kéo theo giấy sau.
                    using var scope = _scopeFactory.CreateScope();
                    outcome = await scope.ServiceProvider.GetRequiredService<ExternalCertificateVerifier>()
                        .VerifyPendingAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Loi khi AI doi chieu chung nhan ben ngoai {Id}.", id);
                    continue;
                }

                // Máy chủ AI tắt: các giấy còn lại cũng sẽ y vậy — dừng, lượt quét sau làm tiếp.
                if (outcome == ExternalCertificateVerifyOutcome.AiUnavailable) break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
