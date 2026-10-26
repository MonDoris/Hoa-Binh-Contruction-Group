// File: Infrastructure/BackgroundJobs/OllamaWarmUpWorker.cs
using HbELearning.Infrastructure.Ai;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Nạp sẵn model hỏi đáp + model nhúng vào RAM ngay khi máy chủ khởi động, và (tùy chọn) nhắc lại định kỳ.
///
/// Nạp model Ollama đo được hơn 12 giây trên máy CPU. Không có worker này thì người đầu tiên hỏi HB Minerva sau
/// mỗi lần khởi động lại (hoặc sau khi Ollama nhả model vì quá keep_alive) gánh trọn ngần ấy giây.
/// "Ollama:WarmUpIntervalMinutes" &gt; 0 thì cứ chừng ấy phút gọi lại một lần để model không bao giờ bị nhả
/// (đổi lại chiếm RAM thường trực bằng kích thước model); mặc định 0 = chỉ nạp một lần lúc khởi động.</summary>
public sealed class OllamaWarmUpWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OllamaWarmUpWorker> _logger;
    private readonly TimeSpan _interval;

    public OllamaWarmUpWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<OllamaWarmUpWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Max(0, config.GetValue<int?>("Ollama:WarmUpIntervalMinutes") ?? 0));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Chờ web lên hẳn trước: làm nóng không được giành CPU của lúc khởi động.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        do
        {
            await WarmUpOnceAsync(stoppingToken);
            if (_interval == TimeSpan.Zero) return;

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task WarmUpOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            // Nhúng trước (model bé, vài trăm ms) rồi mới tới model hỏi đáp (hàng chục giây).
            if (scope.ServiceProvider.GetRequiredService<IEmbeddingService>() is OllamaEmbeddingService embedding)
                await embedding.WarmUpAsync(cancellationToken);

            if (scope.ServiceProvider.GetRequiredService<IAiChatService>() is OllamaChatService chat)
                await chat.WarmUpAsync(cancellationToken);

            _logger.LogInformation("Da nap san model AI vao bo nho.");
        }
        catch (OperationCanceledException)
        {
            // Tắt máy chủ giữa chừng — bình thường.
        }
        catch (Exception ex)
        {
            // Ollama chưa chạy là chuyện thường lúc khởi động; không phải lỗi của ứng dụng.
            _logger.LogWarning(ex, "Khong nap san duoc model AI (Ollama chua san sang?). Cau hoi dau tien se cham hon.");
        }
    }
}
