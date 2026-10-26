// File: Infrastructure/BackgroundJobs/CourseKnowledgeSyncWorker.cs
namespace HbELearning.Infrastructure.BackgroundJobs;

/// <summary>Dịch vụ nền để AI TỰ HỌC nội dung khóa học. Chạy song song hai việc:
///   1. HỌC NGAY: khóa học vừa xuất bản / vừa sửa bài học được báo qua <see cref="ICourseKnowledgeSyncQueue"/>
///      và được học trong vòng vài chục giây — HB Minerva trả lời được nội dung khóa học ngay sau khi xuất bản.
///   2. QUÉT ĐỊNH KỲ: cứ mỗi <see cref="IntervalHour"/> giờ quét lại toàn bộ, làm lưới an toàn cho những gì
///      hàng đợi bỏ lỡ (máy chủ AI đang tắt lúc xuất bản, máy chủ khởi động lại, sửa thẳng trong database).
///
/// Vì sao phải chạy nền thay vì nạp ngay lúc người soạn bấm Lưu bài học: sinh vector cho một bài học tốn của
/// Ollama hàng chục giây trên máy chạy CPU. Gắn vào nút Lưu là người soạn ngồi nhìn vòng xoay sau mỗi lần
/// sửa một dấu phẩy, và bài học chỉ lưu được khi máy chủ AI đang chạy — tức tính năng AI hỏng thì kéo theo
/// cả việc soạn bài hỏng. Ở đây thì ngược lại: máy chủ AI có tắt cả ngày cũng không ai biết, lượt quét sau
/// tự nạp bù phần còn thiếu.
///
/// Lượt quét gần như miễn phí khi không có gì đổi: mỗi đơn vị nạp mang một dấu vân tay của chính phần chữ
/// đã nạp, trùng vân tay là bỏ qua, không gọi Ollama lần nào (xem <see cref="CourseKnowledgeService"/>).</summary>
public class CourseKnowledgeSyncWorker : BackgroundService
{
    /// <summary>Khoảng cách giữa hai lượt quét. Nội dung khóa học đổi rất thưa — vài giờ một lần là đủ, mà
    /// quản trị viên vẫn bấm được "Học ngay" ở màn hình Tài liệu AI khi cần có hiệu lực tức thì.</summary>
    private const int IntervalHour = 6;

    /// <summary>Chờ trước lượt quét ĐẦU TIÊN sau khi máy chủ khởi động. Lúc mới bật, ứng dụng còn đang dựng
    /// schema, nạp dữ liệu gốc và phục vụ những người đăng nhập sớm nhất; đẩy thêm một loạt lệnh nặng cho
    /// Ollama vào đúng lúc đó chỉ làm mọi thứ chậm đi cùng lúc.</summary>
    private const int StartupDelayMinute = 3;

    /// <summary>Chờ thêm ngần này sau khi nhận việc từ hàng đợi rồi mới học: người soạn thường lưu liền nhiều
    /// bài, và lệnh duyệt còn đang ghi lịch sử/gửi thông báo — gom lại thì chỉ học một lượt, và chắc chắn
    /// dữ liệu đã ghi xong.</summary>
    private static readonly TimeSpan QueueDebounce = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICourseKnowledgeSyncQueue _queue;
    private readonly ILogger<CourseKnowledgeSyncWorker> _logger;

    public CourseKnowledgeSyncWorker(IServiceScopeFactory scopeFactory, ICourseKnowledgeSyncQueue queue, ILogger<CourseKnowledgeSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(RunQueueLoopAsync(stoppingToken), RunPeriodicLoopAsync(stoppingToken));

    /// <summary>Học ngay những khóa học vừa được báo thay đổi.</summary>
    private async Task RunQueueLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var courseIdList = await _queue.DequeueAllAsync(stoppingToken);

                // Khóa được báo trong lúc chờ/lúc đang học sẽ nằm lại hàng đợi và được lấy ở vòng sau.
                await Task.Delay(QueueDebounce, stoppingToken);

                foreach (var courseId in courseIdList)
                    await SyncOnceAsync(courseId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunPeriodicLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinute), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;   // máy chủ tắt ngay sau khi bật — không có gì để làm
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(IntervalHour));

        try
        {
            do
            {
                await SyncOnceAsync(null, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // máy chủ đang tắt
        }
    }

    /// <summary>Một lượt học: <paramref name="courseId"/> = null là quét toàn bộ.</summary>
    private async Task SyncOnceAsync(int? courseId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var knowledge = scope.ServiceProvider.GetRequiredService<ICourseKnowledgeService>();

            var result = await knowledge.SyncAsync(courseId: courseId, cancellationToken: stoppingToken);

            // Chỉ ồn ào khi thực sự có nạp gì đó. Lượt quét "không có gì đổi" là trường hợp thường gặp
            // nhất và nó lặp lại mỗi vài giờ, suốt đời máy chủ — ghi log mức Information cho nó thì nhật
            // ký đầy những dòng vô nghĩa và người đi dò lỗi thật phải lội qua.
            if (result.IndexedUnitCount > 0 || result.RemovedChunkCount > 0)
                _logger.LogInformation(
                    "AI da hoc them noi dung khoa hoc {CourseId}: {UnitCount} muc ({ChunkCount} doan), don {RemovedCount} doan cu.",
                    courseId?.ToString() ?? "(tat ca)", result.IndexedUnitCount, result.ChunkCount, result.RemovedChunkCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Máy chủ AI chưa chạy / chưa cài model. Đây là chuyện bình thường ở môi trường chưa dựng
            // Ollama, nên chỉ nhắc chứ không coi là lỗi hệ thống — lượt quét sau tự thử lại.
            _logger.LogWarning("Chua hoc duoc noi dung khoa hoc: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            // Một lượt hỏng không được làm chết vòng lặp — lượt sau thử lại.
            _logger.LogError(ex, "Dong bo kien thuc khoa hoc {CourseId} that bai.", courseId?.ToString() ?? "(tat ca)");
        }
    }
}
