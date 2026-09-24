// File: Application/Features/OnlineClassRooms/OnlineClassRoomService.cs
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace HbELearning.Application.Features.OnlineClassRooms;

public class OnlineClassRoomService : IOnlineClassRoomService
{
    private readonly IAppDbContext _db;
    private readonly IMediaFileProcessor _mediaFileProcessor;
    private readonly IConfiguration _config;
    private readonly IFileService _fileService;
    private readonly IAppPaths _env;
    private readonly ICurrentUser _currentUser;

    public OnlineClassRoomService(IAppDbContext db, IConfiguration config, IFileService fileService, IAppPaths env, ICurrentUser currentUser, IMediaFileProcessor mediaFileProcessor)
    {
        _mediaFileProcessor = mediaFileProcessor;
        _db = db;
        _config = config;
        _fileService = fileService;
        _env = env;
        _currentUser = currentUser;
    }

    public async Task<Result> GetList(OnlineClassRoomStatus? status)
    {
        var query = _db.OnlineClassRoom.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status);

        var list = await query
            .OrderByDescending(x => x.ScheduledStart)
            .Select(x => new OnlineClassRoomListDto(
                x.Id, x.Title,
                x.HostUserId, x.HostUser.FullName,
                x.ScheduledStart, x.ScheduledEnd,
                x.Status.ToString(), x.ActualStartAt, x.ActualEndAt,
                (_currentUser.IsAdmin || x.HostUserId == _currentUser.UserId) ? x.JoinCode : null))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetById(int id)
    {
        var room = await _db.OnlineClassRoom.AsNoTracking()
            .Include(x => x.HostUser)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));

        var isPrivileged = _currentUser.IsAdmin || room.HostUserId == _currentUser.UserId;
        return Result.Success(new OnlineClassRoomListDto(
            room.Id, room.Title,
            room.HostUserId, room.HostUser.FullName,
            room.ScheduledStart, room.ScheduledEnd,
            room.Status.ToString(), room.ActualStartAt, room.ActualEndAt,
            isPrivileged ? room.JoinCode : null));
    }

    public async Task<Result> ResolveByCode(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        var room = await _db.OnlineClassRoom.AsNoTracking()
            .Where(x => x.JoinCode == normalizedCode
                     && (x.Status == OnlineClassRoomStatus.Scheduled || x.Status == OnlineClassRoomStatus.Live))
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync();

        return room is null
            ? Result.NotFound(new MessageResponse("Mã phòng không đúng hoặc buổi học không còn hoạt động."))
            : Result.Success(new MessageResponse("OK", room.Id));
    }

    public async Task<Result> GetEvents(int id)
    {
        var room = await _db.OnlineClassRoom.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));

        var list = await _db.OnlineClassRoomEvent.AsNoTracking()
            .Where(x => x.OnlineClassRoomId == id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new OnlineClassRoomEventDto(x.Id, x.EventType.ToString(), x.UserId, x.UserName, x.Message, x.CreatedAt))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> GetRecordings(int id)
    {
        var room = await _db.OnlineClassRoom.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));

        var list = await _db.OnlineClassRoomRecording.AsNoTracking()
            .Where(x => x.OnlineClassRoomId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new OnlineClassRoomRecordingDto(x.Id, x.FileUrl, x.FileSizeBytes, x.RecordedByUserId, x.RecordedByUser.FullName, x.CreatedAt))
            .ToListAsync();

        return Result.Success(list);
    }

    public async Task<Result> StartRecording(int id)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));

        var storedName = $"{Guid.NewGuid():N}.webm";
        var relativeFolder = $"classroom-recording/{DateTime.Now:yyyyMM}";
        var folder = Path.Combine(_env.WebRootPath, "uploads", relativeFolder);
        Directory.CreateDirectory(folder);
        await using (System.IO.File.Create(Path.Combine(folder, storedName))) { }

        var recording = new OnlineClassRoomRecording
        {
            OnlineClassRoomId = id,
            FileUrl = $"/uploads/{relativeFolder}/{storedName}",
            FileSizeBytes = 0,
            RecordedByUserId = _currentUser.UserId
        };
        _db.OnlineClassRoomRecording.Add(recording);
        await _db.SaveChangesAsync();

        var recordedByName = _currentUser.FullName ?? "Người dùng";
        return Result.Success(new OnlineClassRoomRecordingDto(recording.Id, recording.FileUrl, recording.FileSizeBytes, _currentUser.UserId, recordedByName, recording.CreatedAt));
    }

    public async Task<Result> UploadRecordingChunk(int id, int recordingId, FileUpload file)
    {
        var recording = await _db.OnlineClassRoomRecording.FirstOrDefaultAsync(x => x.Id == recordingId && x.OnlineClassRoomId == id);
        if (recording is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi."));
        if (recording.RecordedByUserId != _currentUser.UserId && !_currentUser.IsAdmin) return Result.Forbidden();
        if (file is null || file.Length == 0) return Result.Success(new MessageResponse("OK"));

        var fullPath = Path.Combine(_env.WebRootPath, recording.FileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        await using (var destStream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.None))
            await file.CopyToAsync(destStream);

        recording.FileSizeBytes += file.Length;
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("OK"));
    }

    public async Task<Result> FinishRecording(int id, int recordingId, FinishRecordingRequest request)
    {
        var recording = await _db.OnlineClassRoomRecording.FirstOrDefaultAsync(x => x.Id == recordingId && x.OnlineClassRoomId == id);
        if (recording is null) return Result.NotFound(new MessageResponse("Không tìm thấy bản ghi."));
        if (recording.RecordedByUserId != _currentUser.UserId && !_currentUser.IsAdmin) return Result.Forbidden();

        var fullPath = Path.Combine(_env.WebRootPath, recording.FileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        try { _mediaFileProcessor.PatchWebmDuration(fullPath, request.DurationMs); }
        catch { /* file không đúng cấu trúc mong đợi -> bỏ qua, video vẫn phát được */ }

        return Result.Success(new MessageResponse("OK"));
    }

    public Result GetIceServers()
    {
        var host = _config["Turn:Host"];
        var secret = _config["Turn:Secret"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(secret))
            return Result.Success(new List<IceServerDto>());

        var ttlSeconds = int.TryParse(_config["Turn:CredentialTtlSeconds"], out var t) ? t : 3600;
        var username = $"{DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds()}:{_currentUser.UserId}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        return Result.Success(new List<IceServerDto>
        {
            new([$"stun:{host}:3478"], null, null),
            new([$"turn:{host}:3478?transport=udp", $"turn:{host}:3478?transport=tcp"], username, credential),
        });
    }

    public async Task<Result> Create(OnlineClassRoomRequest request)
    {
        var room = new OnlineClassRoom
        {
            Title = request.Title.Trim(),
            ScheduledStart = request.ScheduledStart,
            ScheduledEnd = request.ScheduledEnd,
            HostUserId = _currentUser.UserId,
            Status = OnlineClassRoomStatus.Scheduled,
            JoinCode = await GenerateUniqueJoinCodeAsync()
        };

        _db.OnlineClassRoom.Add(room);
        await _db.SaveChangesAsync();

        return Result.Created(nameof(GetById), new { id = room.Id }, new MessageResponse("Đã tạo phòng học online.", room.Id));
    }

    public async Task<Result> Update(int id, OnlineClassRoomRequest request)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));
        if (!_currentUser.IsAdmin && room.HostUserId != _currentUser.UserId) return Result.Forbidden();
        if (room.Status is OnlineClassRoomStatus.Live or OnlineClassRoomStatus.Ended)
            return Result.Invalid(new MessageResponse("Không thể sửa phòng học đang diễn ra hoặc đã kết thúc."));

        room.Title = request.Title.Trim();
        room.ScheduledStart = request.ScheduledStart;
        room.ScheduledEnd = request.ScheduledEnd;

        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Cập nhật thành công."));
    }

    public async Task<Result> Cancel(int id)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));
        if (!_currentUser.IsAdmin && room.HostUserId != _currentUser.UserId) return Result.Forbidden();
        if (!room.CanBeCancelled)
            return Result.Invalid(new MessageResponse("Chỉ có thể hủy phòng học chưa diễn ra."));

        room.Cancel();
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã hủy phòng học."));
    }

    public async Task<Result> Delete(int id)
    {
        var room = await _db.OnlineClassRoom.FirstOrDefaultAsync(x => x.Id == id);
        if (room is null) return Result.NotFound(new MessageResponse("Không tìm thấy phòng học."));
        if (room.Status == OnlineClassRoomStatus.Live)
            return Result.Invalid(new MessageResponse("Không thể xóa phòng học đang diễn ra."));

        // Xóa file video vật lý trước — cascade delete chỉ dọn bản ghi OnlineClassRoomRecording trong DB, không tự xóa file trên đĩa.
        var fileUrlList = await _db.OnlineClassRoomRecording.Where(x => x.OnlineClassRoomId == id).Select(x => x.FileUrl).ToListAsync();
        foreach (var url in fileUrlList) _fileService.Delete(url);

        _db.OnlineClassRoom.Remove(room);
        await _db.SaveChangesAsync();
        return Result.Success(new MessageResponse("Đã xóa phòng học."));
    }

    /// <summary>Sinh mã tham gia 6 chữ số — dễ đọc/gõ tay qua điện thoại hơn mã lẫn chữ cái. Thử lại nếu
    /// trùng mã phòng khác (xác suất cực thấp nhưng vẫn kiểm tra cho chắc).</summary>
    private async Task<string> GenerateUniqueJoinCodeAsync()
    {
        const string chars = "0123456789";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(RandomNumberGenerator.GetBytes(6).Select(b => chars[b % chars.Length]).ToArray());
            if (!await _db.OnlineClassRoom.AnyAsync(x => x.JoinCode == code)) return code;
        }
        throw new InvalidOperationException("Không sinh được mã phòng duy nhất, vui lòng thử lại.");
    }
}
