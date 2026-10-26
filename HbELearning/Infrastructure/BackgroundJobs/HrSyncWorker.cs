// File: Infrastructure/BackgroundJobs/HrSyncWorker.cs
using HbELearning.Application.Common.Results;

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Tự đồng bộ nhân sự mỗi ngày một lần vào giờ cấu hình (HrSync:AutoSyncHour, 0–23 theo giờ máy chủ).
/// Mặc định -1 = TẮT: chưa có API Key thì không có gì để chạy, và có rồi thì nên để Admin xem trước và chạy tay
/// vài lần cho yên tâm về dữ liệu rồi mới bật tự động.
///
/// Lớp này chỉ lo hẹn giờ và mở phạm vi DI; đồng bộ cái gì, theo quy tắc nào xem <see cref="IHrSyncService"/>.</summary>
public class HrSyncWorker : BackgroundService
{
    private const int CheckMinutes = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HrSyncWorker> _logger;
    private DateOnly _lastRunDate;

    public HrSyncWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<HrSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(CheckMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (!int.TryParse(_config["HrSync:AutoSyncHour"], out var hour) || hour is < 0 or > 23) continue;

                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now);
                if (now.Hour != hour || _lastRunDate == today) continue;

                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IHrSystemClient>();
                if (!client.IsConfigured) continue;

                // Đánh dấu trước: một lượt lỗi không được lặp lại mỗi 10 phút suốt cả tiếng đó.
                _lastRunDate = today;
                var result = await scope.ServiceProvider.GetRequiredService<IHrSyncService>().Run(previewOnly: false, stoppingToken);
                if (result.Kind != ResultKind.Success)
                    _logger.LogWarning("Dong bo nhan su tu dong khong thanh cong: {Kind}", result.Kind);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dong bo nhan su tu dong that bai");
            }
        }
    }
}
