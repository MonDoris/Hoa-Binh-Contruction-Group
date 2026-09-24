// File: Infrastructure/BackgroundJobs/StudyReminderWorker.cs

namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Vòng lặp nền bắn các lời nhắc học tập người dùng tự đặt khi tới giờ.
///
/// Đây là điểm khác căn bản so với nhắc nhở theo hạn hoàn thành: những nhắc nhở kia chỉ sinh ra lúc người học mở
/// ứng dụng (NotificationDispatcher.SyncReminderAsync), nên không thể "báo đúng giờ". Vòng lặp này chạy cùng máy chủ,
/// quét mỗi TickSeconds giây, nên tới phút đã hẹn là thông báo có sẵn trong chuông — chuông ở giao diện tự
/// làm mới mỗi phút và bật thêm thông báo của trình duyệt (xem wwwroot/js/app.js).
///
/// Lớp này chỉ lo phần hạ tầng: hẹn giờ, mở phạm vi DI cho mỗi lượt, và giữ vòng lặp sống qua lỗi. Bắn lời nhắc nào,
/// báo gì, lần sau là khi nào — xem <see cref="IStudyReminderDispatcher"/>.</summary>
public class StudyReminderWorker : BackgroundService
{
    /// <summary>Chu kỳ quét. Người dùng đặt giờ tới phút nên quét dày hơn một phút để sai số không dồn.</summary>
    private const int TickSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StudyReminderWorker> _logger;

    public StudyReminderWorker(IServiceScopeFactory scopeFactory, ILogger<StudyReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IStudyReminderDispatcher>().FireDueRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Một lượt quét hỏng (mất kết nối db chẳng hạn) không được làm chết vòng lặp — lượt sau thử lại.
                _logger.LogError(ex, "Quet loi nhac hoc tap that bai");
            }
        }
    }
}
