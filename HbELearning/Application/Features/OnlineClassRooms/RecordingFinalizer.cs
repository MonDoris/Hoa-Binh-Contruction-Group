// File: Application/Features/OnlineClassRooms/RecordingFinalizer.cs

namespace HbELearning.Application.Features.OnlineClassRooms;

/// <summary>
/// Chốt một Bản ghi: ghi thời lượng thật + lúc dừng vào database và vá trường Duration trong header của tệp WebM.
///
/// Có hai nơi cần chốt — trình duyệt của Chủ trì báo "xong" (OnlineClassRoomService.FinishRecording), và máy chủ tự
/// chốt khi Buổi học kết thúc hay Chủ trì mất kết nối mà trình duyệt chưa kịp báo (ClassRoomSessionService) — nên
/// việc chốt nằm ở một chỗ để hai đường không lệch nhau.
/// </summary>
public sealed class RecordingFinalizer
{
    private readonly IAppDbContext _db;
    private readonly IAppPaths _env;
    private readonly IMediaFileProcessor _mediaFileProcessor;
    private readonly TimeProvider _time;

    public RecordingFinalizer(IAppDbContext db, IAppPaths env, IMediaFileProcessor mediaFileProcessor, TimeProvider time)
    {
        _db = db;
        _env = env;
        _mediaFileProcessor = mediaFileProcessor;
        _time = time;
    }

    /// <summary>Chốt Bản ghi (đã được theo dõi bởi DbContext) rồi lưu. Thời lượng do bên gọi quyết định: trình duyệt
    /// báo đúng độ dài tệp nó đã ghi; máy chủ chỉ dùng đồng hồ của mình khi trình duyệt không còn để báo.</summary>
    public async Task FinalizeAsync(OnlineClassRoomRecording recording, double durationMs)
    {
        var fullPath = Path.Combine(_env.WebRootPath, recording.FileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        try { _mediaFileProcessor.PatchWebmDuration(fullPath, durationMs); }
        catch { /* tệp không đúng cấu trúc mong đợi -> bỏ qua, video vẫn phát được */ }

        recording.DurationMs = (long)Math.Round(durationMs);
        recording.StoppedAt = _time.GetLocalNow().DateTime;
        await _db.SaveChangesAsync();
    }
}
