using HbELearning.Application.DTOs;
using HbELearning.Domain.Entities;
using HbELearning.Domain.Enums;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>
/// Bấm một dòng Diễn biến thì video nhảy tới đúng lúc xảy ra sự kiện. Vị trí video = thời điểm sự kiện trừ lúc bắt đầu
/// Bản ghi, rồi trừ các đoạn tạm dừng đã kết thúc trước sự kiện. Dịch vụ tính sẵn vị trí này cho từng dòng Diễn biến.
/// </summary>
public class SeekPositionTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Một Buổi học đã kết thúc (T0 → T0+10 phút) với người xem là Học viên đã vào buổi.</summary>
    private static (ClassRoomWorld World, int RoomId) Room()
    {
        var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi nhảy", joined: new[] { world.Student }, duration: TimeSpan.FromMinutes(10), startedAt: T0);
        world.ActAs(world.Student);
        return (world, roomId);
    }

    private static int AddRecording(ClassRoomWorld world, int roomId, double startSeconds, double? stopSeconds = null,
        bool withStartedEvent = true)
    {
        using var db = world.NewDb();
        var recording = new OnlineClassRoomRecording
        {
            OnlineClassRoomId = roomId, FileUrl = $"/uploads/x/{Guid.NewGuid():N}.webm", FileSizeBytes = 1000,
            RecordedByUserId = world.Host.Id, CreatedAt = At(startSeconds - 0.4), // bản ghi được tạo ngay trước khi đồng hồ chạy
            StoppedAt = stopSeconds is { } s ? At(s) : null,
        };
        db.OnlineClassRoomRecording.Add(recording);
        db.SaveChanges();
        if (withStartedEvent) Mark(world, roomId, recording.Id, OnlineClassRoomEventType.RecordingStarted, startSeconds);
        if (stopSeconds is { } stop) Mark(world, roomId, recording.Id, OnlineClassRoomEventType.RecordingStopped, stop);
        return recording.Id;
    }

    private static void Mark(ClassRoomWorld world, int roomId, int recordingId, OnlineClassRoomEventType type, double seconds)
    {
        using var db = world.NewDb();
        db.OnlineClassRoomEvent.Add(new OnlineClassRoomEvent
        {
            OnlineClassRoomId = roomId, EventType = type, RecordingId = recordingId,
            UserId = world.Host.Id, UserName = world.Host.FullName, CreatedAt = At(seconds),
        });
        db.SaveChanges();
    }

    private static int Chat(ClassRoomWorld world, int roomId, double seconds, string text = "chat")
    {
        using var db = world.NewDb();
        var chat = new OnlineClassRoomEvent
        {
            OnlineClassRoomId = roomId, EventType = OnlineClassRoomEventType.ChatMessage, Message = text,
            UserId = world.Student.Id, UserName = world.Student.FullName, CreatedAt = At(seconds),
        };
        db.OnlineClassRoomEvent.Add(chat);
        db.SaveChanges();
        return chat.Id;
    }

    private static async Task<OnlineClassRoomEventDto> EventOf(ClassRoomWorld world, int roomId, int eventId)
    {
        var review = (OnlineClassRoomReviewDto)(await world.Rooms.GetReview(roomId)).Value!;
        return review.EventList.Single(e => e.Id == eventId);
    }

    [Fact]
    public async Task Su_kien_trong_ban_ghi_nhay_toi_dung_vi_tri_tinh_tu_luc_bat_dau_ghi()
    {
        var (world, roomId) = Room();
        using var _ = world;
        var recording = AddRecording(world, roomId, startSeconds: 60, stopSeconds: 300);
        var chat = Chat(world, roomId, seconds: 90);

        var target = await EventOf(world, roomId, chat);

        Assert.Equal(recording, target.SeekRecordingId);
        Assert.Equal(30_000, target.SeekOffsetMs);
    }

    [Fact]
    public async Task Vi_tri_nhay_tru_cac_doan_tam_dung_da_ket_thuc_truoc_su_kien()
    {
        var (world, roomId) = Room();
        using var _ = world;
        // Ghi từ giây 10; tạm dừng 20→50 (30 giây không vào video); tiếp tục; tạm dừng lần hai 80→100; tiếp tục.
        var recording = AddRecording(world, roomId, startSeconds: 10, stopSeconds: 200);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingPaused, 20);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingResumed, 50);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingPaused, 80);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingResumed, 100);
        var beforePause = Chat(world, roomId, 15);
        var afterFirstPause = Chat(world, roomId, 60);
        var afterBothPauses = Chat(world, roomId, 120);

        Assert.Equal(5_000, (await EventOf(world, roomId, beforePause)).SeekOffsetMs);
        Assert.Equal(20_000, (await EventOf(world, roomId, afterFirstPause)).SeekOffsetMs);   // (60-10) - 30
        // Thời gian THẬT đã ghi tới giây 120: 10→20 (10 s) + 50→80 (30 s) + 100→120 (20 s) = 60 s.
        Assert.Equal(60_000, (await EventOf(world, roomId, afterBothPauses)).SeekOffsetMs);
    }

    [Fact]
    public async Task Su_kien_xay_ra_luc_dang_tam_dung_khong_nhay_duoc()
    {
        var (world, roomId) = Room();
        using var _ = world;
        var recording = AddRecording(world, roomId, startSeconds: 10, stopSeconds: 200);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingPaused, 20);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingResumed, 50);
        var duringPause = Chat(world, roomId, 35);

        var target = await EventOf(world, roomId, duringPause);

        Assert.Null(target.SeekRecordingId);
        Assert.Null(target.SeekOffsetMs);
    }

    [Fact]
    public async Task Su_kien_truoc_khi_ghi_va_sau_khi_dung_khong_nhay_duoc()
    {
        var (world, roomId) = Room();
        using var _ = world;
        AddRecording(world, roomId, startSeconds: 100, stopSeconds: 160);
        var before = Chat(world, roomId, 50);
        var after = Chat(world, roomId, 400);

        Assert.Null((await EventOf(world, roomId, before)).SeekOffsetMs);
        Assert.Null((await EventOf(world, roomId, after)).SeekOffsetMs);
    }

    [Fact]
    public async Task Nhieu_ban_ghi_chon_dung_ban_ghi_chua_su_kien()
    {
        var (world, roomId) = Room();
        using var _ = world;
        var first = AddRecording(world, roomId, startSeconds: 10, stopSeconds: 40);
        var second = AddRecording(world, roomId, startSeconds: 60, stopSeconds: 100);
        var inFirst = Chat(world, roomId, 20);
        var between = Chat(world, roomId, 50);
        var inSecond = Chat(world, roomId, 70);

        var a = await EventOf(world, roomId, inFirst);
        var b = await EventOf(world, roomId, between);
        var c = await EventOf(world, roomId, inSecond);

        Assert.Equal((first, 10_000), (a.SeekRecordingId, a.SeekOffsetMs));
        Assert.Null(b.SeekRecordingId);
        Assert.Equal((second, 10_000), (c.SeekRecordingId, c.SeekOffsetMs));
    }

    [Fact]
    public async Task Ban_ghi_cu_khong_co_su_kien_ghi_hinh_van_nhay_theo_gio_bat_dau_ban_ghi()
    {
        var (world, roomId) = Room();
        using var _ = world;
        // Bản ghi tạo trước khi có tính năng tạm dừng: không có sự kiện ghi hình nào, không có giờ chốt.
        var recording = AddRecording(world, roomId, startSeconds: 100, withStartedEvent: false);
        var chat = Chat(world, roomId, 160);

        var target = await EventOf(world, roomId, chat);

        Assert.Equal(recording, target.SeekRecordingId);
        Assert.Equal(60_400, target.SeekOffsetMs); // gốc là lúc tạo Bản ghi (giây 99,6), không có sự kiện "bắt đầu" để neo
    }

    [Fact]
    public async Task Chinh_cac_dong_ghi_hinh_cung_nhay_duoc()
    {
        var (world, roomId) = Room();
        using var _ = world;
        var recording = AddRecording(world, roomId, startSeconds: 10, stopSeconds: 100);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingPaused, 40);
        Mark(world, roomId, recording, OnlineClassRoomEventType.RecordingResumed, 70);
        var review = (OnlineClassRoomReviewDto)(await world.Rooms.GetReview(roomId)).Value!;

        var paused = review.EventList.Single(e => e.EventType == "RecordingPaused");
        var resumed = review.EventList.Single(e => e.EventType == "RecordingResumed");

        Assert.Equal(30_000, paused.SeekOffsetMs);   // đúng điểm cắt
        Assert.Equal(30_000, resumed.SeekOffsetMs);  // phần video tiếp nối ngay sau điểm cắt
    }
}
