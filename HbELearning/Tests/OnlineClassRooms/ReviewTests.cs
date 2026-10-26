using HbELearning.Application.Common.Results;
using HbELearning.Application.DTOs;
using HbELearning.Tests.Support;

namespace HbELearning.Tests.OnlineClassRooms;

/// <summary>Trang xem lại một Buổi học: tóm tắt, các Bản ghi (kèm thời lượng, dung lượng) và Diễn biến, trong một truy vấn.</summary>
public class ReviewTests
{
    private static OnlineClassRoomReviewDto Review(Result r) => (OnlineClassRoomReviewDto)r.Value!;

    [Fact]
    public async Task Nguoi_chua_tung_vao_bi_403_khi_xem_lai()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Họp nội bộ", joined: new[] { world.Student }, recordingSizes: 1000);
        world.ActAs(world.Outsider);

        Assert.Equal(ResultKind.Forbidden, (await world.Rooms.GetReview(roomId)).Kind);
    }

    [Fact]
    public async Task Buoi_khong_ton_tai_tra_ve_khong_tim_thay()
    {
        using var world = ClassRoomWorld.Create();
        world.ActAs(world.Host);

        Assert.Equal(ResultKind.NotFound, (await world.Rooms.GetReview(9999)).Kind);
    }

    [Fact]
    public async Task Tom_tat_dem_dung_nguoi_tham_gia_tin_nhan_va_thoi_luong()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi tóm tắt", joined: new[] { world.Student }, duration: TimeSpan.FromMinutes(50));
        using (var db = world.NewDb())
        {
            // Thêm hai tin nhắn nữa (đã có sẵn một tin của chủ trì) và một lần vào lại của học viên.
            foreach (var text in new[] { "Em hỏi", "Em cảm ơn" })
                db.OnlineClassRoomEvent.Add(new HbELearning.Domain.Entities.OnlineClassRoomEvent
                {
                    OnlineClassRoomId = roomId, EventType = HbELearning.Domain.Enums.OnlineClassRoomEventType.ChatMessage,
                    UserId = world.Student.Id, UserName = world.Student.FullName, Message = text,
                });
            db.OnlineClassRoomEvent.Add(new HbELearning.Domain.Entities.OnlineClassRoomEvent
            {
                OnlineClassRoomId = roomId, EventType = HbELearning.Domain.Enums.OnlineClassRoomEventType.Joined,
                UserId = world.Student.Id, UserName = world.Student.FullName,
            });
            db.SaveChanges();
        }
        world.ActAs(world.Student);

        var review = Review(await world.Rooms.GetReview(roomId));

        Assert.Equal(TimeSpan.FromMinutes(50).TotalMilliseconds, review.DurationMs);
        Assert.Equal(2, review.ParticipantCount);   // chủ trì + học viên, không đếm lượt vào lại
        Assert.Equal(3, review.ChatMessageCount);
        Assert.Equal(world.Host.FullName, review.HostUserName);
    }

    [Fact]
    public async Task Tra_ve_day_du_ban_ghi_va_dien_bien_ke_ca_phan_nguoi_xem_vang_mat()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi hai phần", joined: new[] { world.Student }, recordingSizes: new long[] { 2_000, 5_000 });
        world.ActAs(world.Student);

        var review = Review(await world.Rooms.GetReview(roomId));

        Assert.Equal(2, review.RecordingList.Count);
        Assert.Equal(7_000, review.RecordingList.Sum(r => r.FileSizeBytes));
        Assert.All(review.RecordingList, r => Assert.Equal(60_000, r.DurationMs));
        Assert.Contains(review.EventList, e => e.EventType == "ChatMessage" && e.Message == "Xin chào cả lớp");
    }

    [Fact]
    public async Task Chi_chu_tri_va_admin_duoc_tai_xuong()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi tải về", joined: new[] { world.Student }, recordingSizes: 1000);

        world.ActAs(world.Host);
        Assert.True(Review(await world.Rooms.GetReview(roomId)).CanDownload);

        world.ActAs(world.Admin);
        Assert.True(Review(await world.Rooms.GetReview(roomId)).CanDownload);

        world.ActAs(world.Student);
        Assert.False(Review(await world.Rooms.GetReview(roomId)).CanDownload);
    }

    [Fact]
    public async Task Buoi_khong_co_ban_ghi_van_co_tom_tat_va_dien_bien()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi không ghi hình", joined: new[] { world.Student });
        world.ActAs(world.Student);

        var review = Review(await world.Rooms.GetReview(roomId));

        Assert.Empty(review.RecordingList);
        Assert.NotEmpty(review.EventList);
        Assert.True(review.ParticipantCount >= 1);
    }

    [Fact]
    public async Task Ban_ghi_cu_khong_co_thoi_luong_van_tra_ve_khong_loi()
    {
        using var world = ClassRoomWorld.Create();
        var roomId = world.AddEndedRoom("Buổi cũ", joined: new[] { world.Student }, recordingSizes: 4_000);
        using (var db = world.NewDb())
        {
            foreach (var recording in db.OnlineClassRoomRecording.Where(r => r.OnlineClassRoomId == roomId))
            { recording.DurationMs = null; recording.StoppedAt = null; }
            db.SaveChanges();
        }
        world.ActAs(world.Student);

        var recording1 = Assert.Single(Review(await world.Rooms.GetReview(roomId)).RecordingList);

        Assert.Null(recording1.DurationMs);
        Assert.Equal(4_000, recording1.FileSizeBytes);
    }
}
